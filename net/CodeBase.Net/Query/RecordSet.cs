namespace CodeBase.Net.Query;

/// <summary>
/// A set of record numbers, held as bits, which may stand for its own complement.
///
/// Record numbers count from one and there is no record zero, so the set is one bit wider than the
/// table and bit zero is never used. A bit set past the end of the table is discarded rather than
/// treated as an error, because a tag can legitimately hold keys for records the set was sized before.
///
/// The complement flag is what lets a set that holds nearly every record be built by naming the few
/// it leaves out. A term like [c]NAME &lt;&gt; "SMITH"[/c] admits everything except a handful of
/// records, and flagging those and inverting is both quicker to build and smaller to describe than
/// walking the whole tag. Combining two sets normalises the flag away first: the saving is in
/// building one, not in storing it, and an inverted set makes the combination rules four cases
/// instead of one for no benefit anyone can measure.
///
/// Governing specification: QUERY-OPTIMIZER.md section 1.1.
/// </summary>
internal sealed class RecordSet
{
    private readonly byte[] bits;

    private RecordSet(int recordCount, bool isComplement)
    {
        RecordCount = recordCount;
        IsComplement = isComplement;
        bits = new byte[(recordCount + 8) / 8];
    }

    /// <summary>Gets how many records the table held when the set was sized.</summary>
    public int RecordCount { get; }

    /// <summary>Gets a value indicating whether the bits name the records left out.</summary>
    public bool IsComplement { get; private set; }

    /// <summary>
    /// Gets how many records the set holds.
    /// </summary>
    public int Count
    {
        get
        {
            int held = 0;

            for (int record = 1; record <= RecordCount; record++)
            {
                if (Contains(record))
                    held++;
            }

            return held;
        }
    }

    /// <summary>
    /// Builds a set holding no records.
    /// </summary>
    /// <param name="recordCount">How many records the table holds.</param>
    /// <returns>The set.</returns>
    public static RecordSet Empty(int recordCount) => new(recordCount, isComplement: false);

    /// <summary>
    /// Builds a set holding every record.
    /// </summary>
    /// <param name="recordCount">How many records the table holds.</param>
    /// <returns>The set.</returns>
    public static RecordSet All(int recordCount) => new(recordCount, isComplement: true);

    /// <summary>
    /// Adds a record to whatever the bits are naming.
    /// </summary>
    /// <param name="record">The record number, counting from one.</param>
    /// <value>
    /// Naming, not holding: while the set stands for its complement this removes the record instead.
    /// Callers build a complement deliberately and then say so, which is why this is not two methods.
    /// </value>
    public void Flag(int record)
    {
        if (record >= 1 && record <= RecordCount)
            bits[record >> 3] |= (byte)(1 << (record & 7));
    }

    /// <summary>
    /// Declares that the bits name the records the set leaves out.
    /// </summary>
    public void MeaningTheRest() => IsComplement = true;

    /// <summary>
    /// Decides whether the set holds a record.
    /// </summary>
    /// <param name="record">The record number, counting from one.</param>
    /// <returns>True when the record is in the set.</returns>
    public bool Contains(int record)
    {
        if (record < 1 || record > RecordCount)
            return false;

        return ((bits[record >> 3] >> (record & 7)) & 1) != 0 != IsComplement;
    }

    /// <summary>
    /// Finds the next record the set holds, at or after a record number.
    /// </summary>
    /// <param name="from">Where to start looking, counting from one.</param>
    /// <returns>The record number, or zero when there is none.</returns>
    /// <value>
    /// The whole reason a bitset is worth building: a run of records the set does not hold costs
    /// nothing to pass over, where a scan would have read each one.
    /// </value>
    public int NextFrom(int from)
    {
        for (int record = Math.Max(from, 1); record <= RecordCount; record++)
        {
            if (Contains(record))
                return record;
        }

        return 0;
    }

    /// <summary>
    /// Keeps only the records both sets hold.
    /// </summary>
    /// <param name="other">The set to intersect with.</param>
    public void KeepBoth(RecordSet other)
    {
        Settle();
        other.Settle();

        for (int index = 0; index < bits.Length; index++)
            bits[index] &= other.bits[index];
    }

    /// <summary>
    /// Adds the records the other set holds.
    /// </summary>
    /// <param name="other">The set to union with.</param>
    public void KeepEither(RecordSet other)
    {
        Settle();
        other.Settle();

        for (int index = 0; index < bits.Length; index++)
            bits[index] |= other.bits[index];
    }

    /// <summary>
    /// Returns the records the set holds, in record-number order.
    /// </summary>
    /// <returns>The record numbers.</returns>
    public IEnumerable<int> Records()
    {
        for (int record = NextFrom(1); record != 0; record = NextFrom(record + 1))
            yield return record;
    }

    /// <summary>
    /// Turns a set that stands for its complement into one that names what it holds.
    /// </summary>
    /// <value>
    /// Inverting sets bit zero, which is not a record, and the spare bits in the last byte, which are
    /// past the end of the table. Neither is scrubbed, because neither can be seen: every way of
    /// reading this set goes through the range check in [c]Contains[/c], so a bit outside one to
    /// [c]RecordCount[/c] is not a record however it came to be set. Scrubbing them was written first
    /// and removed when breaking it deliberately changed no test -- the invariant lives in the range
    /// check and cannot live in two places at once.
    ///
    /// The one thing that would make them matter is reading the bytes directly, which counting the
    /// set bits a byte at a time would do. Anything doing that has to mask the last byte itself.
    /// </value>
    private void Settle()
    {
        if (!IsComplement)
            return;

        for (int index = 0; index < bits.Length; index++)
            bits[index] = (byte)~bits[index];

        IsComplement = false;
    }
}
