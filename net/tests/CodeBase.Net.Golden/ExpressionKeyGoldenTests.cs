using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// Every key of every expression tag in the corpus, rebuilt and compared against the bytes on disk.
///
/// This is the question the whole step exists to answer: given the expression a tag stores, does this
/// port produce the same key bytes the C library wrote? The oracle is the index dump's key listing,
/// which the generator took from the reference's own navigation rather than from a re-read of the
/// file, so the keys here are what the library believes it stored.
///
/// Two runs, and the difference between them is the point. One drives the conversion from the
/// [b]value the reference recorded[/b], with this port's evaluator out of the circuit, so a wrong
/// kernel cannot be cancelled by a wrong transform. The other runs the whole engine end to end. Both
/// have to agree with the disk, and because the value gate already passed on its own, agreement here
/// cannot be two errors meeting in the middle.
///
/// Governing specification: EXPRESSIONS.md sections 9.1 to 9.4.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class ExpressionKeyGoldenTests
{
    private static readonly CorpusIndexDump Index = CorpusIndexDump.Load("CDXEXPR.cdx");

    private static readonly CorpusExpressionDump Oracle = CorpusExpressionDump.Load();

    [Fact]
    public void EveryKeyOfEveryExpressionTagIsRebuiltByteForByte()
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXEXPR.DBF"));

        IFieldMetadata fields = new TableFieldMetadata([.. table.Fields], "CDXEXPR");
        List<string> failures = [];
        int compared = 0;

        foreach (DumpIndexTag tag in Index.RealTags)
        {
            string text = Encoding.Latin1.GetString(tag.ExpressionBytes);

            CompiledExpression compiled;
            try
            {
                compiled = TypeResolver.Resolve(ExpressionParser.Parse(text, fields));
            }
            catch (CodeBaseException error)
            {
                failures.Add($"{tag.Name} '{text}': refused with {(int)error.Code}");
                continue;
            }

            // The length is computed and then checked against what the header says, because a key of
            // the right bytes at the wrong width is a different key.
            int length = ExpressionKey.Length(compiled);
            if (length != tag.KeyLength)
            {
                failures.Add($"{tag.Name} '{text}': computed key length {length}, header says {tag.KeyLength}");
                continue;
            }

            ExpressionEvaluator evaluator = new(compiled);
            bool indicator = ExpressionKey.CanBeAbsent(compiled);

            foreach (DumpIndexKey stored in tag.Keys)
            {
                table.Go((int)stored.Record);

                byte[] key = new byte[length];
                ExpressionKey.Write(
                    evaluator.Evaluate(Context(table)),
                    compiled.HasTrim,
                    indicator,
                    key);

                compared++;
                if (!key.AsSpan().SequenceEqual(stored.Key))
                {
                    failures.Add($"{tag.Name} '{text}' at record {stored.Record}: "
                        + $"{Convert.ToHexString(key)}, expected {Convert.ToHexString(stored.Key)}");
                }
            }
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();

        // Every tag, every record, and the count is pinned so that a reader which quietly found no
        // keys could not pass this by comparing nothing.
        compared.Should().Be(Index.RealTags.Sum(tag => tag.Keys.Count));
        compared.Should().BeGreaterThan(300);
    }

    [Fact]
    public void TheHeaderAgreesAboutWhichTagsCarryAPresentOrAbsentByte()
    {
        // Bit 0x02 of the type code says the key has the leading byte, so the file states directly
        // what this port works out from the expression's fields.
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXEXPR.DBF"));

        IFieldMetadata fields = new TableFieldMetadata([.. table.Fields], "CDXEXPR");
        List<string> failures = [];

        foreach (DumpIndexTag tag in Index.RealTags)
        {
            string text = Encoding.Latin1.GetString(tag.ExpressionBytes);
            CompiledExpression compiled = TypeResolver.Resolve(ExpressionParser.Parse(text, fields));

            bool stated = (Convert.ToInt32(tag.Header["typeCode"], 16) & 0x02) != 0;
            if (ExpressionKey.CanBeAbsent(compiled) != stated)
                failures.Add($"{tag.Name} '{text}': header says {stated}");
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
    }

    [Fact]
    public void TheKeysAlsoRebuildFromTheValuesTheReferenceRecordedWithTheEvaluatorOutOfTheCircuit()
    {
        // The half that cannot be cancelled. For the tags whose expression the value dump also
        // records, the conversion is fed the reference's own recorded value rather than ours, so a
        // wrong kernel has nothing to hide behind.
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXEXPR.DBF"));

        IFieldMetadata fields = new TableFieldMetadata([.. table.Fields], "CDXEXPR");
        List<string> failures = [];
        int compared = 0;

        foreach (DumpIndexTag tag in Index.RealTags)
        {
            string text = Encoding.Latin1.GetString(tag.ExpressionBytes);
            CompiledExpression compiled = TypeResolver.Resolve(ExpressionParser.Parse(text, fields));

            if (Recorded(text) is not DumpExpression recorded)
                continue;

            // A recorded value is raw result bytes and carries no marking for absence, so a tag
            // whose key has the present-or-absent byte cannot be rebuilt from one: its three absent
            // records would come back present. Those are compared end to end above, where the
            // marking survives, and their keys there are all zero bytes as they should be.
            if (ExpressionKey.CanBeAbsent(compiled))
                continue;

            foreach (DumpIndexKey stored in tag.Keys)
            {
                if (!recorded.RecordValues.TryGetValue((int)stored.Record, out byte[]? value))
                    continue;

                byte[] key = new byte[ExpressionKey.Length(compiled)];
                ExpressionKey.Write(
                    Rehydrate(compiled.ResultType, value),
                    compiled.HasTrim,
                    ExpressionKey.CanBeAbsent(compiled),
                    key);

                compared++;
                if (!key.AsSpan().SequenceEqual(stored.Key))
                {
                    failures.Add($"{tag.Name} '{text}' at record {stored.Record} from the recorded value: "
                        + $"{Convert.ToHexString(key)}, expected {Convert.ToHexString(stored.Key)}");
                }
            }
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
        compared.Should().BeGreaterThan(100);
    }

    /// <summary>
    /// Finds the value dump's entry for a tag's expression, whose text may be spaced differently.
    /// </summary>
    private static DumpExpression? Recorded(string expression)
    {
        string wanted = Squeeze(expression);

        foreach (DumpExpression candidate in Oracle.FieldExpressions)
        {
            if (Squeeze(candidate.Expression) == wanted && candidate.RecordValues.Count > 0)
                return candidate;
        }

        return null;
    }

    private static string Squeeze(string text) => text.Replace(" ", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Rebuilds a value from the raw result bytes the reference recorded for it.
    /// </summary>
    /// <value>
    /// The inverse of what an evaluation writes out, so that the conversion can be handed the
    /// reference's answer instead of ours. A null is not recoverable from the bytes, which is why the
    /// nullable tag's records are compared only end to end.
    /// </value>
    private static ExpressionValue Rehydrate(ExpressionType type, byte[] raw) => type switch
    {
        ExpressionType.NumericAsText => ExpressionValue.NumericText(raw),
        ExpressionType.DateAsText => ExpressionValue.DateText(raw),
        ExpressionType.Integer => ExpressionValue.Whole(BitConverter.ToInt32(raw)),
        ExpressionType.Logical => ExpressionValue.Logic(BitConverter.ToInt32(raw) != 0),
        ExpressionType.Currency => ExpressionValue.Currency(BitConverter.ToInt64(raw)),
        ExpressionType.BinaryFloat => ExpressionValue.Float(BitConverter.ToSingle(raw)),
        ExpressionType.Timestamp or ExpressionType.TimestampWithMilliseconds =>
            ExpressionValue.Timestamp(BitConverter.ToInt32(raw), BitConverter.ToInt32(raw, 4)),
        ExpressionType.NumericAsDouble => ExpressionValue.Numeric(BitConverter.ToDouble(raw)),
        ExpressionType.DateAsDouble => ExpressionValue.Date(BitConverter.ToDouble(raw)),
        _ => ExpressionValue.Text(raw),
    };

    private static EvaluationContext Context(Table table) => new()
    {
        Record = table.CurrentRecord,
        NullFlags = table.NullFlags,
        RecordNumber = table.RecordNumber,
        Deleted = table.Deleted,
        RecordCount = table.RecordCount,
        Options = ExpressionOptions.CodeBase.WithCentury(Oracle.Century),
        Memo = field => table.GetMemoBytes(field),
    };
}
