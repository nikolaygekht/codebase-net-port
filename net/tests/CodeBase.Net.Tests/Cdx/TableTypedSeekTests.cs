using System.Buffers.Binary;
using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.Dbf;
using Xunit;
using KeyField = CodeBase.Net.Tests.Cdx.IndexedTableImage.KeyField;

namespace CodeBase.Net.Tests.Cdx;

/// <summary>
/// Seeking a tag of every kind through the table: by number, by date, by text through a conversion,
/// under a collation, behind a null byte, and the refusals that name the seek to use instead.
/// </summary>
/// <value>
/// Each table and its tag are built in memory, the tag's keys with the key transforms (which the
/// golden suite holds to stored keys), so what each test expects is the record a seek reaches, read
/// off the data it lays down. The rules are ADR-58 (text only on character keys), ADR-64 (a
/// conversion tag sought through its own transform) and D4SEEK.C:47-57 (a value behind the null byte).
/// </value>
[Trait("Layer", "Component")]
public sealed class TableTypedSeekTests
{
    // ------------------------------------------------------------------ by number

    [Fact]
    public void AnIntegerTagIsSoughtByAnInt_AndTheSearchContinuesBothWays()
    {
        (CodeBaseEngine engine, Table table) = Integers(-7, 3, 3, 10);
        using (engine)
        {
            table.Seek(3).Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(2);

            table.SeekNext().Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(3);
            table.SeekNext().Should().Be(GoResult.NoRecord, "record 4 holds 10");

            table.Seek(3);
            table.SeekNext();
            table.SeekPrevious().Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(2);

            table.Seek(4).Should().Be(GoResult.NoRecord);
        }
    }

    [Fact]
    public void ANumericTagIsSoughtByADouble_ExactlyOrToEitherSide()
    {
        (CodeBaseEngine engine, Table table) = Open(
            new KeyField("AMOUNT", 'N', 8, 2),
            [Text("   12.50"), Text("   -3.00"), Text("  100.00")],
            [(Double(-3), 2), (Double(12.5), 1), (Double(100), 3)]);
        using (engine)
        {
            table.Seek(12.5).Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(1);

            table.SeekAtOrAfter(50.0).Should().Be(SeekResult.After);
            table.RecordNumber.Should().Be(3);

            table.SeekAtOrBefore(50.0).Should().Be(SeekResult.Before);
            table.RecordNumber.Should().Be(1);

            table.SeekAtOrAfter(200.0).Should().Be(SeekResult.Eof);
        }
    }

    [Fact]
    public void ADateTagIsSoughtByADate_ExactlyOrToEitherSide()
    {
        (CodeBaseEngine engine, Table table) = Open(
            new KeyField("BORN", 'D', 8),
            [Text("19870615"), Text("20000115"), Text("19870615")],
            [(Date(1987, 6, 15), 1), (Date(1987, 6, 15), 3), (Date(2000, 1, 15), 2)]);
        using (engine)
        {
            table.Seek(new DateOnly(2000, 1, 15)).Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(2);

            table.SeekAtOrAfter(new DateOnly(1987, 6, 15)).Should().Be(SeekResult.Found);
            table.RecordNumber.Should().Be(1);

            table.SeekAtOrAfter(new DateOnly(1990, 1, 1)).Should().Be(SeekResult.After);
            table.RecordNumber.Should().Be(2);

            table.SeekAtOrBefore(new DateOnly(1990, 1, 1)).Should().Be(SeekResult.Before);
            table.RecordNumber.Should().Be(3, "the last of the keys before it");
        }
    }

    // ------------------------------------------------------------------ by text

    [Fact]
    public void ADescendingConversionTagIsSoughtByTheTextItConverts()
    {
        // The tag stores DESCEND(NAME), each byte's complement; a seek for the text converts it the
        // same way first (ADR-64), whole or as a prefix.
        string[] names = ["APPLE ", "BANANA", "CHERRY"];
        (CodeBaseEngine engine, Table table) = Open(
            new KeyField("NAME", 'C', 6),
            [.. names.Select(Text)],
            Sorted(names.Select((n, i) => (Complement(Text(n)), (uint)(i + 1)))),
            expression: "DESCEND(NAME)");
        using (engine)
        {
            table.Seek("BANANA").Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(2);

            table.SeekPrefix("AP").Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(1);

            table.Seek("APRICO").Should().Be(GoResult.NoRecord);

            table.Seek("CHERRY").Should().Be(GoResult.Ok);
            table.SeekNext().Should().Be(GoResult.NoRecord, "a text seek leaves a search to continue");
        }
    }

