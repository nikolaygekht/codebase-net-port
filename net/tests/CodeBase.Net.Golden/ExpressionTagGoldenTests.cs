using AwesomeAssertions;
using CodeBase.Net.Expressions;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// An expression tag used as a tag: selected, walked, and positioned into by record number.
///
/// The last thing the step owed. Keys were already rebuilt byte for byte against the file, but a key
/// that is right and a tag that works are different claims: the second one runs through tag
/// selection, the leaf decoder's pad byte, the descent, and the re-derivation of a record's key that
/// a move by record number needs. This is the first test in the project that follows a tag whose key
/// is not a field.
///
/// The walk order is compared against the key listing the generator took from the reference's own
/// navigation, so the assertion is that this library visits the records the C library stored, in the
/// order it stored them.
///
/// Governing specification: EXPRESSIONS.md section 9, CDX-FORMAT.md section 2.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class ExpressionTagGoldenTests
{
    private static readonly CorpusIndexDump Index = CorpusIndexDump.Load("CDXEXPR.cdx");

    private static readonly CorpusExpressionDump Oracle = CorpusExpressionDump.Load();

    [Fact]
    public void EveryExpressionTagCanBeSelected()
    {
        // ADR-28's refusal is gone. Twenty-two tags, and every one of them now types.
        using CodeBaseEngine engine = Engine();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXEXPR.DBF"));

        table.Tags.Should().HaveCount(22);

        foreach (Tag tag in table.Tags)
        {
            Action act = () => table.SelectTag(tag);

            act.Should().NotThrow($"tag {tag.Name} keyed on '{tag.Expression}'");
        }
    }

    [Fact]
    public void EveryExpressionTagWalksTheRecordsTheReferenceStoredInItsOrder()
    {
        using CodeBaseEngine engine = Engine();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXEXPR.DBF"));

        List<string> failures = [];

        foreach (DumpIndexTag expected in Index.RealTags)
        {
            Tag tag = table.Tags[expected.Name.TrimEnd()];
            table.SelectTag(tag);

            List<uint> walked = [];
            for (GoResult go = table.Top(); go == GoResult.Ok;)
            {
                walked.Add((uint)table.RecordNumber);
                if (table.Skip(1) != SkipResult.Moved)
                    break;
            }

            List<uint> stored = [.. expected.Keys.Select(key => key.Record)];
            if (!walked.SequenceEqual(stored))
            {
                failures.Add($"{expected.Name.TrimEnd()} '{tag.Expression}': walked "
                    + $"[{string.Join(",", walked)}], stored [{string.Join(",", stored)}]");
            }
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
    }

    [Fact]
    public void MovingByRecordNumberRebuildsThatRecordsKeyAndDescendsToIt()
    {
        // The path that actually runs the expression engine. A walk reads entries in order and needs
        // no key at all; moving to a record by number does, because the tree is ordered by key and
        // the reference turns that lookup into a descent by re-deriving the record's key
        // (d4seekSynchToCurrentPos, D4SEEK.C:1141). If the rebuilt key were wrong the descent would
        // land somewhere else and the next step would report the wrong record.
        using CodeBaseEngine engine = Engine();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXEXPR.DBF"));

        List<string> failures = [];
        int steps = 0;

        foreach (DumpIndexTag expected in Index.RealTags)
        {
            Tag tag = table.Tags[expected.Name.TrimEnd()];
            table.SelectTag(tag);

            for (int at = 0; at < expected.Keys.Count - 1; at++)
            {
                uint from = expected.Keys[at].Record;
                uint next = expected.Keys[at + 1].Record;

                table.Go((int)from).Should().Be(GoResult.Ok);
                table.Skip(1).Should().Be(SkipResult.Moved);

                steps++;
                if (table.RecordNumber != (int)next)
                {
                    failures.Add($"{expected.Name.TrimEnd()} '{tag.Expression}': from record {from} "
                        + $"the next in tag order is {table.RecordNumber}, stored says {next}");
                }
            }
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
        steps.Should().BeGreaterThan(300);
    }

    private static CodeBaseEngine Engine() => new()
    {
        // The picture and century the dump was taken under. Without them a tag over a date read from
        // text is refused rather than keyed differently, which is ADR-42's whole point.
        ExpressionOptions = ExpressionOptions.CodeBase.WithCentury(Oracle.Century),
    };
}
