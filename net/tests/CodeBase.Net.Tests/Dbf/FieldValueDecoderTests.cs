using System.Buffers.Binary;
using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Dbf;
using Xunit;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// The per-type matrix: what each accessor does with each type, and which pairings are refused.
///
/// Three outcomes per pairing rather than two. The natural decodes are gated against the corpus, so
/// what is left for here is the refusals, the two cross-type conversions the reference performs, and
/// the values no corpus table holds.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class FieldValueDecoderTests
{
    [Theory]
    [InlineData('L')]
    [InlineData('T')]
    [InlineData('7')]
    [InlineData('0')]
    public void Double_OfATypeTheReferenceRefuses_IsRefused(char type)
    {
        // f4double raises e4parm for these under the error checking the shipped build has on.
        Action act = () => Decode(type, 8, d => FieldValueDecoder.Double(d.Record, d.Field));

        act.Should().Throw<CodeBaseException>().WithMessage("*cannot be read as a number*");
    }

    [Theory]
    [InlineData('L')]
    [InlineData('T')]
    [InlineData('7')]
    [InlineData('0')]
    public void Int32_OfATypeTheReferenceRefuses_IsRefused(char type)
    {
        Action act = () => Decode(type, 8, d => FieldValueDecoder.Int32(d.Record, d.Field));

        act.Should().Throw<CodeBaseException>();
    }

    [Theory]
    [InlineData('C')]
    [InlineData('N')]
    [InlineData('D')]
    [InlineData('I')]
    public void Boolean_OfAnythingButALogical_IsRefused(char type)
    {
        Action act = () => Decode(type, 8, d => FieldValueDecoder.Boolean(d.Record, d.Field));

        act.Should().Throw<CodeBaseException>().WithMessage("*truth value*");
    }

    [Theory]
    [InlineData('C')]
    [InlineData('T')]
    [InlineData('N')]
    public void Date_OfAnythingButADate_IsRefused(char type)
    {
        Action act = () => Decode(type, 8, d => FieldValueDecoder.Date(d.Record, d.Field));

        act.Should().Throw<CodeBaseException>().WithMessage("*as a date*");
    }

    [Theory]
    [InlineData('C')]
    [InlineData('D')]
    [InlineData('Y')]
    public void DateTime_OfAnythingButADateTime_IsRefused(char type)
    {
        Action act = () => Decode(type, 8, d => FieldValueDecoder.DateTime(d.Record, d.Field));

        act.Should().Throw<CodeBaseException>().WithMessage("*date and time*");
    }

    [Theory]
    [InlineData('C')]
    [InlineData('N')]
    [InlineData('B')]
    public void Decimal_OfAnythingButACurrency_IsRefused(char type)
    {
        Action act = () => Decode(type, 8, d => FieldValueDecoder.Decimal(d.Record, d.Field));

        act.Should().Throw<CodeBaseException>().WithMessage("*exact decimal*");
    }

    [Fact]
    public void Double_OfADate_IsItsJulianDay()
    {
        // A cross-type conversion the reference performs and the port has to keep, because stored
        // values were produced by it. See Decision 9.
        double value = Decode('D', 8, d => FieldValueDecoder.Double(d.Record, d.Field), "19810101");

        value.Should().Be(2444606, "the C library documents Jan 1 1981 as Julian day 2444606");
    }

    [Fact]
    public void Double_OfACurrency_GoesThroughItsFourDecimalPlaces()
    {
        byte[] bytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, -999999999);

        double value = Decode('Y', 8, d => FieldValueDecoder.Double(d.Record, d.Field), bytes);

        value.Should().Be(-99999.9999);
    }

    [Theory]
    [InlineData('Y', true)]
    [InlineData('y', true)]
    [InlineData('T', true)]
    [InlineData('t', true)]
    [InlineData('N', false)]
    [InlineData('n', false)]
    [InlineData('F', false)]
    [InlineData(' ', false)]
    [InlineData('?', false)]
    public void Boolean_IsTrueForFourLettersAndFalseForEverythingElse(char stored, bool expected)
    {
        bool value = Decode(
            'L', 1, d => FieldValueDecoder.Boolean(d.Record, d.Field), stored.ToString());

        value.Should().Be(expected);
    }

    [Fact]
    public void Int32_OfAnInteger_ReadsFourBytesLittleEndian()
    {
        byte[] min = [0x00, 0x00, 0x00, 0x80];
        byte[] max = [0xFF, 0xFF, 0xFF, 0x7F];

        Decode('I', 4, d => FieldValueDecoder.Int32(d.Record, d.Field), min).Should().Be(int.MinValue);
        Decode('I', 4, d => FieldValueDecoder.Int32(d.Record, d.Field), max).Should().Be(int.MaxValue);
    }

    [Fact]
    public void AFixedWidthType_ReadsItsNaturalWidthAndIgnoresAShortDescriptor()
    {
        // The descriptor says four bytes; a datetime is eight and takes them from what follows. That
        // is what the C library does, and it stays inside the record. See Decision 10.
        byte[] record = new byte[1 + 8];
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(1), 2444606);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(5), 0);

        RecordBuffer buffer = Buffer(record);
        FieldDefinition field = Field('T', length: 4);

        FieldValueDecoder.Bytes(buffer, field).Length.Should().Be(8);
        FieldValueDecoder.DateTime(buffer, field).Should().Be(new DateTime(1981, 1, 1));
    }

    [Theory]
    [InlineData('M')]
    [InlineData('X')]
    [InlineData('G')]
    public void IsMemo_IsTrueForTheThreeTypesWhoseValueLivesElsewhere(char type)
    {
        FieldValueDecoder.IsMemo(Field(type, 4)).Should().BeTrue();
    }

    [Theory]
    [InlineData('C')]
    [InlineData('Z')]
    [InlineData('N')]
    public void IsMemo_IsFalseForATypeStoredInTheRecord(char type)
    {
        // Z especially: a binary character field is stored in the record like any other character
        // field and shares nothing with the memo path. See Decision 12.
        FieldValueDecoder.IsMemo(Field(type, 8)).Should().BeFalse();
    }

    [Theory]
    [InlineData('C')]
    [InlineData('N')]
    [InlineData('D')]
    public void MemoBlock_OfAFieldThatHoldsNoReference_IsRefused(char type)
    {
        Action act = () => Decode(type, 8, d => FieldValueDecoder.MemoBlock(d.Record, d.Field));

        act.Should().Throw<CodeBaseException>().WithMessage("*read as a memo*");
    }

    [Theory]
    [InlineData('X')]
    [InlineData('G')]
    [InlineData('Z')]
    public void ABinaryField_RefusesToBeReadAsText(char type)
    {
        // Decoding bytes that are not in the table's code page always produces a string and the
        // string is always meaningless, which the caller has no way to notice. See Decision 11.
        Action act = () => FieldValueDecoder.RefuseIfBinary(Field(type, 8));

        act.Should().Throw<CodeBaseException>().WithMessage("*marked binary*");
    }

    [Theory]
    [InlineData('C')]
    [InlineData('M')]
    public void ATextFieldIsNotRefused(char type)
    {
        // A plain memo is text: it is the binary variants that are not.
        Action act = () => FieldValueDecoder.RefuseIfBinary(Field(type, 8));

        act.Should().NotThrow();
    }

    [Fact]
    public void MemoBlock_ReadsTheReferenceOutOfTheRecord()
    {
        int block = Decode(
            'M', 4, d => FieldValueDecoder.MemoBlock(d.Record, d.Field), [0x1B, 0x00, 0x00, 0x00]);

        block.Should().Be(27);
    }

    [Fact]
    public void AFixedWidthType_ThatWouldLeaveTheRecord_IsStillRefused()
    {
        // Reading into the next field is allowed; reading out of the record is not.
        RecordBuffer buffer = Buffer(new byte[1 + 4]);

        Action act = () => FieldValueDecoder.DateTime(buffer, Field('T', length: 4));

        act.Should().Throw<CodeBaseException>().WithMessage("*does not lie inside its record*");
    }

    /// <summary>
    /// Decodes a one-field record built from text.
    /// </summary>
    private static T Decode<T>(char type, int length, Func<(RecordBuffer Record, FieldDefinition Field), T> read, string contents) =>
        Decode(type, length, read, Encoding.ASCII.GetBytes(contents));

    /// <summary>
    /// Decodes a one-field record built from bytes, zero filled when none are given.
    /// </summary>
    private static T Decode<T>(
        char type,
        int length,
        Func<(RecordBuffer Record, FieldDefinition Field), T> read,
        byte[]? contents = null)
    {
        byte[] record = new byte[1 + length];
        contents?.AsSpan(0, Math.Min(contents.Length, length)).CopyTo(record.AsSpan(1));

        return read((Buffer(record), Field(type, length)));
    }

    private static RecordBuffer Buffer(byte[] bytes)
    {
        RecordBuffer buffer = new(bytes.Length);
        bytes.CopyTo(buffer.Raw);
        return buffer;
    }

    private static FieldDefinition Field(char type, int length) =>
        new("F", type, type, length, 0, 1, false, false, false, false, null);
}