    [Fact]
    public void ACollatedTagIsSoughtByTheTextItWeighs()
    {
        // GENERAL weighs both cases of a letter alike (KEY-COLLATION.md section 3.3), so a whole seek
        // finds either spelling; a prefix seek is weighed into the heads alone.
        string[] names = ["apple ", "BANANA", "Cherry"];
        (CodeBaseEngine engine, Table table) = Open(
            new KeyField("NAME", 'C', 6),
            [.. names.Select(Text)],
            Sorted(names.Select((n, i) => (Collated(Text(n)), (uint)(i + 1)))),
            collation: "GENERAL");
        using (engine)
        {
            table.Seek("APPLE").Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(1);

            table.SeekPrefix("ban").Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(2);

            table.Seek("APPLF").Should().Be(GoResult.NoRecord);
        }
    }

    [Fact]
    public void AWholeCollatedSeekWeighsTheAccentsAndAPrefixSeekDoesNot()
    {
        // An accented letter shares its head with the plain one and differs in its tail (KEY-COLLATION.md
        // section 3.3): a whole seek carries the tails and tells the two apart, a prefix seek is heads
        // only and finds either. 0x90 is E-acute in cp437, the unmarked table's code page.
        (CodeBaseEngine engine, Table table) = Open(
            new KeyField("NAME", 'C', 6),
            [Text("CAF\u0090  ")],
            [(Collated(Text("CAF\u0090  ")), 1)],
            collation: "GENERAL");
        using (engine)
        {
            table.Seek("CAFE").Should().Be(GoResult.NoRecord);
            table.Seek("CAF\u0090").Should().Be(GoResult.Ok);
            table.SeekPrefix("CAFE").Should().Be(GoResult.Ok);
        }
    }

    [Fact]
    public void AWholeTextSeekIsNotAPrefixSeek()
    {
        // Seek and the positioning seeks take the value as a whole key, blank-padded; only SeekPrefix
        // takes it as the start of one.
        (CodeBaseEngine engine, Table table) = IndexedTableImage.Open(
            ["APPLE", "BANANA"], [("APPLE", 1), ("BANANA", 2)], encoding: Encoding.Latin1);
        using (engine)
        {
            table.SelectTag(table.Tags[0]);

            table.Seek("AP").Should().Be(GoResult.NoRecord);
            table.SeekAtOrAfter("AP").Should().Be(SeekResult.After);
            table.RecordNumber.Should().Be(1);
            table.SeekAtOrBefore("AP").Should().Be(SeekResult.Bof, "AP and blanks sort before APPLE");
            table.SeekPrefix("AP").Should().Be(GoResult.Ok);
        }
    }

    [Fact]
    public void ANullableTagIsSoughtBehindItsNullByte()
    {
        // A value's key is 0x80 and its bytes, an absent one all zeros (D4SEEK.C:47-57): a seek for
        // blanks finds the blank value and not the record with none.
        (CodeBaseEngine engine, Table table) = Open(
            new KeyField("NAME", 'C', 6, Nullable: true),
            [Text("APPLE "), null, Text("      ")],
            [(new byte[7], 2), ([0x80, .. Text("      ")], 3), ([0x80, .. Text("APPLE ")], 1)]);
        using (engine)
        {
            table.Seek("APPLE").Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(1);

            table.Seek("      ").Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(3);

            table.SeekPrefix("APP").Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(1);
        }
    }

    [Fact]
    public void ANullableIntegerTagIsSoughtBehindItsNullByteToo()
    {
        (CodeBaseEngine engine, Table table) = Open(
            new KeyField("COUNT", 'I', 4, Nullable: true),
            [Int(5), null],
            [(new byte[5], 2), ([0x80, .. Int32Key(5)], 1)]);
        using (engine)
        {
            table.Seek(5).Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(1);
        }
    }

    // ------------------------------------------------------------------ refused

    [Theory]
    [InlineData('I', 4, "Seek it with an int.")]
    [InlineData('N', 8, "Seek it with a double.")]
    [InlineData('D', 8, "Seek it with a DateOnly.")]
    [InlineData('Y', 8, "Seek it with a decimal.")]
    [InlineData('T', 8, "Seek it with a DateTime.")]
    public void TextIsRefusedOnATagThatIsNotCharacter_NamingTheSeekToUse(char type, int length, string advice)
    {
        (CodeBaseEngine engine, Table table) = Open(new KeyField("K", type, length, type == 'Y' ? 4 : 0), [], []);
        using (engine)
        {
            Action act = () => table.Seek("3");

            act.Should().Throw<CodeBaseException>()
                .Where(e => e.Code == ErrorCode.Info && e.Message.Contains(advice));
        }
    }

