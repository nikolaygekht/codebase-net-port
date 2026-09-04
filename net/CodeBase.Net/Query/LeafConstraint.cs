namespace CodeBase.Net.Query;

/// <summary>
/// One end of a range, and whether the value itself is in it.
/// </summary>
/// <param name="Value">The value the range stops at.</param>
/// <param name="Inclusive">Whether that value is itself admitted.</param>
internal readonly record struct Bound(ConstraintValue Value, bool Inclusive);

/// <summary>What shape a constraint has taken.</summary>
internal enum ConstraintKind
{
    /// <summary>Nothing can satisfy it, and that was proved without reading anything.</summary>
    Empty,

    /// <summary>Exactly one value.</summary>
    Equal,

    /// <summary>A range, either end of which may be open, less any values excluded from it.</summary>
    Range,
}

/// <summary>
/// What one tag is constrained to by the terms of a filter.
///
/// The reference keeps six nullable slots -- less-than, less-or-equal, greater-than,
/// greater-or-equal, equal, and a list of not-equals -- and asserts six pairwise exclusions between
/// them after every merge. Those assertions say that a normalised constraint is really only ever one
/// of three things, so it is three things here and the assertions are unrepresentable rather than
/// checked: an exact value, a range with at most one bound at each end and any values struck out of
/// it, or nothing at all.
///
/// Exclusions live on the range rather than beside it because the reference's own invariants let them
/// coexist with bounds, and its evaluation walks the range and then removes each excluded value.
///
/// Governing specification: QUERY-OPTIMIZER.md sections 5.3 and 11.2.
/// </summary>
internal sealed class LeafConstraint
{
    private static readonly ConstraintValue[] None = [];

    private LeafConstraint(
        ConstraintKind kind, ConstraintValue? value, Bound? lower, Bound? upper,
        IReadOnlyList<ConstraintValue> exclusions)
    {
        Kind = kind;
        Value = value;
        Lower = lower;
        Upper = upper;
        Exclusions = exclusions;
    }

    /// <summary>Gets a constraint nothing can satisfy.</summary>
    public static LeafConstraint Nothing { get; } =
        new(ConstraintKind.Empty, null, null, null, None);

    /// <summary>Gets a constraint everything satisfies.</summary>
    public static LeafConstraint Anything { get; } =
        new(ConstraintKind.Range, null, null, null, None);

    /// <summary>Gets which of the three shapes this has taken.</summary>
    public ConstraintKind Kind { get; }

    /// <summary>Gets the one value admitted, where the kind is an exact value.</summary>
    public ConstraintValue? Value { get; }

    /// <summary>Gets the lower end of the range, or null where it is open.</summary>
    public Bound? Lower { get; }

    /// <summary>Gets the upper end of the range, or null where it is open.</summary>
    public Bound? Upper { get; }

    /// <summary>Gets the values struck out of the range.</summary>
    public IReadOnlyList<ConstraintValue> Exclusions { get; }

    /// <summary>
    /// Builds a constraint admitting one value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The constraint.</returns>
    public static LeafConstraint Exactly(ConstraintValue value) =>
        new(ConstraintKind.Equal, value, null, null, None);

    /// <summary>
    /// Builds a constraint admitting everything from a value upwards.
    /// </summary>
    /// <param name="value">The lower end.</param>
    /// <param name="inclusive">Whether the value itself is admitted.</param>
    /// <returns>The constraint.</returns>
    public static LeafConstraint From(ConstraintValue value, bool inclusive) =>
        new(ConstraintKind.Range, null, new Bound(value, inclusive), null, None);

    /// <summary>
    /// Builds a constraint admitting everything up to a value.
    /// </summary>
    /// <param name="value">The upper end.</param>
    /// <param name="inclusive">Whether the value itself is admitted.</param>
    /// <returns>The constraint.</returns>
    public static LeafConstraint To(ConstraintValue value, bool inclusive) =>
        new(ConstraintKind.Range, null, null, new Bound(value, inclusive), None);

    /// <summary>
    /// Builds a constraint admitting everything except one value.
    /// </summary>
    /// <param name="value">The value to strike out.</param>
    /// <returns>The constraint.</returns>
    public static LeafConstraint Excluding(ConstraintValue value) =>
        new(ConstraintKind.Range, null, null, null, [value]);

    /// <summary>
    /// Decides whether a key value satisfies this constraint.
    /// </summary>
    /// <param name="key">The value a record's key holds.</param>
    /// <returns>True when the record is in the set this describes.</returns>
    /// <value>
    /// What the constraint [i]means[/i], independent of how it will be reached. Evaluating it is not
    /// how a query is answered -- the point of the optimizer is to seek instead of testing every
    /// record -- but it is how the constraint can be checked against what the filter itself says, and
    /// how a merge can be checked for having changed nothing.
    /// </value>
    public bool Accepts(ConstraintValue key)
    {
        if (Kind == ConstraintKind.Empty)
            return false;

        if (Kind == ConstraintKind.Equal)
            return key.CompareTo(Value!) == 0;

        if (Lower is Bound low)
        {
            int order = key.CompareTo(low.Value);
            if (order < 0 || (order == 0 && !low.Inclusive))
                return false;
        }

        if (Upper is Bound high)
        {
            int order = key.CompareTo(high.Value);
            if (order > 0 || (order == 0 && !high.Inclusive))
                return false;
        }

        foreach (ConstraintValue excluded in Exclusions)
        {
            if (key.CompareTo(excluded) == 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns the constraint as text, for a failure message.
    /// </summary>
    /// <returns>A readable form.</returns>
    public override string ToString() => Kind switch
    {
        ConstraintKind.Empty => "nothing",
        ConstraintKind.Equal => $"= {Value}",
        _ => string.Join(
            " and ",
            new[]
            {
                Lower is Bound l ? (l.Inclusive ? $">= {l.Value}" : $"> {l.Value}") : null,
                Upper is Bound u ? (u.Inclusive ? $"<= {u.Value}" : $"< {u.Value}") : null,
                Exclusions.Count == 0 ? null : "not " + string.Join(", ", Exclusions),
            }.Where(part => part is not null)) is { Length: > 0 } text
            ? text
            : "anything",
    };
}
