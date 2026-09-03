using AwesomeAssertions;
using CodeBase.Net.Expressions;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// The transcription gate: the ported function table against the reference implementation's own.
///
/// Layer four, and the first gate of the expression step, because everything after it is meaningless
/// until it holds. Overload resolution reports which row it chose, so a row that is wrong here makes
/// the resolution gate compare two wrong numbers; and a wrong row does not fail loudly at run time,
/// it selects the neighbouring row and returns a plausible value.
///
/// The two sides are genuinely independent readings. The port's rows were typed out of the C source
/// text, e4functi.c lines 32 to 301, including its conditional compilation. The dump was walked out
/// of the compiled library's array by the generator. Nothing was derived from the other.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class FunctionTableGoldenTests
{
    private static readonly CorpusFunctionsDump Reference = CorpusFunctionsDump.Load();

    [Fact]
    public void TheTableHasAsManyRowsAsTheReference()
    {
        // The count is what makes the row-for-row comparison below mean anything: two tables that
        // agree on every row of the shorter one still disagree if one is longer.
        Reference.Rows.Should().HaveCount(Reference.Count);
        FunctionTable.Rows.Should().HaveCount(Reference.Count);
    }

    [Fact]
    public void EveryRowMatchesTheReferenceRowInEveryField()
    {
        List<string> differences = [];

        for (int index = 0; index < Reference.Rows.Count; index++)
        {
            DumpFunctionRow expected = Reference.Rows[index];
            FunctionRow actual = FunctionTable.Row(index);

            Compare(differences, index, "name", expected.Name, actual.Name ?? string.Empty);
            Compare(differences, index, "code", expected.Code, actual.Code);
            Compare(differences, index, "nameLength", expected.NameLength, actual.NameLength);
            Compare(differences, index, "priority", expected.Priority, actual.Priority);
            Compare(differences, index, "returnType", expected.ReturnType, (int)actual.ReturnType);
            Compare(differences, index, "parameterCount", expected.ParameterCount, actual.ParameterCount);

            for (int parameter = 0; parameter < expected.ParameterTypes.Count; parameter++)
            {
                Compare(
                    differences,
                    index,
                    $"parameter{parameter + 1}",
                    expected.ParameterTypes[parameter],
                    (int)actual.ParameterType(parameter));
            }
        }

        // Joined rather than asserted as a collection, because a transcription slip is usually a run
        // of rows shifted by one and the assertion message has to show the whole run to name the
        // cause. A collection assertion reports only its first item.
        string.Join(Environment.NewLine, differences).Should().BeEmpty();
    }

    [Fact]
    public void TheIndexConstantsNameTheRowsTheReferenceSaysTheyDo()
    {
        // The bands are what the parser and the optimizer address the table by, so they are pinned
        // against the rows they are supposed to bracket rather than against their own arithmetic.
        FunctionTable.Row(FunctionTable.FirstLogical).Name.Should().Be(".TRUE.");
        FunctionTable.Row(FunctionTable.LastLogical).Name.Should().Be("!");
        FunctionTable.Row(FunctionTable.FirstOperator).Name.Should().Be(".OR.");
        FunctionTable.Row(FunctionTable.FirstFunction).Name.Should().Be("CHR");

        // The operator band deliberately runs two rows past the last real operator, and that is the
        // reference implementation's own arithmetic rather than an off-by-one here.
        FunctionTable.Row(FunctionTable.LastOperator).Name.Should().Be("DEL");
    }

    [Fact]
    public void TheLastRowIsTheTerminatorAndNothingBeforeItIs()
    {
        FunctionTable.Row(FunctionTable.Rows.Count - 1).IsTerminator.Should().BeTrue();
        FunctionTable.Rows.Take(FunctionTable.Rows.Count - 1)
            .Should().OnlyContain(row => !row.IsTerminator);
    }

    private static void Compare<TValue>(List<string> differences, int index, string field, TValue expected, TValue actual)
    {
        if (!Equals(expected, actual))
            differences.Add($"row {index} {field}: expected {expected}, got {actual}");
    }
}