    [Fact]
    public void AValueOfAnotherTypeIsRefusedByWhatItIs()
    {
        (CodeBaseEngine engine, Table table) = Open(new KeyField("BORN", 'D', 8), [], []);
        using (engine)
        {
            Action act = () => table.Seek(5);

            act.Should().Throw<CodeBaseException>()
                .Where(e => e.Code == ErrorCode.Info && e.Message.Contains("an integer"));
        }
    }

    // ------------------------------------------------------------------ by currency and datetime

    [Fact]
    public void ACurrencyTagIsSoughtByADecimal_ExactlyOrToEitherSide()
    {
        (CodeBaseEngine engine, Table table) = Currencies(12.5m, -3m, 100m);
        using (engine)
        {
            table.Seek(12.5m).Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(1);

            table.SeekAtOrAfter(50m).Should().Be(SeekResult.After);
            table.RecordNumber.Should().Be(3);

            table.SeekAtOrBefore(50m).Should().Be(SeekResult.Before);
            table.RecordNumber.Should().Be(1);

            table.SeekAtOrAfter(200m).Should().Be(SeekResult.Eof);
            table.Seek(12.4999m).Should().Be(GoResult.NoRecord);
        }
    }

    [Fact]
    public void ACurrencyWithAFifthDecimalIsRefused_AndTheSearchThatWasRunningSurvives()
    {
        // A currency holds ten-thousandths; the key transform would truncate a fifth decimal, and a
        // positional seek would then report Found for a value no key equals (ADR-80).
        (CodeBaseEngine engine, Table table) = Currencies(12.5m, -3m, 100m);
        using (engine)
        {
            table.Seek(12.5m).Should().Be(GoResult.Ok);

            table.Invoking(t => t.SeekAtOrAfter(12.50001m)).Should().Throw<CodeBaseException>()
                .Where(e => e.Code == ErrorCode.Info && e.Message.Contains("ten-thousandths"));

            table.RecordNumber.Should().Be(1);
            table.Invoking(t => t.SeekNext()).Should().NotThrow("the currency search is still running");
        }
    }

    [Fact]
    public void ADateTimeTagIsSoughtByADateTime_ExactlyOrToEitherSide()
    {
        (CodeBaseEngine engine, Table table) = Moments(
            'T',
            new DateTime(2000, 1, 1, 10, 0, 0),
            new DateTime(1999, 12, 31, 23, 59, 59),
            new DateTime(2026, 11, 14, 12, 0, 0));
        using (engine)
        {
            table.Seek(new DateTime(2000, 1, 1, 10, 0, 0)).Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(1);

            table.SeekAtOrAfter(new DateTime(2000, 1, 1)).Should().Be(SeekResult.After);
            table.RecordNumber.Should().Be(1);

            table.SeekAtOrBefore(new DateTime(2000, 1, 1)).Should().Be(SeekResult.Before);
            table.RecordNumber.Should().Be(2);

            table.SeekAtOrAfter(new DateTime(2030, 1, 1)).Should().Be(SeekResult.Eof);
        }
    }

    [Theory]
    [InlineData(499, GoResult.Ok)]
    [InlineData(500, GoResult.NoRecord)] // half up, to 10:00:01
    [InlineData(-500, GoResult.Ok)]      // 09:59:59.500, up to 10:00:00
    [InlineData(-501, GoResult.NoRecord)]
    public void ATimestampSeekRoundsToTheSecond_AsTheKeysDo(int milliseconds, GoResult expected)
    {
        // A T field holds seconds, and its key, its text and GetDateTime all round half up (ADR-79).
        DateTime ten = new(2000, 1, 1, 10, 0, 0);
        (CodeBaseEngine engine, Table table) = Moments('T', ten);
        using (engine)
            table.Seek(ten.AddMilliseconds(milliseconds)).Should().Be(expected);
    }

    [Fact]
    public void AMillisecondTagIsSoughtWithItsMilliseconds()
    {
        DateTime ten = new(2000, 1, 1, 10, 0, 0);
        (CodeBaseEngine engine, Table table) = Moments('7', ten.AddMilliseconds(250), ten.AddMilliseconds(750));
        using (engine)
        {
            table.Seek(ten.AddMilliseconds(750)).Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(2);

            table.Seek(ten.AddMilliseconds(500)).Should().Be(GoResult.NoRecord);
            table.SeekAtOrAfter(ten.AddMilliseconds(500)).Should().Be(SeekResult.After);
            table.RecordNumber.Should().Be(2);
        }
    }

    [Fact]
    public void AMillisecondValueFinerThanAMillisecondIsRefused()
    {
        DateTime ten = new(2000, 1, 1, 10, 0, 0);
        (CodeBaseEngine engine, Table table) = Moments('7', ten);
        using (engine)
        {
            table.Invoking(t => t.Seek(ten.AddTicks(1))).Should().Throw<CodeBaseException>()
                .Where(e => e.Code == ErrorCode.Info && e.Message.Contains("millisecond"));
        }
    }

