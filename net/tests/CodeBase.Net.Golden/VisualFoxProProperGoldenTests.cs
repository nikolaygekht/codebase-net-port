using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// Visual FoxPro's PROPER against the keys Visual FoxPro wrote for it, over every byte that might begin a word.
///
/// [c]VFPPROP[/c] is written by Visual FoxPro 9 (ADR-44) on its host's code page, 1251: a nullable
/// [c]C(30)[/c] holding 36 named probes and then a letter, each of the 256 bytes and a letter, under
/// one tag, [c]PROPER(K)[/c]. Which of those bytes begins a word is the whole of the function's rule,
/// and the keys are its answer, so every stored key rebuilt from its record is the gate.
///
/// Governing decision: ADR-83. The witness: VFPPROP.vfp.dump.txt.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class VisualFoxProProperGoldenTests
{
    private const string Oracle = "vfp/VFPPROP.DBF";

    [Fact]
    public void EveryKeyVisualFoxProStoredForProperCaseIsRebuiltFromItsRecord()
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(Oracle));

        CdxTag tag = table.Tags["T_PROPER"].Inner;
        SeekConverter converter = SeekConverter.For(
            tag.Header, [.. table.Fields], table.CodePageNumber!.Value, table.ExpressionOptions, "VFPPROP", table.LongFieldNames);
        ExpressionEvaluator evaluator = new(converter.Compiled);
        FieldDefinition probe = table.Fields["K"];
        List<string> failures = [];
        int compared = 0;
        TagCursor cursor = tag.OpenCursor();

        for (bool on = cursor.Top(); on; on = cursor.Next())
        {
            IndexEntry stored = cursor.Current;
            table.Go((int)stored.Record);

            byte[] built = new byte[converter.KeyLength];
            RecordKey.WriteValue(converter, evaluator.Evaluate(table.EvaluationContext()), built);

            compared++;
            if (!built.AsSpan().SequenceEqual(stored.Key))
            {
                failures.Add(
                    $"record {stored.Record} {Convert.ToHexString(table.GetRawBytes(probe))}: " +
                    $"{Convert.ToHexString(built)}, stored {Convert.ToHexString(stored.Key)}");
            }
        }

        string.Join(Environment.NewLine, failures.Take(10)).Should().BeEmpty($"{failures.Count} keys disagree");
        compared.Should().Be(table.RecordCount, "every record is in the tag once");
    }

    [Fact]
    public void TheOracleStillHoldsWhatTheRebuildRestsOn()
    {
        // Every byte between two letters, and a null: a sample that missed either would let the test
        // above pass on less than it claims.
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(Oracle));
        FieldDefinition probe = table.Fields["K"];

        table.RecordCount.Should().Be(292);
        table.CodePageNumber.Should().Be(1251);

        HashSet<byte> between = [];
        int nulls = 0;
        for (int record = 1; record <= table.RecordCount; record++)
        {
            table.Go(record);
            if (table.IsNull(probe))
            {
                nulls++;
                continue;
            }

            ReadOnlySpan<byte> raw = table.GetRawBytes(probe);
            if (record > 36 && raw[0] == (byte)'a' && raw[2] == (byte)'b')
                between.Add(raw[1]);
        }

        nulls.Should().Be(1);
        between.Should().HaveCount(256);
    }
}
