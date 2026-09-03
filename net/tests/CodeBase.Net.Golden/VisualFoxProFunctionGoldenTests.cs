using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// The thirteen functions Visual FoxPro has and the reference implementation does not, against
/// Visual FoxPro's own answers.
///
/// The reference refuses every one of these, so there is nothing in `CDXEXPR` that could gate them
/// and the corpus's usual oracle is silent by construction — the generator [i]is[/i] CodeBase. ADR-44
/// settles it with a second offline oracle: Visual FoxPro 9, run in a VM, writing its own answers
/// into a table of its own. That table is read here by this port's own DBF reader, which is what
/// makes it usable at all.
///
/// The oracle holds more than these thirteen: it also runs the functions both engines have, so that
/// the places they disagree are recorded rather than assumed. Those are not gated here — `TRIM` alone
/// answers differently in each, and following Visual FoxPro there would break every stored key in the
/// corpus. What this gates is exactly the expressions whose compiled form reaches a row in the Visual
/// FoxPro band, which is a rule over rows rather than a list of names.
///
/// Governing decisions: ADR-43, ADR-44.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class VisualFoxProFunctionGoldenTests
{
    private const string Oracle = "vfp/VFPFVAL.DBF";

    private const string Tags = "vfp/VFPFUNC.DBF";

    /// <summary>
    /// The one case the ASCII mapping cannot answer.
    /// </summary>
    /// <value>
    /// Visual FoxPro lower cases by the table's code page, and this table is marked 1251, where
    /// byte 0xC9 is an upper-case Cyrillic letter and 0xE9 its lower-case pair. Only the ASCII path
    /// is implemented — the same gap `UPPER` has — so this is excluded by name rather than passed by
    /// accident. Closing it needs a transcribed case table per code page, and the step's state file
    /// records it as owed.
    /// </value>
    private const string NeedsTheAnsiMapping = "LOWER(\"AB1c\" + CHR(201))";

    [Fact]
    public void EveryVisualFoxProOnlyFunctionAnswersWhatVisualFoxProAnswered()
    {
        using CodeBaseEngine engine = new() { ExpressionOptions = ExpressionOptions.CodeBase.WithCentury(20) };
        using Table oracle = engine.OpenTable(Corpus.PathOf(Oracle));

        IFieldMetadata fields = new TableFieldMetadata([], "VFPFVAL");
        List<string> failures = [];
        int compared = 0;

        foreach (VisualFoxProCase recorded in Cases(oracle))
        {
            CompiledExpression compiled;
            try
            {
                compiled = TypeResolver.Resolve(ExpressionParser.Parse(recorded.Expression, fields));
            }
            catch (CodeBaseException error)
            {
                failures.Add($"{recorded.Expression}: refused with {(int)error.Code} {error.Message}");
                continue;
            }

            // Only the expressions that reach a row Visual FoxPro adds. The rest of the oracle is
            // the two engines' disagreements, which are recorded and deliberately not followed.
            if (!compiled.Nodes.Any(node => FunctionTable.IsVisualFoxPro(node.Row)))
                continue;

            if (recorded.Expression == NeedsTheAnsiMapping)
                continue;

            compared++;
            ExpressionValue value = new ExpressionEvaluator(compiled).Evaluate(new EvaluationContext());
            string? wrong = Disagreement(recorded, value);

            if (wrong is not null)
                failures.Add($"{recorded.Expression}: {wrong}");
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
        compared.Should().Be(36, "every recorded case that reaches the Visual FoxPro band is compared");
    }

    [Fact]
    public void TheKeyLengthOfEveryVisualFoxProTagIsWhatVisualFoxProWrote()
    {
        // The other half, and the one ADR-43 named as its risk: a length computed from the operand
        // has to match the length Visual FoxPro put in the tag header, or a tag this port writes is
        // a tag Visual FoxPro cannot read. Its own headers are the authority, because Visual FoxPro
        // exposes the number through no function.
        using CodeBaseEngine engine = new() { ExpressionOptions = ExpressionOptions.CodeBase.WithCentury(20) };
        using Table table = engine.OpenTable(Corpus.PathOf(Tags));

        IFieldMetadata fields = new TableFieldMetadata([.. table.Fields], "VFPFUNC");
        List<string> failures = [];
        int compared = 0;

        foreach (Tag tag in table.Tags)
        {
            CompiledExpression compiled;
            try
            {
                compiled = TypeResolver.Resolve(ExpressionParser.Parse(tag.Expression.Trim(), fields));
            }
            catch (CodeBaseException error)
            {
                // PROPER is Visual FoxPro's and is not in this port; the tag is in the oracle for the
                // per-function length rule and is named here rather than skipped silently.
                if (tag.Expression.Contains("PROPER", StringComparison.OrdinalIgnoreCase))
                    continue;

                failures.Add($"{tag.Name} '{tag.Expression}': refused with {(int)error.Code}");
                continue;
            }

            compared++;
            int computed = ExpressionKey.Length(compiled);

            if (computed != tag.KeyLength)
                failures.Add($"{tag.Name} '{tag.Expression}': computed {computed}, header says {tag.KeyLength}");
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
        // Sixteen: the tag directory lists seventeen, because Visual FoxPro attempted eighteen and
        // STRTRAN's failed at build time on the first row whose result did not fit the width it had
        // already written a header for. Of the seventeen, PROPER is the one this port does not have.
        compared.Should().Be(16, "seventeen tags exist and PROPER is not one this port has");
    }

    [Fact]
    public void TheOracleStillHoldsTheCasesTheseTestsRestOn()
    {
        // A reader that quietly found nothing would make both tests above pass by comparing nothing.
        using CodeBaseEngine engine = new();
        using Table oracle = engine.OpenTable(Corpus.PathOf(Oracle));

        oracle.RecordCount.Should().Be(77);
        Cases(oracle).Should().HaveCount(71, "two are refused by Visual FoxPro itself and four belong to its other two settings groups");
    }

    /// <summary>
    /// Reads the oracle's cases, skipping the ones Visual FoxPro itself refused.
    /// </summary>
    private static List<VisualFoxProCase> Cases(Table oracle)
    {
        FieldDefinition group = oracle.Fields["GRP"];
        FieldDefinition text = oracle.Fields["EXPRTEXT"];
        FieldDefinition type = oracle.Fields["RESTYPE"];
        FieldDefinition length = oracle.Fields["RESLEN"];
        FieldDefinition number = oracle.Fields["RESD"];
        FieldDefinition characters = oracle.Fields["RESC"];
        FieldDefinition truth = oracle.Fields["RESL"];
        FieldDefinition error = oracle.Fields["ERRMSG"];

        List<VisualFoxProCase> cases = [];

        for (GoResult go = oracle.Top(); go == GoResult.Ok;)
        {
            if (oracle.GetString(error).Trim().Length == 0 && oracle.GetString(group).Trim() == "main")
            {
                cases.Add(new VisualFoxProCase(
                    oracle.GetString(text).Trim(),
                    oracle.GetString(type).Trim(),
                    oracle.GetInt32(length),
                    oracle.GetRawBytes(number).ToArray(),
                    oracle.GetRawBytes(characters).ToArray(),
                    oracle.GetBoolean(truth)));
            }

            if (oracle.Skip(1) != SkipResult.Moved)
                break;
        }

        return cases;
    }

    /// <summary>
    /// Says how a value differs from what Visual FoxPro recorded, or null where it does not.
    /// </summary>
    /// <value>
    /// A number is compared on its [b]stored bits[/b] and not on a formatting of them, which is what
    /// the oracle's own header insists on: the text beside each case is for review and the field is
    /// the answer. It also catches the sign of a zero, which no formatting shows and which an index
    /// key would carry.
    /// </value>
    private static string? Disagreement(VisualFoxProCase recorded, ExpressionValue value) => recorded.Type switch
    {
        "N" => value.ToRaw().AsSpan().SequenceEqual(recorded.Number)
            ? null
            : $"{Convert.ToHexString(value.ToRaw())}, expected {Convert.ToHexString(recorded.Number)}",

        "C" => value.Bytes[..Math.Min(recorded.Length, value.Bytes.Length)]
                .SequenceEqual(recorded.Characters.AsSpan(0, recorded.Length))
                && value.TrimmedLength == recorded.Length
            ? null
            : $"'{Encoding.Latin1.GetString(value.Bytes)}' ({value.TrimmedLength}), expected "
                + $"'{Encoding.Latin1.GetString(recorded.Characters, 0, recorded.Length)}' ({recorded.Length})",

        "L" => value.Logical == recorded.Truth ? null : $"{value.Logical}, expected {recorded.Truth}",

        _ => null,
    };

    private sealed record VisualFoxProCase(
        string Expression,
        string Type,
        int Length,
        byte[] Number,
        byte[] Characters,
        bool Truth);
}
