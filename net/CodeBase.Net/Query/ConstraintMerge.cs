namespace CodeBase.Net.Query;

/// <summary>
/// Folds two constraints on one tag into one, so the tag is seeked once instead of twice.
///
/// This is the whole of what merging buys, and it buys nothing else: the records a plan reaches are
/// the same before and after, and what changes is how many descents it takes to reach them. The
/// reference states the purpose plainly -- "if a bitmap is (val &lt; 10 and val &gt; 5), we want to
/// create a simple bitmap between 5 and 10, instead of having 2 different bitmaps".
///
/// Where the reference has twelve routines folding one nullable slot into another, this has two
/// functions over a closed union, because the union has already made unrepresentable the six pairwise
/// exclusions its routines assert after every fold.
///
/// The two directions are not symmetric in what they can do. Two constraints joined by an and always
/// have a single answer -- the values both admit, which may be none. Two joined by an or often do
/// not: everything below one and everything above another is not a range, and no single constraint
/// describes it. Rather than invent a shape for those, this refuses them and lets the two leaves
/// stand; two seeks and a union is slower and exactly as correct.
///
/// Governing specification: QUERY-OPTIMIZER.md sections 5.3 and 5.4.
/// </summary>
internal static class ConstraintMerge
{
    /// <summary>
    /// Folds two constraints into the values both admit.
    /// </summary>
    /// <param name="left">One constraint.</param>
    /// <param name="right">The other.</param>
    /// <returns>The values both admit, which may be none.</returns>
    public static LeafConstraint And(LeafConstraint left, LeafConstraint right)
    {
        if (left.Kind == ConstraintKind.Empty || right.Kind == ConstraintKind.Empty)
            return LeafConstraint.Nothing;

        if (left.Kind == ConstraintKind.Equal)
            return right.Accepts(left.Value!) ? left : LeafConstraint.Nothing;

        if (right.Kind == ConstraintKind.Equal)
            return left.Accepts(right.Value!) ? right : LeafConstraint.Nothing;

        return Narrow(
            Narrower(left.Lower, right.Lower, isLower: true),
            Narrower(left.Upper, right.Upper, isLower: false),
            [.. left.Exclusions, .. right.Exclusions]);
    }

    /// <summary>
    /// Folds two constraints into the values either admits, where that is one constraint.
    /// </summary>
    /// <param name="left">One constraint.</param>
    /// <param name="right">The other.</param>
    /// <returns>The values either admits, or null where no single constraint describes them.</returns>
    /// <value>
    /// Refusing is not a gap to be filled later: two leaves that stay separate are seeked separately
    /// and their records united, which is the same answer. What refusing avoids is inventing a
    /// description for a set that has none -- and the reference's own routine for this is the one
    /// piece of the module its specification records as read for shape rather than transcribed, which
    /// is not a thing to build on.
    /// </value>
    public static LeafConstraint? Or(LeafConstraint left, LeafConstraint right)
    {
        if (left.Kind == ConstraintKind.Empty)
            return right;

        if (right.Kind == ConstraintKind.Empty)
            return left;

        // Two sets of struck-out values: whatever one of them lets through, the union lets through,
        // so only the values struck out by both remain struck out.
        if (IsUnbounded(left) && IsUnbounded(right))
        {
            return LeafConstraint.Range(
                null, null, [.. left.Exclusions.Where(value => Excludes(right, value))]);
        }

        if (left.Exclusions.Count > 0 || right.Exclusions.Count > 0)
            return null;

        // An exact value is a range whose ends have met, so it needs no rule of its own: a value
        // inside a range is a range the other one swallows, and a value sitting against an exclusive
        // end is two ranges that touch, which relaxes the end rather than leaving a hole at it.
        (Bound? leftLow, Bound? leftHigh) = Ends(left);
        (Bound? rightLow, Bound? rightHigh) = Ends(right);

        // Two ranges only join into one where they overlap or touch; otherwise the union has a hole
        // in it and no single range describes it.
        if (!Reaches(leftHigh, rightLow) || !Reaches(rightHigh, leftLow))
            return null;

        Bound? lower = Wider(leftLow, rightLow, isLower: true);
        Bound? upper = Wider(leftHigh, rightHigh, isLower: false);

        return lower is Bound low && upper is Bound high
            && low.Value.CompareTo(high.Value) == 0 && low.Inclusive && high.Inclusive
            ? LeafConstraint.Exactly(low.Value)
            : LeafConstraint.Range(lower, upper, []);
    }

