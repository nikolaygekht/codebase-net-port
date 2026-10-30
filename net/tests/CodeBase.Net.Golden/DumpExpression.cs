namespace CodeBase.Net.Golden;

/// <summary>
/// One expression the reference implementation compiled, and what it compiled to.
/// </summary>
/// <param name="Expression">The text that was compiled.</param>
/// <param name="Type">The type letter of the value the whole expression produces.</param>
/// <param name="Length">The declared length of that value.</param>
/// <param name="HasTrim">Whether the reference marked the expression as containing a trim.</param>
/// <param name="NodeCount">
/// The node count the dump states on its own line, which is read separately from the node lines so
/// that a reader which quietly dropped one cannot pass.
/// </param>
/// <param name="Nodes">The nodes, in the order the dump lists them, which is postfix with the root last.</param>
/// <param name="Value">
/// The value the reference produced, as raw result bytes, or null where none was recorded. The
/// clock-dependent section records none deliberately, because a value read off the clock would make
/// the corpus stop regenerating identically.
/// </param>
/// <param name="RecordValues">
/// The value per record, for an expression over fields, keyed by record number, from one compiled
/// expression evaluated record after record.
/// </param>
/// <param name="FreshValues">
/// The value per record where parsing the expression afresh for that record gave other bytes. The
/// reference never clears a computed node's null flag, so one compiled expression carries a null
/// forward from an earlier record; a fresh parse cannot. Empty where the two readings agree.
/// </param>
internal sealed record DumpExpression(
    string Expression,
    char Type,
    int Length,
    bool HasTrim,
    int NodeCount,
    IReadOnlyList<DumpNode> Nodes,
    byte[]? Value,
    IReadOnlyDictionary<int, byte[]> RecordValues,
    IReadOnlyDictionary<int, byte[]> FreshValues)
{
    /// <summary>Gets the value one record has when nothing evaluated before it can leak into it.</summary>
    /// <param name="record">The record number.</param>
    /// <returns>The fresh reading where it differs, otherwise the recorded one.</returns>
    /// <value>
    /// What this port is gated on (ADR-61): an answer that depends on which records were evaluated
    /// earlier is one an optimizer that evaluates a subset could never reproduce.
    /// </value>
    public byte[] FreshValueAt(int record) =>
        FreshValues.TryGetValue(record, out byte[]? fresh) ? fresh : RecordValues[record];
}
