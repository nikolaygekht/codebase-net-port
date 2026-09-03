namespace CodeBase.Net.Golden;

/// <summary>
/// One compiled node, as the reference implementation's own dump records it.
///
/// The three shape fields are the ones the parse pass alone decides, and they survive overload
/// resolution untouched. The row and the length are what overload resolution decides, and the row
/// carries the result type with it, because a row is one overload and its return type is the table's.
/// So comparing rows compares types, given a transcribed table that has already been gated.
/// </summary>
/// <param name="Row">The function table row the node resolved to.</param>
/// <param name="Length">The declared length of the value it produces.</param>
/// <param name="ParameterCount">How many operands the node takes.</param>
/// <param name="SubtreeSize">How many nodes the node and its operands occupy.</param>
/// <param name="FieldNumber">Which field the node loads, counted from one, or zero for none.</param>
/// <param name="Immediate2">The second folded number, which only the conditional uses.</param>
internal readonly record struct DumpNode(
    int Row,
    int Length,
    int ParameterCount,
    int SubtreeSize,
    int FieldNumber,
    int Immediate2);
