namespace CodeBase.Net.Memo;

/// <summary>
/// One memo entry read back: what it declared itself to be, and its bytes.
///
/// A record with no memo yields an empty entry rather than nothing at all, because the format draws
/// no line between an absent memo and an empty one and neither does this.
/// </summary>
internal readonly struct MemoEntry
{
    /// <summary>
    /// What a record with no memo reads as.
    /// </summary>
    public static readonly MemoEntry Absent = new(MemoType.Text, []);

    /// <summary>
    /// Initializes a new entry.
    /// </summary>
    /// <param name="type">What the entry declares itself to hold.</param>
    /// <param name="payload">The entry's bytes.</param>
    public MemoEntry(MemoType type, byte[] payload)
    {
        Type = type;
        Payload = payload;
    }

    /// <summary>
    /// Gets what the entry declares itself to hold.
    /// </summary>
    /// <value>Text for an absent memo, which is what an empty one would also say.</value>
    public MemoType Type { get; }

    /// <summary>
    /// Gets the entry's bytes.
    /// </summary>
    /// <value>Empty for an absent memo. Never null.</value>
    public byte[] Payload { get; }
}
