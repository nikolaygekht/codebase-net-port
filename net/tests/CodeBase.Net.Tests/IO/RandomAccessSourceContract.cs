using AwesomeAssertions;
using CodeBase.Net.IO;
using Xunit;

namespace CodeBase.Net.Tests.IO;

/// <summary>
/// The promises every source keeps, asserted against each implementation of one.
///
/// A hand-written test double has a weakness a generated mock does not: it is a second
/// implementation of the contract, written from memory, and nothing makes it agree with the real
/// one. A double that returns a whole buffer where a file would return a partial read gives every
/// test above it false confidence.
///
/// So the promises live here once and each implementation is run through them. If the in-memory
/// source and the file disagree about what happens at the end of a file, this fails rather than the
/// suite quietly resting on the difference.
/// </summary>
public abstract class RandomAccessSourceContract
{
    /// <summary>
    /// Creates a source over the given bytes.
    /// </summary>
    /// <param name="bytes">The file contents.</param>
    /// <returns>The source, which the test will dispose.</returns>
    private protected abstract IRandomAccessSource CreateSource(byte[] bytes);

    [Fact]
    public void Length_ReportsTheNumberOfBytesInTheFile()
    {
        using IRandomAccessSource source = CreateSource(new byte[37]);

        source.Length.Should().Be(37);
    }

    [Fact]
    public void Length_OfAnEmptyFile_IsZero()
    {
        using IRandomAccessSource source = CreateSource([]);

        source.Length.Should().Be(0);
    }

    [Fact]
    public void Read_FillsTheBufferFromTheGivenOffset()
    {
        using IRandomAccessSource source = CreateSource([10, 20, 30, 40, 50]);
        byte[] buffer = new byte[3];

        int read = source.Read(1, buffer);

        read.Should().Be(3);
        buffer.Should().Equal(20, 30, 40);
    }

    [Fact]
    public void Read_AskedForMoreThanRemains_ReturnsOnlyWhatRemains()
    {
        // The behaviour the open path has to allow for: a read is not a promise of a full buffer.
        using IRandomAccessSource source = CreateSource([10, 20, 30, 40, 50]);
        byte[] buffer = new byte[4];

        int read = source.Read(3, buffer);

        read.Should().Be(2);
        buffer[..2].Should().Equal(40, 50);
    }

    [Fact]
    public void Read_AtTheEndOfTheFile_ReturnsNothing()
    {
        using IRandomAccessSource source = CreateSource([10, 20, 30]);

        source.Read(3, new byte[4]).Should().Be(0);
    }

    [Fact]
    public void Read_PastTheEndOfTheFile_ReturnsNothing()
    {
        using IRandomAccessSource source = CreateSource([10, 20, 30]);

        source.Read(99, new byte[4]).Should().Be(0);
    }

    [Fact]
    public void Read_IntoAnEmptyBuffer_ReturnsNothing()
    {
        using IRandomAccessSource source = CreateSource([10, 20, 30]);

        source.Read(0, []).Should().Be(0);
    }

    [Fact]
    public void Dispose_CanBeCalledMoreThanOnce()
    {
        IRandomAccessSource source = CreateSource([1, 2, 3]);

        source.Dispose();

        Action act = source.Dispose;
        act.Should().NotThrow();
    }
}
