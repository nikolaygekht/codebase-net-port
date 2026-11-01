using System.Buffers.Binary;
using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using Xunit;

namespace CodeBase.Net.Tests.Expressions;

/// <summary>
/// What a field reads as when an expression loads it from a record, one field type at a time.
/// </summary>
/// <value>
/// The record is laid down here in the stored form DBF-FORMAT.md gives each type, so every
/// expectation is the value written into it. A character, numeric or date field enters as the text
/// the record holds, and the binary types as the value their bytes encode -- which for an integer,
/// a currency, a float and a timestamp means the value's raw form is the stored bytes again
/// (ExpressionValue.ToRaw). The kernels themselves are ExpressionEvaluatorTests'; what is here is
/// the path from a record to them.
/// </value>
[Trait("Layer", "Unit")]
public sealed class ExpressionFieldLoaderTests
{
    private const int Julian2000January15 = 2451559;
    private const int TwelveThirtyFourFiftySix = ((12 * 3600) + (34 * 60) + 56) * 1000;

    private static readonly FieldDefinition[] Definitions =
    [
        Field("K_NUM", 'N', 8, 1, decimals: 2),
        Field("K_DATE", 'D', 8, 9),
        Field("K_LOG", 'L', 1, 17),
        Field("K_INT", 'I', 4, 18),
        Field("K_CUR", 'Y', 8, 22, decimals: 4),
        Field("K_TS", 'T', 8, 30),
        Field("K_DBL", 'B', 8, 38, decimals: 2),
        Field("K_FLT", 'H', 4, 46),
        Field("K_MEMO", 'M', 4, 50),
        Field("K_BLANKD", 'D', 8, 54),
        Field("K_BLANKT", 'T', 8, 62),
        Field("K_NINT", 'I', 4, 70, nullBit: 9),
        Field("K_FARINT", 'I', 4, 74, nullBit: 16),
        Field("_NullFlags", '0', 2, 78),
    ];

    private static readonly IFieldMetadata Fields = new TableFieldMetadata(Definitions, "T");

    [Fact]
    public void ANumericFieldEntersAsTheTextTheRecordHolds()
    {
        ExpressionValue value = Value("K_NUM");

        value.Type.Should().Be(ExpressionType.NumericAsText);
        value.IsNull.Should().BeFalse();
        Ascii(value).Should().Be("   12.50");
    }

    [Fact]
    public void ANumericFieldInArithmeticIsReadAsItsNumber() =>
        Value("K_NUM + 0").Number.Should().Be(12.5);

    [Fact]
    public void ADateFieldEntersAsItsEightCharacters()
    {
        ExpressionValue value = Value("K_DATE");

        value.Type.Should().Be(ExpressionType.DateAsText);
        value.IsNull.Should().BeFalse();
        Ascii(value).Should().Be("20000115");
    }

    [Fact]
    public void ADateFieldInArithmeticIsReadAsItsJulianDay() =>
        Value("K_DATE + 1").Number.Should().Be(Julian2000January15 + 1);

    [Fact]
    public void ALogicalFieldIsReadAsTrueOrFalse() =>
        Value("K_LOG").Logical.Should().BeTrue();

    [Fact]
    public void AnIntegerFieldIsReadAsItsWholeNumber()
    {
        ExpressionValue value = Value("K_INT");

        value.Type.Should().Be(ExpressionType.Integer);
        value.Integer.Should().Be(-7);
        value.ToRaw().Should().Equal(Stored("K_INT"));
    }

    [Fact]
    public void ACurrencyFieldIsReadAsItsScaledWholeNumber()
    {
        ExpressionValue value = Value("K_CUR");

        value.Type.Should().Be(ExpressionType.Currency);
        value.Scaled.Should().Be(123456);
        value.ToRaw().Should().Equal(Stored("K_CUR"));
    }

    [Fact]
    public void ATimestampFieldIsReadAsItsDayAndItsTimeOfDay()
    {
        ExpressionValue value = Value("K_TS");

        value.TimestampDay.Should().Be(Julian2000January15);
        value.TimestampTime.Should().Be(TwelveThirtyFourFiftySix);
        value.ToRaw().Should().Equal(Stored("K_TS"));
    }

