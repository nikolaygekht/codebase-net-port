using CodeBase.Net.Cdx;

namespace CodeBase.Net.Query;

/// <summary>
/// Turns one leaf of a plan into the records it selects, by seeking its tag.
///
/// This is where the optimizer earns its name. A constraint names a contiguous run of a tag's keys,
/// so reaching them costs one descent and then a walk of exactly the run -- where answering the same
/// term by reading records costs the whole table.
///
/// The reference reaches a range by seeking both of its ends and walking between the two positions,
/// and it reaches two of its four seek positions by inverting the tag and seeking forwards on the
/// reversed view. This port seeks the lower end and walks up, testing each key against the upper
/// bound as it goes, which reaches the same records and needs neither the inversion nor a second
/// descent. The set is what a bitmap is; the order the bits were set in is not part of it.
///
/// Governing specification: QUERY-OPTIMIZER.md sections 6.1 to 6.3.
/// </summary>
internal static class ConstraintSeeker
{
    /// <summary>
    /// Works out which records satisfy a constraint.
    /// </summary>
    /// <param name="constraint">What the tag is constrained to.</param>
    /// <param name="probe">The tag's keys.</param>
    /// <param name="recordCount">How many records the table holds.</param>
    /// <returns>The records the constraint admits.</returns>
    public static RecordSet Evaluate(LeafConstraint constraint, ITagProbe probe, int recordCount)
    {
        if (constraint.Kind == ConstraintKind.Empty)
            return RecordSet.Empty(recordCount);

        if (constraint.Kind == ConstraintKind.Equal)
            return Matching(probe.KeyFor(constraint.Value!), probe, recordCount);

        // A constraint that only strikes values out is almost the whole table, so it is cheaper to
        // name what it leaves out and say so than to walk every key in the tag. This is the one place
        // the complement flag is built rather than merely carried.
        if (constraint.Lower is null && constraint.Upper is null && constraint.Exclusions.Count > 0)
        {
            RecordSet excluded = RecordSet.Empty(recordCount);

            foreach (ConstraintValue value in constraint.Exclusions)
            {
                foreach (int record in Matching(probe.KeyFor(value), probe, recordCount).Records())
                    excluded.Flag(record);
            }

            excluded.MeaningTheRest();
            return excluded;
        }

        return Between(constraint, probe, recordCount);
    }

    /// <summary>
    /// Collects every record whose key matches a value.
    /// </summary>
    /// <value>
    /// Equal keys sit together, so this is the range walk with both ends the same value -- and where
    /// the value is a short character constant, "matches" means begins with, which is how one
    /// constraint reaches a whole group.
    /// </value>
    private static RecordSet Matching(KeySearch wanted, ITagProbe probe, int recordCount)
    {
        RecordSet found = RecordSet.Empty(recordCount);

        if (!probe.SeekAtOrAfter(wanted))
            return found;

        do
        {
            if (wanted.Compare(probe.Current.Key) != 0)
                break;

            found.Flag((int)probe.Current.Record);
        }
        while (probe.Step());

        return found;
    }

    /// <summary>
    /// Walks a range from its lower end and collects what falls inside it.
    /// </summary>
    /// <value>
    /// The lower end is where the descent lands: at the first key at or after the bound, or at the
    /// tag's lowest key when the range is open below. An exclusive bound is not seeked differently --
    /// the walk simply steps over the keys that equal it, which is the same records and one less
    /// thing to get wrong than a second seek position.
    ///
    /// The upper end is tested per key rather than seeked, so a range whose upper bound sits below
    /// its lower one ends immediately and correctly: the first key tested is already past it. That is
    /// the empty range, and it needs no rule of its own.
    /// </value>
    private static RecordSet Between(LeafConstraint constraint, ITagProbe probe, int recordCount)
    {
        RecordSet found = RecordSet.Empty(recordCount);

        bool landed = constraint.Lower is Bound low
            ? probe.SeekAtOrAfter(probe.KeyFor(low.Value))
            : probe.First();

        if (!landed)
            return found;

        KeySearch? lower = constraint.Lower is Bound bound ? probe.KeyFor(bound.Value) : null;
        KeySearch? upper = constraint.Upper is Bound top ? probe.KeyFor(top.Value) : null;
        KeySearch[] excluded = [.. constraint.Exclusions.Select(probe.KeyFor)];

        do
        {
            byte[] key = probe.Current.Key;

            if (upper is KeySearch limit)
            {
                int order = limit.Compare(key);
                if (order > 0 || (order == 0 && !constraint.Upper!.Value.Inclusive))
                    break;
            }

            if (lower is KeySearch start && !constraint.Lower!.Value.Inclusive && start.Compare(key) == 0)
                continue;

            if (IsExcluded(excluded, key))
                continue;

            found.Flag((int)probe.Current.Record);
        }
        while (probe.Step());

        return found;
    }

    /// <summary>
    /// Decides whether a key is one of the values struck out of a range.
    /// </summary>
    private static bool IsExcluded(KeySearch[] excluded, ReadOnlySpan<byte> key)
    {
        foreach (KeySearch value in excluded)
        {
            if (value.Compare(key) == 0)
                return true;
        }

        return false;
    }
}
