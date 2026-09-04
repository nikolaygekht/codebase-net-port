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

        // One value inside the other constraint adds nothing to it.
        if (left.Kind == ConstraintKind.Equal && right.Accepts(left.Value!))
            return right;

        if (right.Kind == ConstraintKind.Equal && left.Accepts(right.Value!))
            return left;

        if (left.Kind == ConstraintKind.Equal || right.Kind == ConstraintKind.Equal)
            return null;

        // Two sets of struck-out values: whatever one of them lets through, the union lets through,
        // so only the values struck out by both remain struck out.
        if (IsUnbounded(left) && IsUnbounded(right))
        {
            return LeafConstraint.Range(
                null, null, [.. left.Exclusions.Where(value => Excludes(right, value))]);
        }

        // Two ranges only join into one where they overlap or touch; otherwise the union has a hole
        // in it and no single range describes it.
        if (left.Exclusions.Count > 0 || right.Exclusions.Count > 0 || !Meet(left, right))
            return null;

        return LeafConstraint.Range(
            Wider(left.Lower, right.Lower, isLower: true),
            Wider(left.Upper, right.Upper, isLower: false),
            []);
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

    /// <summary>Decides whether a constraint strikes out a value.</summary>
    private static bool Excludes(LeafConstraint constraint, ConstraintValue value) =>
        constraint.Exclusions.Any(other => other.CompareTo(value) == 0);

    /// <summary>Decides whether a range has no ends at all.</summary>
    private static bool IsUnbounded(LeafConstraint constraint) =>
        constraint.Lower is null && constraint.Upper is null;

    /// <summary>
    /// Decides whether two ranges overlap or touch, so that their union is one range.
    /// </summary>
    private static bool Meet(LeafConstraint left, LeafConstraint right) =>
        Reaches(left.Upper, right.Lower) && Reaches(right.Upper, left.Lower);

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
