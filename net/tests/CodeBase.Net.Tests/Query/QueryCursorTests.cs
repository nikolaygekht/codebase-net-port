using AwesomeAssertions;
using CodeBase.Net.Tests.Cdx;
using Xunit;

namespace CodeBase.Net.Tests.Query;

/// <summary>
/// How a query moves over its result: in record order or in a selected tag's, forwards and back,
/// off either end and in again, and what it counts.
/// </summary>
/// <value>
/// A table and its index built in memory, so every expectation is read off the data the test lays
/// down: which records the filter selects, in the order the records or the tag's keys put them.
/// A cursor that never reaches an end is the likeliest way for this code to break, so every walk
/// fails once it has visited more records than the table holds; a loop inside the query itself is
/// the watchdog's (ADR-70).
/// </value>
[Trait("Layer", "Component")]
public sealed class QueryCursorTests
{
    /// <summary>Record n holds the n-th value.</summary>
    private static readonly string[] Values = ["CHERRY", "APPLE", "BANANA", "APPLE", "DATE", "BANANA"];

    /// <summary>The tag's entries, in key order.</summary>
    private static readonly (string Key, uint Record)[] Entries =
    [
        ("APPLE", 2), ("APPLE", 4), ("BANANA", 3), ("BANANA", 6), ("CHERRY", 1), ("DATE", 5),
    ];

    private const string ApplesAndBananas = "TEXT = \"APPLE\" .OR. TEXT = \"BANANA\"";

    /// <summary>Selects record 1, which the filter above leaves out, and record 5.</summary>
    private const string CherriesAndDates = "TEXT = \"CHERRY\" .OR. TEXT = \"DATE\"";

    private const string Nothing = "TEXT = \"FIG\"";

    [Fact]
    public void WithNoTagSelectedTheResultComesInRecordOrder()
    {
        (CodeBaseEngine engine, Table table) = Open();
        using (engine)
        {
            CodeBase.Net.Query query = table.Query(ApplesAndBananas);

            Forwards(query).Should().Equal(2, 3, 4, 6);
            Backwards(query).Should().Equal(6, 4, 3, 2);
        }
    }

