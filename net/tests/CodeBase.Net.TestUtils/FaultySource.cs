using CodeBase.Net.IO;

namespace CodeBase.Net.TestUtils;

/// <summary>
/// A file that misbehaves, for the failures a real one cannot be made to produce on demand.
/// </summary>
/// <remarks>
/// Hand-written rather than mocked, and not by preference. A source reads into a
/// [c]Span[/c] of bytes, and a mocking library cannot express a parameter of that kind at all, so
/// the boundary that most wanted a mock is the one boundary that cannot have one. Asserting on
/// [c]IsDisposed[/c] afterwards is the better test anyway: it states the outcome rather than the
/// call that produced it.
/// </remarks>
internal sealed class FaultySource : IRandomAccessSource
{
    private readonly Fault fault;

    /// <summary>
    /// Initializes a new source that fails in the given way.
    /// </summary>
    /// <param name="length">The length the source claims to have.</param>
    /// <param name="fault">How reading it fails.</param>
    public FaultySource(long length, Fault fault)
    {
        Length = length;
        this.fault = fault;
    }

    /// <summary>
    /// The ways a source can fail.
    /// </summary>
    public enum Fault
    {
        /// <summary>
        /// Every read returns fewer bytes than asked for, as a network filesystem may.
        /// </summary>
        ShortRead,

        /// <summary>
        /// Every read fails outright.
        /// </summary>
        Throw,
    }

    /// <summary>
    /// Gets a value indicating whether the source has been closed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <inheritdoc/>
    public long Length { get; }

    /// <inheritdoc/>
    public int Read(long offset, Span<byte> buffer) => fault switch
    {
        Fault.ShortRead => 0,
        _ => throw new IOException("The device is not ready."),
    };

    /// <inheritdoc/>
    public void Dispose() => IsDisposed = true;
}

/// <summary>
/// A factory that hands out one source, whatever it is asked for.
/// </summary>
internal sealed class StubFactory : IRandomAccessSourceFactory
{
    private readonly IRandomAccessSource source;

    /// <summary>
    /// Initializes a new factory over the source it will return.
    /// </summary>
    /// <param name="source">What every open returns.</param>
    public StubFactory(IRandomAccessSource source) => this.source = source;

    /// <inheritdoc/>
    public IRandomAccessSource Open(string path) => source;
}

/// <summary>
/// A resolver that finds no companion, and fails if one is looked for when none should be.
/// </summary>
internal sealed class NoCompanions : ICompanionFileResolver
{
    /// <inheritdoc/>
    public string? Resolve(string tablePath, string extension) => null;
}