    [Fact]
    public void ADoubleFieldIsReadAsItsNumber() =>
        Value("K_DBL").Number.Should().Be(2.5);

    [Fact]
    public void ABinaryFloatFieldIsReadAsAFloat()
    {
        ExpressionValue value = Value("K_FLT");

        value.Type.Should().Be(ExpressionType.BinaryFloat);
        value.Number.Should().Be(1.5);
        value.ToRaw().Should().Equal(Stored("K_FLT"));
    }

    [Fact]
    public void AnIntegerBesideAFloatIsReadAsAFloat()
    {
        // The promotion rewrites the integer's loader (TypeResolverTests), so the integer arrives
        // already a float and the product is a float's.
        ExpressionValue value = Value("K_FLT * K_INT");

        value.Type.Should().Be(ExpressionType.BinaryFloat);
        value.Number.Should().Be(-10.5);
    }

    [Theory]
    // Float arithmetic stays float (EXPRESSIONS.md section 6), every operator.
    [InlineData("K_FLT + K_FLT", 3)]
    [InlineData("K_FLT - K_FLT", 0)]
    [InlineData("K_FLT / K_FLT", 1)]
    public void FloatArithmeticKeepsTheFloatType(string expression, double expected)
    {
        ExpressionValue value = Value(expression);

        value.Type.Should().Be(ExpressionType.BinaryFloat);
        value.Number.Should().Be(expected);
    }

    [Fact]
    public void AMemoIsReadIntoTheOperandsWidthAndZeroPadded()
    {
        // A memo operand is 1024 wide, padded with zeros rather than blanks (EXPRESSIONS.md 9.5).
        byte[] memo = Encoding.ASCII.GetBytes("HELLO");
        ExpressionValue value = Value("K_MEMO", Record(memoBlock: 5), _ => memo);

        value.Bytes.Length.Should().Be(1024);
        value.Bytes[..5].ToArray().Should().Equal(memo);
        value.Bytes[5..].ToArray().Should().OnlyContain(b => b == 0);
    }

    [Fact]
    public void AMemoLongerThanTheOperandIsCut()
    {
        byte[] memo = Enumerable.Repeat((byte)'M', 1100).ToArray();

        Value("K_MEMO", Record(memoBlock: 5), _ => memo).Bytes.ToArray().Should().Equal(memo[..1024]);
    }

    [Fact]
    public void ARecordWithNoMemoReadsNothing()
    {
        bool asked = false;
        ExpressionValue value = Value("K_MEMO", Record(memoBlock: 0), _ =>
        {
            asked = true;
            return new byte[] { 1 };
        });

        value.Bytes.ToArray().Should().OnlyContain(b => b == 0);
        asked.Should().BeFalse("block zero means the record has no memo to read");
    }

    [Fact]
    public void AMemoWithNothingToReadItFromIsEmpty() =>
        Value("K_MEMO", Record(memoBlock: 5), memo: null).Bytes.ToArray().Should().OnlyContain(b => b == 0);

    [Fact]
    public void AFieldCannotBeReadWithoutARecord()
    {
        Action act = () => new ExpressionEvaluator(Compile("K_INT")).Evaluate(new EvaluationContext());

        act.Should().Throw<CodeBaseException>()
            .Where(e => e.Code == ErrorCode.NotSupported && e.Message.Contains("K_INT"));
    }

    [Theory]
    // The null bitmap is read bit by bit across its bytes: bit 9 is the second byte's second bit.
    [InlineData(0x00, 0x02, true)]
    [InlineData(0x02, 0x00, false)]
    [InlineData(0xFF, 0xFD, false)]
    public void ANullBitIsFoundInTheByteItFallsIn(byte first, byte second, bool absent)
    {
        RecordBuffer record = Record(memoBlock: 0);
        record.Raw[78] = first;
        record.Raw[79] = second;

        Value("K_NINT", record, memo: null).IsNull.Should().Be(absent);
    }