    /// <summary>
    /// Builds a range from two bounds and a set of struck-out values, in its simplest true form.
    /// </summary>
    /// <value>
    /// Three rules turn a merged range into the canonical one, and the third is the one easy to
    /// forget. A range whose ends have crossed admits nothing. A range whose ends have met at one
    /// value, both inclusive, is that value. And a value struck out at an inclusive end moves the end
    /// past itself -- the only place the algebra strengthens an operator, and leaving it out returns
    /// exactly one record too many.
    /// </value>
    private static LeafConstraint Narrow(
        Bound? lower, Bound? upper, IReadOnlyList<ConstraintValue> exclusions)
    {
        List<ConstraintValue> kept = [];

        foreach (ConstraintValue value in exclusions)
        {
            if (lower is Bound low && low.Inclusive && value.CompareTo(low.Value) == 0)
            {
                lower = new Bound(low.Value, Inclusive: false);
                continue;
            }

            if (upper is Bound high && high.Inclusive && value.CompareTo(high.Value) == 0)
            {
                upper = new Bound(high.Value, Inclusive: false);
                continue;
            }

            // A value struck out from beyond either end was never in the range to begin with.
            if (Within(lower, upper, value) && !kept.Exists(other => other.CompareTo(value) == 0))
                kept.Add(value);
        }

        if (lower is Bound start && upper is Bound end)
        {
            int order = start.Value.CompareTo(end.Value);

            if (order > 0)
                return LeafConstraint.Nothing;

            if (order == 0)
            {
                return start.Inclusive && end.Inclusive
                    ? LeafConstraint.Exactly(start.Value)
                    : LeafConstraint.Nothing;
            }
        }

        return LeafConstraint.Range(lower, upper, kept);
    }

    /// <summary>
    /// Picks the bound that admits less.
    /// </summary>
    /// <param name="left">One bound, or null where that side is open.</param>
    /// <param name="right">The other.</param>
    /// <param name="isLower">Whether these are lower ends, where the greater value admits less.</param>
    /// <returns>The narrower bound.</returns>
    /// <value>
    /// A missing bound admits everything on its side, so it loses to any real one. Where two sit on
    /// the same value the exclusive one is narrower, which is why the two inclusivities are combined
    /// rather than one of them chosen.
    /// </value>
    private static Bound? Narrower(Bound? left, Bound? right, bool isLower)
    {
        if (left is not Bound a)
            return right;

        if (right is not Bound b)
            return left;

        int order = a.Value.CompareTo(b.Value);

        if (order == 0)
            return new Bound(a.Value, a.Inclusive && b.Inclusive);

        return (order > 0) == isLower ? left : right;
    }

    /// <summary>
    /// Picks the bound that admits more.
    /// </summary>
    /// <param name="left">One bound, or null where that side is open.</param>
    /// <param name="right">The other.</param>
    /// <param name="isLower">Whether these are lower ends, where the lesser value admits more.</param>
    /// <returns>The wider bound, which is none where either side was already open.</returns>
    /// <value>
    /// The mirror image, and the null rule mirrors with it: an open side already admits everything,
    /// so the union is open too. Where two sit on the same value the inclusive one is wider.
    /// </value>
    private static Bound? Wider(Bound? left, Bound? right, bool isLower)
    {
        if (left is not Bound a || right is not Bound b)
            return null;

        int order = a.Value.CompareTo(b.Value);

        if (order == 0)
            return new Bound(a.Value, a.Inclusive || b.Inclusive);

        return (order < 0) == isLower ? left : right;
    }

    /// <summary>Decides whether a value lies between two bounds.</summary>
    private static bool Within(Bound? lower, Bound? upper, ConstraintValue value)
    {
        if (lower is Bound low)
        {
            int order = value.CompareTo(low.Value);
            if (order < 0 || (order == 0 && !low.Inclusive))
                return false;
        }

        if (upper is Bound high)
        {
            int order = value.CompareTo(high.Value);
            if (order > 0 || (order == 0 && !high.Inclusive))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Reads a constraint as a pair of ends, so an exact value and a range are the same shape.
    /// </summary>
    private static (Bound? Lower, Bound? Upper) Ends(LeafConstraint constraint) =>
        constraint.Kind == ConstraintKind.Equal
            ? (new Bound(constraint.Value!, true), new Bound(constraint.Value!, true))
            : (constraint.Lower, constraint.Upper);

    /// <summary>Decides whether a constraint strikes out a value.</summary>
    private static bool Excludes(LeafConstraint constraint, ConstraintValue value) =>
        constraint.Exclusions.Any(other => other.CompareTo(value) == 0);

    /// <summary>Decides whether a constraint is a range with no ends at all.</summary>
    /// <value>
    /// The kind is part of the question and not a formality: an exact value keeps its value in its
    /// own field and leaves both ends null, so testing the ends alone would call it unbounded and
    /// send two exact values down the path meant for two sets of struck-out values -- where they
    /// would come back as everything.
    /// </value>
    private static bool IsUnbounded(LeafConstraint constraint) =>
        constraint.Kind == ConstraintKind.Range
        && constraint.Lower is null && constraint.Upper is null;

    /// <summary>
    /// Decides whether a range ending at one bound reaches a range starting at another.
    /// </summary>
    /// <value>
    /// Ends that meet at one value still join, provided at least one of them admits it: everything up
    /// to five and everything from five is everything, and so is everything below five together with
    /// everything from five. Only two ends that both exclude the same value leave a hole.
    /// </value>
    private static bool Reaches(Bound? upper, Bound? lower)
    {
        if (upper is not Bound end || lower is not Bound start)
            return true;

        int order = end.Value.CompareTo(start.Value);

        return order > 0 || (order == 0 && (end.Inclusive || start.Inclusive));
    }
}
