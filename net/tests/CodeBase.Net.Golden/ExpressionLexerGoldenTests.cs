using AwesomeAssertions;
using CodeBase.Net.Expressions;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// The scanner against the reference implementation's own answers.
///
/// Layer four. What this catches that the unit table cannot: the unit table was written by reading
/// the reference's scanner and saying what it would do, so it passes on a self-consistent misreading
/// of it. These cases were not written at all. They are the expressions the reference actually
/// parsed, and the strings it actually refused, recorded by the generator from the library itself.
///
/// Two halves, and the second is the one with teeth. Everything the reference compiled has to scan,
/// which is a floor. And of the strings it refused, the ones the scanner is supposed to own have to
/// be refused here with the same code, while the rest have to scan cleanly, because refusing them
/// here would mean this port refuses at the wrong pass and reports the wrong reason.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class ExpressionLexerGoldenTests
{
    private static readonly CorpusExpressionDump Oracle = CorpusExpressionDump.Load();

    /// <summary>
    /// The refusals the scanner owns, and nothing else.
    /// </summary>
    /// <value>
    /// Declared rather than discovered. A test that accepted whatever the scanner happened to refuse
    /// would pass whether the refusal came from the right pass or not, and refusing too early is the
    /// failure that matters here: it reports the wrong reason and it can refuse text the reference
    /// accepts.
    /// </value>
    private static readonly string[] ScannerRefusals =
    [
        "-K_TEXT",
        "{01/01/2000}",
        "'unterminated",
        "NOSUCHFIELD",
        "K_TEXT.K_CITY",
        "K_TEXT +",
        "UPPER(",
    ];

    [Fact]
    public void EveryExpressionTheReferenceCompiledScans()
    {
        List<string> failures = [];

        foreach (DumpExpression compiled in Oracle.AcceptedExpressions)
        {
            try
            {
                ExpressionLexer.Tokenize(compiled.Expression).Should().NotBeEmpty();
            }
            catch (CodeBaseException error)
            {
                failures.Add($"{compiled.Expression}: {error.Code}");
            }
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
    }

    [Fact]
    public void TheSectionsAreAsLargeAsTheDumpSaysTheyAre()
    {
        // A reader that silently found nothing would make every test above pass, so the counts are
        // pinned against what the step's record says the generator produced.
        Oracle.ConstantExpressions.Should().HaveCount(131);
        Oracle.FieldExpressions.Should().HaveCount(77);
        Oracle.ClockDependentExpressions.Should().HaveCount(4);
        Oracle.Rejections.Should().HaveCount(18);
        Oracle.VisualFoxProOnly.Should().HaveCount(13);
    }

    [Fact]
    public void TheRefusalsTheScannerOwnsCarryTheCodeTheReferenceGave()
    {
        List<string> failures = [];

        foreach (DumpRejection rejection in Oracle.Rejections.Where(r => ScannerRefusals.Contains(r.Expression)))
        {
            try
            {
                ExpressionLexer.Tokenize(rejection.Expression);
                failures.Add($"{rejection.Expression}: scanned, expected {rejection.Code}");
            }
            catch (CodeBaseException error)
            {
                if ((int)error.Code != rejection.Code)
                    failures.Add($"{rejection.Expression}: {(int)error.Code}, expected {rejection.Code}");
            }
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
    }

    [Fact]
    public void EveryRefusalTheScannerDoesNotOwnScansCleanly()
    {
        // These are refused for reasons no scanner can see: a name the table has no row for, a
        // parameter that had to be constant, a type no overload accepts, a field that does not
        // exist. Each has a pass that owns it, and refusing here would take the answer away from it.
        List<string> failures = [];

        foreach (DumpRejection rejection in Oracle.Rejections.Where(r => !ScannerRefusals.Contains(r.Expression)))
        {
            try
            {
                ExpressionLexer.Tokenize(rejection.Expression);
            }
            catch (CodeBaseException error)
            {
                failures.Add($"{rejection.Expression}: refused with {(int)error.Code}, expected to scan");
            }
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
    }

    [Fact]
    public void EveryRefusalTheScannerOwnsIsOneTheDumpRecords()
    {
        // Keeps the declared list honest: a case renamed or dropped from the generator would leave a
        // name here matching nothing, and the two lists would quietly stop meaning the same thing.
        Oracle.Rejections.Select(rejection => rejection.Expression)
            .Should().Contain(ScannerRefusals);
    }

    [Fact]
    public void TheFunctionsVisualFoxProHasAndTheReferenceDoesNotScanAsOrdinaryCalls()
    {
        // The reference refuses all thirteen, with the code for a name its table has no row for.
        // This port accepts them, and the scanner is where that becomes possible: it has to read
        // them as calls without knowing whether the table has a row, which it does, because whether
        // a name is a function is settled by the parenthesis after it and by nothing else.
        foreach (DumpRejection candidate in Oracle.VisualFoxProOnly)
        {
            IReadOnlyList<ExpressionToken> tokens = ExpressionLexer.Tokenize(candidate.Expression);

            tokens[0].Kind.Should().Be(ExpressionTokenKind.FunctionName);
            tokens[1].Kind.Should().Be(ExpressionTokenKind.LeftParenthesis);
        }
    }
}