    [Fact]
    public void ANullBitPastTheEndOfTheBitmapReadsAsPresent()
    {
        // A field claiming a bit the flags field has no byte for: read as there, not past the end.
        RecordBuffer record = Record(memoBlock: 0);
        record.Raw[78] = 0xFF;
        record.Raw[79] = 0xFF;

        Value("K_FARINT", record, memo: null).IsNull.Should().BeFalse();
    }

    [Theory]
    // The date and timestamp kernels over a field, whose stored text is handed over as it is
    // (EXPRESSIONS.md section 7.3): a blank date keeps the picture's separators, and a blank
    // timestamp is eight blanks and a zero time, both witnessed.
    [InlineData("DTOC(K_DATE)", "01/15/00")]
    [InlineData("DTOC(K_BLANKD)", "  /  /  ")]
    [InlineData("TTOC(K_TS)", "20000115123456")]
    [InlineData("TTOC(K_DATE)", "20000115000000")]
    [InlineData("TTOC(K_BLANKT)", "        000000")]
    public void TheDateKernelsReadAFieldsStoredForm(string expression, string expected) =>
        Ascii(Value(expression)).Should().Be(expected);

    [Theory]
    // Equality by each binary type's own value (EXPRESSIONS.md section 8).
    [InlineData("K_CUR = K_CUR", true)]
    [InlineData("K_TS = K_TS", true)]
    [InlineData("K_INT = K_INT", true)]
    [InlineData("K_FLT = K_FLT", true)]
    [InlineData("K_FLT = K_FLT + K_FLT", false)]
    [InlineData("K_DATE = K_DATE", true)]
    public void BinaryValuesAreEqualByTheirOwnValue(string expression, bool expected) =>
        Value(expression).Logical.Should().Be(expected);

    private static ExpressionValue Value(string expression) =>
        Value(expression, Record(memoBlock: 0), memo: null);

    private static ExpressionValue Value(
        string expression, RecordBuffer record, Func<FieldDefinition, ReadOnlyMemory<byte>>? memo) =>
        new ExpressionEvaluator(Compile(expression)).Evaluate(new EvaluationContext
        {
            Record = record,
            NullFlags = Definitions.Single(f => f.Name == "_NullFlags"),
            Options = ExpressionOptions.CodeBase.WithCentury(20),
            Memo = memo,
        });

    private static CompiledExpression Compile(string expression) =>
        TypeResolver.Resolve(ExpressionParser.Parse(expression, Fields));

    private static string Ascii(ExpressionValue value) => Encoding.ASCII.GetString(value.Bytes);

    private static byte[] Stored(string name)
    {
        FieldDefinition field = Definitions.Single(f => f.Name == name);
        return Record(memoBlock: 0).Raw.Slice(field.RecordOffset, field.Length).ToArray();
    }

    /// <summary>
    /// Lays down one record holding a value of every type, in each type's stored form.
    /// </summary>
    private static RecordBuffer Record(int memoBlock)
    {
        RecordBuffer record = new(80);
        Span<byte> raw = record.Raw;

        raw[0] = (byte)' ';
        Encoding.ASCII.GetBytes("   12.50").CopyTo(raw[1..]);
        Encoding.ASCII.GetBytes("20000115").CopyTo(raw[9..]);
        raw[17] = (byte)'T';
        BinaryPrimitives.WriteInt32LittleEndian(raw[18..], -7);
        BinaryPrimitives.WriteInt64LittleEndian(raw[22..], 123456);
        BinaryPrimitives.WriteInt32LittleEndian(raw[30..], Julian2000January15);
        BinaryPrimitives.WriteInt32LittleEndian(raw[34..], TwelveThirtyFourFiftySix);
        BinaryPrimitives.WriteDoubleLittleEndian(raw[38..], 2.5);
        BinaryPrimitives.WriteSingleLittleEndian(raw[46..], 1.5f);
        BinaryPrimitives.WriteInt32LittleEndian(raw[50..], memoBlock);
        Encoding.ASCII.GetBytes("        ").CopyTo(raw[54..]);

        return record;
    }

    private static FieldDefinition Field(
        string name, char type, int length, int offset, int decimals = 0, int? nullBit = null) =>
        new(name, type, type, length, decimals, offset, false, false, false, false, nullBit);
}