    [Fact]
    public void WithATagSelectedTheResultComesInTheTagsOrder()
    {
        (CodeBaseEngine engine, Table table) = Open();
        using (engine)
        {
            table.SelectTag(table.Tags[0]);
            CodeBase.Net.Query query = table.Query(ApplesAndBananas);

            Forwards(query).Should().Equal(2, 4, 3, 6);
            Backwards(query).Should().Equal(6, 3, 4, 2);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATermNoTagAnswersIsCheckedOnEveryCandidate(bool byTag)
    {
        // TEXT >= "B" is sought; RECNO() > 3 is checked per record: 1, 3, 5, 6 become 5, 6.
        (CodeBaseEngine engine, Table table) = Open();
        using (engine)
        {
            if (byTag)
                table.SelectTag(table.Tags[0]);

            CodeBase.Net.Query query = table.Query("TEXT >= \"B\" .AND. RECNO() > 3");

            Forwards(query).Should().BeEquivalentTo([5, 6]);
            query.Count.Should().Be(2);
        }
    }

    [Fact]
    public void ACountOfAFilterTheTagAnswersWhollyIsTheCandidatesCount()
    {
        (CodeBaseEngine engine, Table table) = Open();
        using (engine)
        {
            table.Query(ApplesAndBananas).Count.Should().Be(4);
        }
    }

    [Theory]
    [InlineData(false, 6, 4)]
    [InlineData(true, 6, 3)]
    public void SteppingBackInFromTheEndReachesTheLastRecordAndThenTheOneBefore(bool byTag, int last, int beforeLast)
    {
        (CodeBaseEngine engine, Table table) = Open();
        using (engine)
        {
            if (byTag)
                table.SelectTag(table.Tags[0]);

            CodeBase.Net.Query query = table.Query(ApplesAndBananas);

            query.Bottom().Should().Be(GoResult.Ok);
            query.Next().Should().Be(GoResult.NoRecord);
            query.Eof.Should().BeTrue();
            query.RecordNumber.Should().Be(0);
            query.Next().Should().Be(GoResult.NoRecord, "stepping further out of the end stays out");

            query.Previous().Should().Be(GoResult.Ok);
            query.Eof.Should().BeFalse();
            query.RecordNumber.Should().Be(last);

            query.Previous().Should().Be(GoResult.Ok);
            query.RecordNumber.Should().Be(beforeLast);
        }
    }

    [Theory]
    [InlineData(false, 2, 3)]
    [InlineData(true, 2, 4)]
    public void SteppingForwardInFromTheStartReachesTheFirstRecordAndThenTheOneAfter(bool byTag, int first, int afterFirst)
    {
        (CodeBaseEngine engine, Table table) = Open();
        using (engine)
        {
            if (byTag)
                table.SelectTag(table.Tags[0]);

            CodeBase.Net.Query query = table.Query(ApplesAndBananas);

            query.Top().Should().Be(GoResult.Ok);
            query.Previous().Should().Be(GoResult.NoRecord);
            query.Bof.Should().BeTrue();
            query.RecordNumber.Should().Be(0);
            query.Previous().Should().Be(GoResult.NoRecord, "stepping further out of the start stays out");

            query.Next().Should().Be(GoResult.Ok);
            query.Bof.Should().BeFalse();
            query.RecordNumber.Should().Be(first);

            query.Next().Should().Be(GoResult.Ok);
            query.RecordNumber.Should().Be(afterFirst);
        }
    }

    [Fact]
    public void SkipMovesByAsManyResultsAsItIsAskedAndReportsAnEnd()
    {
        (CodeBaseEngine engine, Table table) = Open();
        using (engine)
        {
            CodeBase.Net.Query query = table.Query(ApplesAndBananas);
            query.Top();

            query.Skip(2).Should().Be(SkipResult.Moved);
            query.RecordNumber.Should().Be(4);

            query.Skip(-1).Should().Be(SkipResult.Moved);
            query.RecordNumber.Should().Be(3);

            query.Skip(0).Should().Be(SkipResult.Moved);
            query.RecordNumber.Should().Be(3);

            query.Skip(10).Should().Be(SkipResult.Eof);
            query.Skip(-10).Should().Be(SkipResult.Bof);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheFirstRecordIsReachedFromEitherDirection(bool byTag)
    {
        (CodeBaseEngine engine, Table table) = Open();
        using (engine)
        {
            if (byTag)
                table.SelectTag(table.Tags[0]);

            CodeBase.Net.Query query = table.Query(CherriesAndDates);

            Forwards(query).Should().Equal(1, 5);
            Backwards(query).Should().Equal(5, 1);

            query.Top().Should().Be(GoResult.Ok);
            query.Previous().Should().Be(GoResult.NoRecord);
            query.Next().Should().Be(GoResult.Ok);
            query.RecordNumber.Should().Be(1, "stepping in from the start lands on the first match, record 1");
        }
    }

    [Fact]
    public void SkippingOneMovesForwardsByOneAndReportsTheEnd()
    {
        (CodeBaseEngine engine, Table table) = Open();
        using (engine)
        {
            CodeBase.Net.Query query = table.Query(ApplesAndBananas);
            query.Top();

            query.Skip(1).Should().Be(SkipResult.Moved);
            query.RecordNumber.Should().Be(3);

            query.Bottom();
            query.Skip(1).Should().Be(SkipResult.Eof);
            query.Eof.Should().BeTrue();
            query.Bof.Should().BeFalse();
        }
    }

    // The reference's rule (QUERY-OPTIMIZER.md section 7.5): a top or bottom that finds nothing leaves
    // the query past its end; a bottom, having run off the start, past its beginning as well; a top
    // only when the table is empty. Each test moves the other way first, so a flag left over from that
    // move is seen.

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATopThatFindsNothingIsPastTheEndAndNotTheBeginning(bool byTag)
    {
        (CodeBaseEngine engine, Table table) = Open();
        using (engine)
        {
            if (byTag)
                table.SelectTag(table.Tags[0]);

            CodeBase.Net.Query query = table.Query(Nothing);
            query.Bottom();

            query.Top().Should().Be(GoResult.NoRecord);
            query.Eof.Should().BeTrue();
            query.Bof.Should().BeFalse();
            query.RecordNumber.Should().Be(0);
            query.Count.Should().Be(0);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABottomThatFindsNothingIsPastBothEnds(bool byTag)
    {
        (CodeBaseEngine engine, Table table) = Open();
        using (engine)
        {
            if (byTag)
                table.SelectTag(table.Tags[0]);

            CodeBase.Net.Query query = table.Query(Nothing);
            query.Top();

            query.Bottom().Should().Be(GoResult.NoRecord);
            query.Eof.Should().BeTrue();
            query.Bof.Should().BeTrue();
            query.RecordNumber.Should().Be(0);
        }
    }

    [Fact]
    public void ATopOverAnEmptyTableIsPastBothEnds()
    {
        (CodeBaseEngine engine, Table table) = IndexedTableImage.Open([], []);
        using (engine)
        {
            CodeBase.Net.Query query = table.Query(Nothing);
            query.Bottom();

            query.Top().Should().Be(GoResult.NoRecord);
            query.Eof.Should().BeTrue();
            query.Bof.Should().BeTrue();
        }
    }

    [Fact]
    public void AFilterThatIsNotLogicalIsRefusedWhenTheQueryFirstMoves()
    {
        (CodeBaseEngine engine, Table table) = Open();
        using (engine)
        {
            CodeBase.Net.Query query = table.Query("TEXT");

            query.Filter.Should().Be("TEXT");
            query.Invoking(q => q.Top()).Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Relate);
        }
    }

    private static (CodeBaseEngine Engine, Table Table) Open() => IndexedTableImage.Open(Values, Entries);

    private static List<int> Forwards(CodeBase.Net.Query query) => Walk(query, query.Top, query.Next);

    private static List<int> Backwards(CodeBase.Net.Query query) => Walk(query, query.Bottom, query.Previous);

    /// <summary>
    /// Collects the records a walk visits, and fails at once on one that does not end.
    /// </summary>
    private static List<int> Walk(CodeBase.Net.Query query, Func<GoResult> start, Func<GoResult> step)
    {
        List<int> found = [];
        for (GoResult go = start(); go == GoResult.Ok; go = step())
        {
            found.Add(query.RecordNumber);
            if (found.Count > Values.Length)
                Assert.Fail($"The walk visited more records than the table holds: {string.Join(", ", found)}.");
        }

        return found;
    }
}
