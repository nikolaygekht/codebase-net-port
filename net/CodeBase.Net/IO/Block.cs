namespace CodeBase.Net.IO;

/// <summary>
/// A run of bytes read from a file, and how much of the buffer holding it is actually the block.
///
/// The point of it is that **the buffer's length is not the block's length**. A leaf's key text grows
/// downwards from the end of the block, so where the block ends decides where every key in it is
/// rebuilt from; a buffer larger than the block — which is what any pool or any window holding more
/// than one block hands out — would move that end and produce wrong keys with nothing raising an
/// error. Carrying the length beside the buffer makes that arithmetic impossible to get wrong instead
/// of merely documented.
///
/// It also lets one buffer hold several blocks. A cached window can be wider than a block, and a
/// block inside it is then this type with a non-zero offset rather than a copy of those bytes.
///
/// [c]AsSpan[/c] and [c]Span[/c] are the only way to reach the bytes, so the offset is applied in one
/// place rather than at every call site.
/// </summary>
/// <param name="Buffer">The array the bytes live in, which may be longer than the block.</param>
/// <param name="Offset">Where the block starts in that array.</param>
/// <param name="Length">How many bytes the block is.</param>
internal readonly record struct Block(byte[] Buffer, int Offset, int Length)
{
    /// <summary>
    /// Wraps an array whose whole length is one block.
    /// </summary>
    /// <param name="buffer">The array, all of which is the block.</param>
    public Block(byte[] buffer)
        : this(buffer, 0, buffer.Length)
    {
    }

    /// <summary>Gets the block's bytes.</summary>
    public ReadOnlySpan<byte> Span => Buffer.AsSpan(Offset, Length);

    /// <summary>Gets the block's bytes for writing, which only whoever fills it should hold.</summary>
    public Span<byte> Writable => Buffer.AsSpan(Offset, Length);

    /// <summary>
    /// Gives part of the block, counted from the block's own start.
    /// </summary>
    /// <param name="start">Where to start, relative to the block and not to the buffer.</param>
    /// <param name="length">How many bytes.</param>
    /// <returns>That part of the block.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The range is not inside the block.</exception>
    public ReadOnlySpan<byte> AsSpan(int start, int length) =>
        Buffer.AsSpan(Offset, Length).Slice(start, length);

    /// <summary>
    /// Gives the rest of the block from a position, counted from the block's own start.
    /// </summary>
    /// <param name="start">Where to start, relative to the block and not to the buffer.</param>
    /// <returns>The block from there on.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The position is not inside the block.</exception>
    public ReadOnlySpan<byte> AsSpan(int start) => Buffer.AsSpan(Offset, Length)[start..];
}
