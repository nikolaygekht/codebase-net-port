using AwesomeAssertions;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Cdx;

/// <summary>
/// The contracts of the seek surface, which is what this step's public methods actually promise.
///
/// The corpus proves that a seek finds the right record. What it cannot state is the shape of the
/// answer: that a miss positions on nothing rather than on a neighbour, that the two are different
/// methods rather than one method with a status code, and that a search does not outlive the
/// position it was made for.
/// </summary>
[Trait("Layer", "Component")]
public sealed class TableSeekTests
{
    /// <summary>Four records over one character field, indexed in key order.</summary>
    private static readonly string[] Values = ["ALPHA", "BRAVO", "CHARLIE", "ALPHA"];

    private static readonly (string Key, uint Record)[] Entries =
        [("ALPHA", 1), ("ALPHA", 4), ("BRAVO", 2), ("CHARLIE", 3)];

    private static (CodeBaseEngine Engine, Table Table) Indexed()
    {
        (CodeBaseEngine engine, Table table) = Opened();
        table.SelectTag(table.Tags[0]);

        return (engine, table);
    }

    /// <summary>
    /// Opens the image with an encoding supplied, the way a host without a code-page provider must.
    /// </summary>
    /// <remarks>
    /// A seek encodes its value the way the field is stored, so it needs the table's encoding just
    /// as reading text does. This project registers no provider (ADR-17 leaves that to the host), so
    /// the unmarked table's cp437 is unavailable and the engine is told what to use instead. That is
    /// the documented escape hatch, exercised here rather than only described.
    /// </remarks>
    private static (CodeBaseEngine Engine, Table Table) Opened()
    {
        return IndexedTableImage.Open(Values, Entries, encoding: System.Text.Encoding.Latin1);
    }

    [Fact]
    public void Seek_WithNoTagSelected_IsRefusedRatherThanScanning()
    {
        // A seek needs an order to seek in. Falling back to a scan would answer the question, slowly
        // and by a route the caller did not ask for -- and the optimizer is built on the assumption
        // that a seek is a descent.
        (CodeBaseEngine engine, Table table) = Opened();
        using (engine)
        using (table)
        {
            Action act = () => table.Seek("ALPHA");

            act.Should().Throw<CodeBaseException>().WithMessage("*none is selected*");
        }
    }

    [Fact]
    public void SeekNext_WithNoSearchRunning_IsRefusedRatherThanGuessing()
    {
        (CodeBaseEngine engine, Table table) = Indexed();
        using (engine)
        using (table)
        {
            Action act = () => table.SeekNext();

            act.Should().Throw<CodeBaseException>().WithMessage("*no search to continue*");
        }
    }

    [Theory]
    [InlineData("Go")]
    [InlineData("Top")]
    [InlineData("Bottom")]
    [InlineData("SelectTag")]
    public void AMoveThatIsNotASeek_EndsTheSearch(string move)
    {
        // The rule that keeps SeekNext honest: it continues a search, and a search belongs to the
        // position it was made for. Left alive across a Go, it would answer a question about where
        // the caller used to be.
        (CodeBaseEngine engine, Table table) = Indexed();
        using (engine)
        using (table)
        {
            table.Seek("ALPHA").Should().Be(GoResult.Ok);

            switch (move)
            {
                case "Go": table.Go(1); break;
                case "Top": table.Top(); break;
                case "Bottom": table.Bottom(); break;
                default: table.SelectTag(table.Tags[0]); break;
            }

            Action act = () => table.SeekNext();

            act.Should().Throw<CodeBaseException>().WithMessage("*no search to continue*");
        }
    }

    [Fact]
    public void Seek_AMiss_LeavesTheCursorOnNoRecordRatherThanOnANeighbour()
    {
        (CodeBaseEngine engine, Table table) = Indexed();
        using (engine)
        using (table)
        {
            table.Seek("NOSUCHVALUE").Should().Be(GoResult.NoRecord);

            table.RecordNumber.Should().BeLessThan(1);
        }
    }

    [Fact]
    public void SeekAtOrAfter_TheSameMiss_LandsOnTheNeighbourAndSaysSo()
    {
        // The pair that justifies having two methods: identical input, deliberately different
        // answers. Rolling them into one would make every caller who wanted the first read a status
        // code to discover the cursor had moved.
        (CodeBaseEngine engine, Table table) = Indexed();
        using (engine)
        using (table)
        {
            // BZ sorts between BRAVO and CHARLIE, so there is a neighbour above it to land on.
            table.SeekAtOrAfter("BZ").Should().Be(SeekResult.After);

            table.RecordNumber.Should().Be(3, "CHARLIE is the first key above BZ");
        }
    }

    [Fact]
    public void SeekAtOrAfter_AValueAboveEveryKey_IsEndOfFile()
    {
        (CodeBaseEngine engine, Table table) = Indexed();
        using (engine)
        using (table)
        {
            table.SeekAtOrAfter("ZZZZZZZZ").Should().Be(SeekResult.Eof);
            table.Eof.Should().BeTrue();
        }
    }

    [Fact]
    public void SeekAtOrBefore_AValueBelowEveryKey_IsBeginningOfFile()
    {
        (CodeBaseEngine engine, Table table) = Indexed();
        using (engine)
        using (table)
        {
            table.SeekAtOrBefore("").Should().Be(SeekResult.Bof);
        }
    }

    [Fact]
    public void Seek_WithAValueTypeTheTagCannotTake_IsRefusedByName()
    {
        // A silent conversion would be a wrong record set, which this project ranks worst. The
        // message names the kind the tag holds so a caller can act on it.
        (CodeBaseEngine engine, Table table) = Indexed();
        using (engine)
        using (table)
        {
            Action act = () => table.Seek(42.0);

            act.Should().Throw<CodeBaseException>().WithMessage("*Character*");
        }
    }
}
