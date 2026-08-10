using CodeBase.Net.IO;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.IO;

/// <summary>
/// Runs the source contract against the in-memory double the other tests use.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class InMemorySourceContractTests : RandomAccessSourceContract
{
    /// <inheritdoc/>
    private protected override IRandomAccessSource CreateSource(byte[] bytes) => new InMemorySource(bytes);
}

/// <summary>
/// Runs the source contract against a real file.
///
/// The one place in the suite that touches a disk on purpose. Everything above the boundary is
/// tested without one; the boundary itself cannot be, and it is what the double is standing in for.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class FileRandomAccessSourceContractTests : RandomAccessSourceContract, IDisposable
{
    private readonly List<string> files = [];

    /// <inheritdoc/>
    private protected override IRandomAccessSource CreateSource(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllBytes(path, bytes);
        files.Add(path);

        return new FileRandomAccessSource(path);
    }

    /// <summary>
    /// Removes the temporary files the tests wrote.
    /// </summary>
    public void Dispose()
    {
        foreach (string path in files)
            File.Delete(path);
    }
}
