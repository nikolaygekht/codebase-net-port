namespace CodeBase.Net.Queries;

/// <summary>
/// Which of a table's tags the query optimizer is allowed to answer a filter with.
/// </summary>
/// <value>
/// The reference implementation, as it is built, refuses three kinds of tag: integer, currency and
/// datetime tags, filtered tags, and unique tags. The first refusal does not apply to this library: the
/// reference cannot convert a constant into those keys, and this port has done exactly that since it
/// gained typed seek by value. A collated tag goes the other way. The reference uses one, although its
/// filter compares in byte order and the tag does not, and returns the wrong records; this library
/// refuses it in both settings (ADR-59).
///
/// Both settings are checked the same way: the record set an optimized query returns must equal, in
/// the same order, the set a full scan of the same filter returns. Only the default can additionally
/// be compared against the reference's own answers, which is why it is the default.
/// </value>
public enum QueryTagScope
{
    /// <summary>
    /// Use the tags the reference implementation would use, except a collated one.
    /// </summary>
    /// <value>
    /// Plain, unfiltered tags in machine order whose key type is not integer, currency or datetime.
    /// Record sets then match the reference's for the same filter wherever the reference answers it
    /// correctly, which is the only thing a compatibility gate can compare.
    /// </value>
    Compatible = 0,

    /// <summary>
    /// Use every tag this library can seek without changing the answer.
    /// </summary>
    /// <value>
    /// Adds the integer, currency and datetime tags, which this library keys and seeks as exactly as it
    /// does any other. Filtered, unique and collated tags stay excluded in both settings: a filtered tag
    /// indexes a subset of the table, so seeking it answers a different question; a unique tag holds
    /// one key per group of equal keys, so it cannot list a range's records at all; and a collated tag
    /// orders its keys differently from the byte order a filter compares in.
    /// </value>
    AllSeekable = 1,
}
