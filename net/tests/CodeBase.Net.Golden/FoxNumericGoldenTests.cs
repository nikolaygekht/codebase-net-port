using AwesomeAssertions;
using CodeBase.Net.Dbf;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// Whether the numeric parse produces the same double the C library produced, for every value in the corpus.
///
/// The step's chief risk, and the reason this runs before the datetime, currency and text work
/// rather than after it. The dump's values came out of the C library's own hand-rolled conversion,
/// whose body is not in the source drop; the standard library's parser is correctly rounded. If the
/// two disagree anywhere, every number this library reports is subtly wrong and so is every range
/// comparison the optimizer will later build on one.
///
/// Compared on the bit pattern rather than with equality, so that negative zero and a wrong last bit
/// cannot slip through.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class FoxNumericGoldenTests
{
    /// <summary>
    /// The types whose stored form is ASCII text and whose value therefore comes from this parser.
    /// </summary>
    private static readonly char[] TextualNumericTypes = ['N', 'F'];

    [Theory]
    [MemberData(nameof(AllTables))]
    public void ToDouble_MatchesTheCLibraryBitForBit(string tableName)
    {
        CorpusDump dump = CorpusDump.Load(tableName);
        int compared = 0;

        foreach (DumpRecord record in dump.Records)
        {
            foreach (DumpValue value in record.Values)
            {
                DumpField field = dump.Fields.Single(f => f.Name == value.Name);
                if (!TextualNumericTypes.Contains(field.Type) || value.Number is null)
                    continue;

                double actual = FoxNumeric.ToDouble(value.Bytes);

                BitConverter.DoubleToInt64Bits(actual)
                    .Should().Be(
                        BitConverter.DoubleToInt64Bits(value.Number.Value),
                        "record {0} field {1} holds '{2}' and the C library read it as {3:R}",
                        record.Number,
                        value.Name,
                        System.Text.Encoding.ASCII.GetString(value.Bytes),
                        value.Number.Value);

                compared++;
            }
        }

        // A table with no numeric field compares nothing, which is fine, but the suite as a whole
        // must not. The count across all tables is asserted separately below.
        compared.Should().Be(ExpectedCount(dump));
    }

    [Fact]
    public void TheComparisonCoversEveryNumericValueInTheCorpus()
    {
        // Says the suite above is not silently comparing nothing. The corpus holds 224 values in
        // fields whose stored form is text: two per record in four tables and one in two more.
        int total = 0;

        foreach (string tableName in TestUtils.Corpus.TableNames)
        {
            CorpusDump dump = CorpusDump.Load(tableName);
            total += ExpectedCount(dump);
        }

        total.Should().Be(224);
    }

    [Fact]
    public void ToDouble_KeepsTheSignOfNegativeZero()
    {
        // Stored as text, so the sign is the only thing carrying it. Any normalization through a
        // Math.Abs or an addition destroys it, and equality cannot see the loss. See Decision 8.
        DumpValue value = CorpusDump.Load("VFPTYPE").Records[1]["F_B"];

        double.IsNegative(value.Number!.Value).Should().BeTrue("the dump says the C library read -0");
    }

    /// <summary>
    /// How many values the table should contribute, counted from its field list rather than from
    /// what the loop happened to find.
    /// </summary>
    private static int ExpectedCount(CorpusDump dump) =>
        dump.Fields.Count(f => TextualNumericTypes.Contains(f.Type)) * dump.RecordCount;

    public static TheoryData<string> AllTables()
    {
        TheoryData<string> data = [];
        foreach (string table in TestUtils.Corpus.TableNames)
            data.Add(table);
        return data;
    }
}
