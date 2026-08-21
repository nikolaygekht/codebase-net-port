namespace CodeBase.Net.IO;

/// <summary>
/// A source that can say whether it was opened so that nobody else may write the file.
///
/// Separate from [c]IRandomAccessSource[/c] on purpose. That interface is deliberately two members
/// wide so a test can stand a byte array in for a file in three lines, and a third member would be
/// paid for by every fake in the suite while mattering to exactly one caller — the cache, deciding
/// whether [c]BlockCacheMode.WhenExclusive[/c] permits it to keep anything.
///
/// A source that does not implement this is treated as shared, which is the safe answer.
/// </summary>
internal interface IExclusiveAccess
{
    /// <summary>
    /// Gets a value indicating whether the file was opened excluding other writers.
    /// </summary>
    /// <value>
    /// False for every source this library opens today: files are opened [c]FileShare.ReadWrite[/c]
    /// because these files are normally open elsewhere, and excluding a writer needs the locking
    /// capability. It is here so the policy can be ported and proved now rather than invented later.
    /// </value>
    bool ExcludesOtherWriters { get; }
}
