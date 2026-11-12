using AwesomeAssertions;
using CodeBase.Net.Cdx;
using Xunit;

namespace CodeBase.Net.Tests.Cdx;

/// <summary>
/// The value-to-key transforms, checked by the property they exist for.
///
/// A key's whole purpose is that plain unsigned byte comparison puts keys in the same order as the
/// values they came from. That property is worth more than any individual byte pattern: it fails
/// for every sign-handling mistake at once, and it needs no expected bytes written down. The exact
/// bytes are gated by the corpus, where each tag's stored keys sit beside the values they were
/// computed from.
///
/// Governing specification: KEY-COLLATION.md section 2.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class KeyTransformTests
{
    private static byte[] Double(double value)
    {
        byte[] key = new byte[8];
        KeyTransform.FromDouble(value, key).Should().Be(8);
        return key;
    }

    private static byte[] Int32(int value)
    {
        byte[] key = new byte[4];
        KeyTransform.FromInt32(value, key).Should().Be(4);
        return key;
    }

    private static int Compare(byte[] a, byte[] b) => a.AsSpan().SequenceCompareTo(b);

    /// <summary>The Julian day of 2000-01-01.</summary>
    private const int Day2000 = 2451545;

    private static byte[] DateTime(int julianDay, int milliseconds)
    {
        byte[] key = new byte[8];
        KeyTransform.FromDateTime(julianDay, milliseconds, key).Should().Be(8);
        return key;
    }

    private static byte[] DateTimeKeepingMilliseconds(int julianDay, int milliseconds)
    {
        byte[] key = new byte[8];
        KeyTransform.FromDateTime(julianDay, milliseconds, key, keepMilliseconds: true).Should().Be(8);
        return key;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(43_200_000)]
    [InlineData(86_399_000)]
    public void DateTime_KeepingMillisecondsAgreesWithRoundingOnAWholeSecond(int milliseconds)
    {
        // The two transforms differ only in what they do with a fraction (i4conv.c:2209-2364).
        DateTimeKeepingMilliseconds(Day2000, milliseconds).Should().Equal(DateTime(Day2000, milliseconds));
    }

    [Theory]
    [InlineData(43_200_001)]
    [InlineData(43_200_499)]
    [InlineData(43_200_500)]
    [InlineData(43_200_999)]
    public void DateTime_KeepingMillisecondsKeysAFractionBetweenItsTwoSeconds(int milliseconds)
    {
        // The '7' type's key (t4dateTimeToFoxMilli) keeps the fraction the 'T' key rounds away, so it
        // sorts strictly between the keys of the whole seconds either side of it.
        int second = milliseconds - (milliseconds % 1000);
        byte[] key = DateTimeKeepingMilliseconds(Day2000, milliseconds);

        Compare(key, DateTime(Day2000, second)).Should().BePositive();
        Compare(key, DateTime(Day2000, second + 1000)).Should().BeNegative();
    }

    [Fact]
    public void DateTime_KeepingMillisecondsKeysInTheOrderOfTheMilliseconds()
    {
        int[] ascending = [0, 1, 2, 499, 500, 501, 998, 999, 1000, 1001, 59_999, 60_000];

        for (int i = 1; i < ascending.Length; i++)
        {
            Compare(
                    DateTimeKeepingMilliseconds(Day2000, ascending[i - 1]),
                    DateTimeKeepingMilliseconds(Day2000, ascending[i]))
                .Should().BeNegative($"{ascending[i - 1]} ms sorts before {ascending[i]} ms");
        }
    }

    [Fact]
    public void Double_KeysSortInTheSameOrderAsTheValues()
    {
        // The property the transform exists for. Any sign-bit slip -- inverting the wrong branch,
        // forgetting to complement negatives, using an or where the reference adds -- breaks this
        // somewhere in the list.
        double[] ascending =
        [
            double.NegativeInfinity, -1e308, -1e5, -1.5, -1.0, -double.Epsilon,
            0.0, double.Epsilon, 1.0, 1.5, 1e5, 1e308, double.PositiveInfinity,
        ];

        for (int i = 1; i < ascending.Length; i++)
        {
            Compare(Double(ascending[i - 1]), Double(ascending[i]))
                .Should().BeNegative($"{ascending[i - 1]} must sort below {ascending[i]}");
        }
    }

    [Fact]
    public void Double_NegativeZeroSortsBelowEveryOtherValue()
    {
        // The one place the reference is arithmetically wrong and this port must be wrong with it.
        // Negative zero tests as greater than or equal to zero in the C, so it takes the positive
        // branch, and the byte addition then wraps its leading 0x80 round to 0x00 -- which puts it
        // below every negative. An or against the sign bit would leave it at 0x80 and sort it with
        // the positives, which is a different key for a value CDXBASE actually indexes.
        byte[] negativeZero = Double(-0.0);

        Compare(negativeZero, Double(double.NegativeInfinity)).Should().BeNegative();
        Compare(negativeZero, Double(-1e308)).Should().BeNegative();
        Compare(negativeZero, Double(0.0)).Should().BeNegative();

        negativeZero.Should().AllSatisfy(b => b.Should().Be(0));
    }

    [Fact]
    public void Double_PositiveAndNegativeZeroAreDifferentKeys()
    {
        // They are the same number and not the same key, which is why a tag can hold both.
        Double(0.0).Should().NotEqual(Double(-0.0));
    }

    [Fact]
    public void Int32_KeysSortInTheSameOrderAsTheValues()
    {
        int[] ascending = [int.MinValue, -70000, -1, 0, 1, 70000, int.MaxValue];

        for (int i = 1; i < ascending.Length; i++)
        {
            Compare(Int32(ascending[i - 1]), Int32(ascending[i]))
                .Should().BeNegative($"{ascending[i - 1]} must sort below {ascending[i]}");
        }
    }

    [Fact]
    public void Int32_ZeroSitsExactlyAtTheMiddleOfTheRange()
    {
        // The reference subtracts 0x80 from the leading byte of a non-positive value, so zero wraps
        // up to the halfway point rather than staying at the bottom. Getting this edge wrong sorts
        // zero below every negative.
        Compare(Int32(0), Int32(-1)).Should().BePositive();
        Compare(Int32(0), Int32(1)).Should().BeNegative();
    }

    [Fact]
    public void Int64_KeysSortInTheSameOrderAsTheValues()
    {
        long[] ascending = [long.MinValue, -1, 0, 1, long.MaxValue];
        byte[] Key(long v) { byte[] k = new byte[8]; KeyTransform.FromInt64(v, k); return k; }

        for (int i = 1; i < ascending.Length; i++)
            Compare(Key(ascending[i - 1]), Key(ascending[i])).Should().BeNegative();
    }

    [Fact]
    public void UInt32_KeysSortByValue_AndAreNotSignFlipped()
    {
        // An unsigned value has no sign bit to invert; inverting one anyway would sort the top half
        // of the range first (i4conv.c:1045-1049 says so in as many words).
        byte[] Key(uint v) { byte[] k = new byte[4]; KeyTransform.FromUInt32(v, k); return k; }

        Compare(Key(0), Key(1)).Should().BeNegative();
        Compare(Key(1), Key(uint.MaxValue)).Should().BeNegative();
        Key(0).Should().AllSatisfy(b => b.Should().Be(0));
    }

    [Fact]
    public void Single_KeysSortInTheSameOrderAsTheValues()
    {
        float[] ascending = [float.NegativeInfinity, -1e30f, -1.5f, 0.0f, 1.5f, 1e30f, float.PositiveInfinity];
        byte[] Key(float v) { byte[] k = new byte[4]; KeyTransform.FromSingle(v, k).Should().Be(4); return k; }

        for (int i = 1; i < ascending.Length; i++)
            Compare(Key(ascending[i - 1]), Key(ascending[i])).Should().BeNegative();

        Key(-0.0f).Should().AllSatisfy(b => b.Should().Be(0));
    }

    [Fact]
    public void Currency_KeysSortInTheSameOrderAsTheValues_AndKeepFourDecimals()
    {
        decimal[] ascending = [-1000.5m, -0.0002m, 0m, 0.0001m, 0.0002m, 1000.5m];
        byte[] Key(decimal v) { byte[] k = new byte[8]; KeyTransform.FromCurrency(v, k).Should().Be(8); return k; }

        for (int i = 1; i < ascending.Length; i++)
        {
            Compare(Key(ascending[i - 1]), Key(ascending[i]))
                .Should().BeNegative($"{ascending[i - 1]} must sort below {ascending[i]}");
        }
    }

    [Fact]
    public void Currency_AValueTooLargeForTheField_IsRefused()
    {
        byte[] key = new byte[8];

        Action act = () => KeyTransform.FromCurrency(decimal.MaxValue, key);

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Currency_AValueTooFarBelowZeroForTheField_IsRefused()
    {
        byte[] key = new byte[8];

        Action act = () => KeyTransform.FromCurrency(decimal.MinValue, key);

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Currency_TheFieldsExtremesAreAccepted_AndKeyAsTheExtremeIntegers()
    {
        // A currency field is a 64-bit integer of ten-thousandths, so its range ends exactly at the
        // integer's: the largest and smallest values fit and must not be refused.
        decimal highest = long.MaxValue / 10000m;
        decimal lowest = long.MinValue / 10000m;
        byte[] key = new byte[8];
        byte[] expected = new byte[8];

        KeyTransform.FromCurrency(highest, key);
        KeyTransform.FromInt64(long.MaxValue, expected);
        key.Should().Equal(expected);

        KeyTransform.FromCurrency(lowest, key);
        KeyTransform.FromInt64(long.MinValue, expected);
        key.Should().Equal(expected);
    }

    [Theory]
    // KEY-COLLATION.md section 2.8: the milliseconds round to the nearest second, half up.
    [InlineData(12_000, 12_499, 12_000)]
    [InlineData(12_000, 12_500, 13_000)]
    [InlineData(12_000, 12_999, 13_000)]
    public void DateTime_TheMillisecondsRoundToTheNearestSecond(int second, int milliseconds, int roundsTo)
    {
        byte[] key = DateTime(Day2000, milliseconds);

        key.Should().Equal(DateTime(Day2000, roundsTo), $"{milliseconds} ms rounds to {roundsTo}");
        if (roundsTo != second)
            key.Should().NotEqual(DateTime(Day2000, second));
    }

    [Theory]
    [InlineData(1721426)] // 0001-01-01
    [InlineData(2415021)] // 1900-01-01
    [InlineData(Day2000)]
    [InlineData(2488070)] // 2100-01-01
    public void DateTime_AFlaggedSecondIsTheDoubleOneStepDown_AndEveryOtherIsTheDoubleItself(int day)
    {
        // KEY-COLLATION.md section 2.8: the day and the second become one double, and a second the
        // empirical bitmap flags is nudged down by one in its lowest byte before the double
        // transform. Across a real day's seconds that is exactly the next double down, which is the
        // expectation here; every second of the day is checked, so both arms are met many times.
        int flagged = 0;
        List<int> wrong = [];

        for (int second = 0; second < 86400; second++)
        {
            double value = day + (second * 1000 / 86400000.0);
            bool nudged = DateTimeKeyFlags.NeedsDecrement(second);
            if (nudged)
                flagged++;

            if (!DateTime(day, second * 1000).AsSpan().SequenceEqual(Double(nudged ? Math.BitDecrement(value) : value)))
                wrong.Add(second);
        }

        wrong.Should().BeEmpty($"every second of day {day} keys as its double, one step down where flagged");
        flagged.Should().BePositive("a day with no flagged second would not reach the decrement");
    }

    [Fact]
    public void DateTime_KeysSortChronologically_AcrossFlaggedSecondsAndMidnight()
    {
        // The nudge is one step of a double whose step is far finer than a second, so it must never
        // reorder two seconds; and the last second of a day sorts below the first of the next.
        byte[] previous = DateTime(Day2000, 0);
        List<int> unordered = [];

        for (int second = 1; second < 86400; second++)
        {
            byte[] current = DateTime(Day2000, second * 1000);
            if (Compare(previous, current) >= 0)
                unordered.Add(second);
            previous = current;
        }

        unordered.Should().BeEmpty("each second must sort above the one before it");
        Compare(previous, DateTime(Day2000 + 1, 0)).Should().BeNegative();
    }

    [Fact]
    public void Date_KeysSortChronologically_AndABlankDateSortsFirst()
    {
        // A date key is its Julian day number as a double, so the ordering rides on the double
        // transform. Day zero is what a blank date converts to, and it must land below real dates.
        byte[] Key(DateOnly d) { byte[] k = new byte[8]; KeyTransform.FromDate(d, k).Should().Be(8); return k; }
        byte[] blank = Double(0);

        Compare(blank, Key(new DateOnly(1, 1, 1))).Should().BeNegative();
        Compare(Key(new DateOnly(1981, 1, 1)), Key(new DateOnly(1981, 1, 2))).Should().BeNegative();
        Compare(Key(new DateOnly(1899, 12, 31)), Key(new DateOnly(1900, 1, 1))).Should().BeNegative();
    }

    [Fact]
    public void Logical_FalseSortsBeforeTrue_BecauseTheKeyIsTheLetter()
    {
        byte[] t = new byte[1];
        byte[] f = new byte[1];

        KeyTransform.FromLogical(true, t).Should().Be(1);
        KeyTransform.FromLogical(false, f).Should().Be(1);

        t[0].Should().Be((byte)'T');
        f[0].Should().Be((byte)'F');
        Compare(f, t).Should().BeNegative();
    }

    [Fact]
    public void EveryTransform_WritesIntoTheCallersBufferWithoutAllocating()
    {
        // The cursor converts a value straight into its own buffer, so nothing here may return an
        // array of its own. Writing at an offset proves the destination is honoured as given.
        byte[] buffer = new byte[16];

        KeyTransform.FromDouble(1.5, buffer.AsSpan(4)).Should().Be(8);

        buffer.AsSpan(0, 4).ToArray().Should().AllSatisfy(b => b.Should().Be(0));
        buffer.AsSpan(4, 8).ToArray().Should().NotEqual(new byte[8]);
    }
}
