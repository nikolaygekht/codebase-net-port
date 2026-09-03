using CodeBase.Net.Dbf;

namespace CodeBase.Net.Expressions;

/// <summary>
/// A field an expression names, together with its position in the table.
///
/// The definition carries everything the engine needs except the one thing it is not told: which
/// field of the table it is. That number is what the reference implementation stores on a field node
/// so the expression can be bound to another record of the same layout later (e4parse.c:2470), so it
/// travels with the definition rather than being looked up again.
/// </summary>
/// <param name="Number">The field's position in the table, counted from one.</param>
/// <param name="Definition">The field.</param>
internal readonly record struct FieldReference(int Number, FieldDefinition Definition);
