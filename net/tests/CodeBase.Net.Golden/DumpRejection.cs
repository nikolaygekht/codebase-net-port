namespace CodeBase.Net.Golden;

/// <summary>
/// One expression the generator put through the reference parser to see whether it was refused.
/// </summary>
/// <param name="Expression">The expression's text.</param>
/// <param name="Accepted">
/// Whether the reference implementation compiled it after all. A case can be in the section and be
/// accepted: the generator records the answer rather than assuming it, and two of the cases turn out
/// to be restrictions the source describes and the library does not enforce.
/// </param>
/// <param name="Code">The code the reference implementation refused it with.</param>
internal readonly record struct DumpRejection(string Expression, bool Accepted, int Code);
