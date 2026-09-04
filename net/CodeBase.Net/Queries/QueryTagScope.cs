namespace CodeBase.Net.Queries;

/// <summary>
/// Which of a table's tags the query optimizer is allowed to answer a filter with.
/// </summary>
/// <value>
/// The reference implementation refuses four kinds of tag outright, and two of those refusals do not
/// apply to this library. It cannot convert a constant into an integer, currency or datetime key, so
/// it will not optimize on such a tag; this port has done exactly that since it gained typed seek by
/// value. And it refuses a collated tag because its own residual comparison uses byte order, which a
/// collated tag does not follow.
///
/// Both settings are checked the same way: the record set an optimized query returns must equal, in
/// the same order, the set a full scan of the same filter returns. Only the default can additionally
/// be compared against the reference's own answers, which is why it is the default.
/// </value>
public enum QueryTagScope
{
    /// <summary>
    /// Use only the tags the reference implementation would use.
    /// </summary>
    /// <value>
    /// Plain, unfiltered tags in machine order whose key type is not integer, currency or datetime.
    /// Record sets then match the reference's for the same filter, which is the only thing a
    /// compatibility gate can compare.
    /// </value>
    Compatible = 0,

    /// <summary>
    /// Use every tag this library can seek.
    /// </summary>
    /// <value>
    /// Adds the integer, currency, datetime and collated tags, which this library keys and seeks as
    /// exactly as it does any other. Filtered and unique tags stay excluded in both settings: a
    /// filtered tag indexes a subset of the table, so seeking it answers a different question, and a
    /// unique tag holds one key per group of equal keys, so it cannot list a range's records at all.
    /// </value>
    AllSeekable = 1,
}
