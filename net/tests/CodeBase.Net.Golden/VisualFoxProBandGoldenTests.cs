using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// The thirteen functions Visual FoxPro adds, over null operands, against the keys Visual FoxPro wrote itself.
///
/// [c]VFPBAND[/c] is written by Visual FoxPro 9 (ADR-44): a nullable numeric and a nullable
/// character field, six records with a null in each and in both, and one tag per function over them.
/// Visual FoxPro answers null for a null operand in every argument of every one of them, and keys that
/// null as a null; the port used to drop the marking, so [c]ABS(N_) = 0[/c] held on a null record
/// that Visual FoxPro leaves out. Every stored key rebuilt from its record is the gate.
///
/// Governing decision: ADR-82. The witness: VFPBAND.vfp.dump.txt, sections null-band and table.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class VisualFoxProBandGoldenTests
{
    private const string Oracle = "vfp/VFPBAND.DBF";

    private static readonly string[] TagNames =
        ["T_ABS", "T_INT", "T_ROUND", "T_MOD", "T_MIN", "T_MAX", "T_SQRT", "T_SIGN",
         "T_LEN", "T_AT", "T_ASC", "T_LOWER", "T_REPL"];

    [Fact]
    public void EveryKeyVisualFoxProStoredOverANullableOperandIsRebuiltFromItsRecord()
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(Oracle));
        List<string> failures = [];
        int compared = 0;

        foreach (string name in TagNames)
        {
            CdxTag tag = table.Tags[name].Inner;
            SeekConverter converter = SeekConverter.For(
                tag.Header, [.. table.Fields], table.CodePageNumber!.Value, table.ExpressionOptions, "VFPBAND", table.LongFieldNames);
            ExpressionEvaluator evaluator = new(converter.Compiled);
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
                        $"{name} record {stored.Record}: {Convert.ToHexString(built)}, stored {Convert.ToHexString(stored.Key)}");
                }
            }
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty($"{failures.Count} keys disagree");
        compared.Should().Be(TagNames.Length * table.RecordCount, "every record is in every tag once");
    }

    [Fact]
    public void TheOracleStillHoldsWhatTheRebuildRestsOn()
    {
        // A table with no null in it, or a tag the reader did not find, would let the test above
        // pass by comparing nothing that matters.
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(Oracle));
        FieldDefinition number = table.Fields["N_"];
        FieldDefinition text = table.Fields["C_"];

        table.RecordCount.Should().Be(6);
        table.Tags.Select(tag => tag.Name).Should().BeEquivalentTo(TagNames);

        List<(bool Number, bool Text)> nulls = [];
        for (int record = 1; record <= table.RecordCount; record++)
        {
            table.Go(record);
            nulls.Add((table.IsNull(number), table.IsNull(text)));
        }

        nulls.Should().Contain((true, false), "a null number beside a present text");
        nulls.Should().Contain((false, true), "a null text beside a present number");
        nulls.Should().Contain((true, true), "both null");
        nulls.Should().Contain((false, false), "neither");
    }
}
