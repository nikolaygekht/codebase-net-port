namespace CodeBase.Net.Golden;

/// <summary>
/// One compiled node of a filter, together with the tag the reference matched to it.
/// </summary>
/// <param name="Row">The function table row the node resolved to.</param>
/// <param name="Length">The declared length of the value it produces.</param>
/// <param name="ParameterCount">How many operands the node takes.</param>
/// <param name="SubtreeSize">How many nodes the node and its operands occupy.</param>
/// <param name="FieldNumber">Which field the node loads, counted from one, or zero for none.</param>
/// <param name="Tag">The tag whose key expression equals the subtree rooted here, or null for none.</param>
internal readonly record struct DumpQueryNode(
    int Row,
    int Length,
    int ParameterCount,
    int SubtreeSize,
    int FieldNumber,
    string? Tag);

/// <summary>
/// The candidate bitset as the reference's optimizer left it.
///
/// [c]IsComplement[/c] is kept beside the bits rather than folded into them, because a set stored as
/// its own complement is a different decision from the same set stored directly -- the reference
/// flags the complement whenever a range covers more than half a tag. What a gate compares is
/// [c]Effective[/c]; the flag is recorded so the decision is visible and never asserted, since the
/// port does not implement the go-around.
/// </summary>
/// <param name="Present">Whether a bitset was built at all. Absent means nothing was optimized.</param>
/// <param name="FlagCount">The bitset's declared size, one more than the record count.</param>
/// <param name="IsComplement">Whether the bits mean the complement of the set.</param>
/// <param name="RawBits">The record numbers whose bit is set, as stored.</param>
internal sealed record DumpFlagSet(
    bool Present,
    int FlagCount,
    bool IsComplement,
    IReadOnlyList<int> RawBits)
{
    /// <summary>
    /// Returns the records the bitset actually selects, in record-number order.
    /// </summary>
    /// <param name="recordCount">How many records the table holds.</param>
    /// <returns>The selected record numbers.</returns>
    public IReadOnlyList<int> Effective(int recordCount)
    {
        if (!Present)
            return Enumerable.Range(1, recordCount).ToArray();

        HashSet<int> raw = [.. RawBits];
        return [.. Enumerable.Range(1, recordCount).Where(r => raw.Contains(r) != IsComplement)];
    }
}

/// <summary>
/// One filter, and everything the reference decided and returned for it.
///
/// The two record sequences are the heart of it. [c]Optimized[/c] is what the reference's optimizer
/// returned; [c]Scanned[/c] is what the same filter returns with the optimizer disabled, which is
/// the reference's own scan truth. They are equal for every case in the corpus but one, and that one
/// is the defect at m4map.c:216 demonstrating itself -- see [c]Agrees[/c].
/// </summary>
/// <param name="Filter">The filter expression, as it was set.</param>
/// <param name="Note">What the case is for, copied from the generator.</param>
/// <param name="FullyMapped">Whether the reference considered the bitmap to answer the whole filter.</param>
/// <param name="BitmapsFreed">Whether the reference gave up on bitmapping this filter.</param>
/// <param name="Nodes">The compiled filter, with the tag matched to each node.</param>
/// <param name="Set">The candidate bitset, taken immediately after the query was prepared.</param>
/// <param name="SetAfterWalk">The same bitset after the whole result was walked.</param>
/// <param name="SetStable">Whether those two are the same, which the dump answers rather than assumes.</param>
/// <param name="Optimized">The records the optimizer returned, in record-number order.</param>
/// <param name="Count">What the reference's own count returned.</param>
/// <param name="OrderingTag">The tag selected for the tag-ordered run, or null if there was none.</param>
/// <param name="ByTag">The records returned with that tag selected, in tag order.</param>
/// <param name="Scanned">The records the same filter returns with the optimizer disabled.</param>
/// <param name="Agrees">Whether the reference's two readings of this filter returned the same records.</param>
/// <param name="DeclaredAgrees">What the dump itself said about that, so the reader can be checked against it.</param>
internal sealed record DumpQueryCase(
    string Filter,
    string Note,
    bool FullyMapped,
    bool BitmapsFreed,
    IReadOnlyList<DumpQueryNode> Nodes,
    DumpFlagSet? Set,
    DumpFlagSet? SetAfterWalk,
    bool SetStable,
    IReadOnlyList<int> Optimized,
    int Count,
    string? OrderingTag,
    IReadOnlyList<int> ByTag,
    IReadOnlyList<int> Scanned,
    bool Agrees,
    bool DeclaredAgrees)
{
    /// <summary>
    /// Gets a value indicating whether the reference optimized any part of this filter.
    /// </summary>
    public bool Optimized_Anything => Set is { Present: true };

    /// <summary>
    /// Gets the records this case is measured against.
    /// </summary>
    /// <value>
    /// The reference's optimized answer where its two readings agree, and its scan truth where they
    /// do not -- because a disagreement is the reference demonstrating a defect on that filter, and
    /// the port implements the intent instead. Reading the expectation from the dump is what keeps
    /// that from being a hand-written exception list that could silently widen.
    /// </value>
    public IReadOnlyList<int> Expected => Agrees ? Optimized : Scanned;
}
