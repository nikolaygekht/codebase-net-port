using System.Text;
using AwesomeAssertions;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// A seek by text is taken by every character tag in the corpus and refused by every other one.
///
/// Text sought in a numeric tag used to be read as a numeric field's stored bytes, and unparseable
/// text reads as zero, so the seek went to the key for zero and reported a plausible answer. Every
/// other kind failed in its own way. Now the text overloads require character keys, as the typed
/// overloads already required theirs (ADR-58).
///
/// Which tags hold character keys is taken from the reference, never from the port: the field's type
/// in the table's dump for a tag over a bare field, the compiled type in the expression dump for a tag
/// over an expression, and for the one expression that dump does not list, the return type the
/// function table gives its outer function.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class SeekTypeGoldenTests
{
    private static readonly CorpusExpressionDump Expressions = CorpusExpressionDump.Load();

    private static readonly CorpusFunctionsDump Functions = CorpusFunctionsDump.Load();

    /// <summary>Every tag of every corpus table that carries a compound index.</summary>
    public static TheoryData<string, string> EveryTag()
    {
        TheoryData<string, string> data = [];
        foreach (string table in Corpus.TableNames.Where(t => File.Exists(Path.Combine(Corpus.Root, t + ".cdx"))))
        {
            foreach (DumpIndexTag tag in CorpusIndexDump.Load(table + ".cdx").RealTags)
                data.Add(table, tag.Name.TrimEnd());
        }

        return data;
    }

    [Fact]
    public void TheGateCoversEveryTagInTheCorpusAndBothOutcomes()
    {
        List<(string Table, string Tag)> tags = [.. EveryTag().Select(row => row.Data)];

        tags.Should().HaveCount(55);
        tags.Count(t => ReferenceType(t.Table, t.Tag) == 'C').Should().BeGreaterThan(0);
        tags.Count(t => ReferenceType(t.Table, t.Tag) != 'C').Should().BeGreaterThan(0);
    }

    [Theory]
    [MemberData(nameof(EveryTag))]
    public void AStringSeek_IsTakenByACharacterTagAndRefusedByEveryOther(string tableName, string tagName)
    {
        char type = ReferenceType(tableName, tagName);

        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));
        table.SelectTag(table.Tags[tagName]);

        Func<Table, object>[] seeks =
        [
            t => t.Seek("A"),
            t => t.SeekPrefix("A"),
            t => t.SeekAtOrAfter("A"),
            t => t.SeekAtOrBefore("A"),
        ];

        foreach (Func<Table, object> seek in seeks)
        {
            table.Go(1).Should().Be(GoResult.Ok);
            Action act = () => seek(table);

            if (type == 'C')
            {
                act.Should().NotThrow($"{tableName} tag {tagName} holds character keys");
                continue;
            }

            CodeBaseException refusal = act.Should().Throw<CodeBaseException>(
                $"{tableName} tag {tagName} holds '{type}' keys, which text cannot be sought in").Which;
            refusal.Code.Should().Be(ErrorCode.Info);
            refusal.Message.Should().Contain(tagName);
            table.RecordNumber.Should().Be(1, "a refused seek moves nothing");
        }
    }

    [Fact]
    public void ARefusedStringSeek_LeavesTheSearchThatWasRunningAlive()
    {
        // The refusal comes before the tag's search buffer is written, so the typed seek it
        // interrupted can still be continued.
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXBASE.DBF"));
        table.SelectTag(table.Tags["T_NUM"]);

        table.Seek(4242.424).Should().Be(GoResult.Ok);
        int found = table.RecordNumber;

        table.Invoking(t => t.Seek("4242.424")).Should().Throw<CodeBaseException>();

        table.RecordNumber.Should().Be(found);
        table.Invoking(t => t.SeekNext()).Should().NotThrow("the numeric search is still running");
    }

    [Theory]
    [InlineData("CDXBASE", "T_NUM", "a double")]
    [InlineData("CDXBASE", "T_INT", "an int")]
    [InlineData("CDXBASE", "T_DATE", "a DateOnly")]
    [InlineData("CDXEXPR", "T_EMPTY", "No seek by value")]
    public void TheRefusal_NamesTheSeekThatWouldHaveWorked(string tableName, string tagName, string advice)
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));
        table.SelectTag(table.Tags[tagName]);

        table.Invoking(t => t.Seek("1")).Should().Throw<CodeBaseException>().WithMessage($"*{advice}*");
    }

    /// <summary>
    /// The type letter the reference gives a tag's key expression.
    /// </summary>
    private static char ReferenceType(string tableName, string tagName)
    {
        DumpIndexTag tag = CorpusIndexDump.Load(tableName + ".cdx").RealTags.Single(t => t.Name.TrimEnd() == tagName);
        string expression = Encoding.ASCII.GetString(tag.ExpressionBytes);

        DumpField? field = CorpusDump.Load(tableName).Fields
            .SingleOrDefault(f => f.Name.Equals(expression, StringComparison.OrdinalIgnoreCase));
        // A memo that is the whole expression is compiled for its tag as a string at the key size,
        // which the corpus's tag-context section records as type 'C' (e4parse.c:433-457). It is the
        // one field whose tag type is not its own.
        if (field is not null)
            return field.Type == 'M' ? 'C' : field.Type;

        DumpExpression? compiled = Expressions.ConstantExpressions
            .Concat(Expressions.FieldExpressions)
            .Concat(Expressions.ClockDependentExpressions)
            .FirstOrDefault(e => WithoutSpaces(e.Expression) == WithoutSpaces(expression));
        if (compiled is not null)
            return compiled.Type;

        // Only EMPTY(K_TEXT) reaches here. Every row of the function table under one name returns
        // the same type, which is asserted rather than assumed.
        string function = expression[..expression.IndexOf('(', StringComparison.Ordinal)];
        List<int> returns = [.. Functions.Rows.Where(r => r.Name == function).Select(r => r.ReturnType).Distinct()];
        returns.Should().ContainSingle($"the reference records one return type for {function}");

        return (char)returns[0];
    }

    private static string WithoutSpaces(string text) => text.Replace(" ", string.Empty, StringComparison.Ordinal);
}