    [Fact]
    public void ADecimalOrADateTimeIsRefusedOnATagOfAnotherKind()
    {
        (CodeBaseEngine engine, Table table) = Integers(1, 2);
        using (engine)
        {
            table.Invoking(t => t.Seek(1m)).Should().Throw<CodeBaseException>()
                .Where(e => e.Code == ErrorCode.Info && e.Message.Contains("a currency"));
            table.Invoking(t => t.Seek(new DateTime(2000, 1, 1))).Should().Throw<CodeBaseException>()
                .Where(e => e.Code == ErrorCode.Info && e.Message.Contains("a datetime"));
        }
    }

    // ------------------------------------------------------------------ plumbing

    private static (CodeBaseEngine Engine, Table Table) Integers(params int[] values) =>
        Open(
            new KeyField("COUNT", 'I', 4),
            [.. values.Select(Int)],
            Sorted(values.Select((v, i) => (Int32Key(v), (uint)(i + 1)))));

    private static (CodeBaseEngine Engine, Table Table) Open(
        KeyField field,
        byte[]?[] values,
        (byte[] Key, uint Record)[] keys,
        string? expression = null,
        string collation = "")
    {
        (CodeBaseEngine engine, Table table) = IndexedTableImage.Open(
            field, values, keys, expression, collation, Encoding.Latin1);
        table.SelectTag(table.Tags[0]);
        return (engine, table);
    }

    /// <summary>Orders keys as the tag holds them: by their bytes, then by record.</summary>
    private static (byte[] Key, uint Record)[] Sorted(IEnumerable<(byte[] Key, uint Record)> keys) =>
        [.. keys.Order(Comparer<(byte[] Key, uint Record)>.Create((a, b) =>
        {
            int order = a.Key.AsSpan().SequenceCompareTo(b.Key);
            return order != 0 ? order : a.Record.CompareTo(b.Record);
        }))];

    private static (CodeBaseEngine Engine, Table Table) Currencies(params decimal[] values) =>
        Open(
            new KeyField("PRICE", 'Y', 8, 4),
            [.. values.Select(v => Int64((long)(v * 10000m)))],
            Sorted(values.Select((v, i) => (CurrencyKey(v), (uint)(i + 1)))));

    private static (CodeBaseEngine Engine, Table Table) Moments(char type, params DateTime[] values) =>
        Open(
            new KeyField("STAMP", type, 8),
            [.. values.Select(Moment)],
            Sorted(values.Select((v, i) => (MomentKey(v, keepMilliseconds: type == '7'), (uint)(i + 1)))));

    private static byte[] Int64(long value)
    {
        byte[] bytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] CurrencyKey(decimal value)
    {
        byte[] key = new byte[8];
        KeyTransform.FromCurrency(value, key);
        return key;
    }

    /// <summary>The Julian day of 0001-01-01, which [c]DateOnly.DayNumber[/c] counts from.</summary>
    private const int JulianOffset = 1_721_426;

    private static byte[] Moment(DateTime value)
    {
        byte[] bytes = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, DateOnly.FromDateTime(value).DayNumber + JulianOffset);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), (int)value.TimeOfDay.TotalMilliseconds);
        return bytes;
    }

    private static byte[] MomentKey(DateTime value, bool keepMilliseconds)
    {
        byte[] key = new byte[8];
        KeyTransform.FromDateTime(
            DateOnly.FromDateTime(value).DayNumber + JulianOffset,
            (int)value.TimeOfDay.TotalMilliseconds,
            key,
            keepMilliseconds);
        return key;
    }

    private static byte[] Text(string value) => Encoding.Latin1.GetBytes(value);

    private static byte[] Int(int value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] Int32Key(int value)
    {
        byte[] key = new byte[4];
        KeyTransform.FromInt32(value, key);
        return key;
    }

    private static byte[] Double(double value)
    {
        byte[] key = new byte[8];
        KeyTransform.FromDouble(value, key);
        return key;
    }

    private static byte[] Date(int year, int month, int day)
    {
        byte[] key = new byte[8];
        KeyTransform.FromDate(new DateOnly(year, month, day), key);
        return key;
    }

    private static byte[] Complement(byte[] text) => [.. text.Select(b => (byte)~b)];

    private static byte[] Collated(byte[] text)
    {
        byte[] key = new byte[2 * text.Length];
        CollatedKey.Write(
            text,
            CollationWeights.TableFor(CollationName.General, CodePageMap.UnmarkedCodePage),
            CollationWeights.ExpansionsFor(CollationName.General, CodePageMap.UnmarkedCodePage),
            includeTails: true,
            key);
        return key;
    }
}
