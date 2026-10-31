using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using Xunit;

namespace CodeBase.Net.Tests.Expressions;

/// <summary>
/// What every kernel computes, written against the specification.
///
/// This is the deciding gate for what a function computes, and the corpus is the confirming one. The
/// reason round that way is that a table of expression and expected value reads as a specification and
/// covers cases no record happens to hold, while a transcript of the reference's answers only
/// confirms our own reading of it. The corpus leads for exactly three things instead: the answers the
/// source does not state, which overload actually ran, and the filters, which have no output.
///
/// Almost every case is a constant, and that is the point. The edge sits in the literal where it
/// reads as a rule, there is no record dimension to get wrong, and a failure cannot be a
/// field-decoding bug in disguise, because that path belongs to the reader and is gated already.
/// Fields appear only for what no literal reaches: the four field-loader-only types, a memo, and
/// absence in any type.
///
/// Governing specification: EXPRESSIONS.md sections 7 and 8.
/// </summary>
public sealed class ExpressionEvaluatorTests
{
    private static readonly IFieldMetadata Fields = new TableFieldMetadata(
        [
            Field("K_TEXT", 'C', 20),
            Field("K_CITY", 'C', 15),
            Field("K_NUM", 'N', 12, decimals: 3),
            Field("K_INT", 'I', 4),
            Field("K_DATE", 'D', 8),
            Field("K_TS", 'T', 8),
            Field("K_LOG", 'L', 1),
            Field("K_CUR", 'Y', 8, decimals: 4),
            Field("K_NNUM", 'N', 12, decimals: 3, nullBit: 0),
            Field("_NullFlags", '0', 1),
        ],
        "T");

    // ------------------------------------------------------------------ text

    [Theory]
    [InlineData("UPPER(\"john smith\")", "JOHN SMITH")]
    [InlineData("UPPER(\"\")", "")]
    [InlineData("UPPER(\"MiXeD 12\")", "MIXED 12")]
    public void UpperCasingIsAsciiAndNothingElse(string expression, string expected) =>
        Text(expression).Should().Be(expected);

    [Fact]
    public void UpperCasingLeavesABytePastAsciiAlone()
    {
        // The reference picks between an ASCII mapping and the Windows ANSI one by the table's code
        // page, and a table with no code-page mark takes the ASCII one. So a byte above 0x7F is not
        // a letter here, whatever a culture-aware routine would say about it.
        Bytes("UPPER(\"Éè\")").Should().Equal((byte)0xC9, (byte)0xE8);
    }

    [Theory]
    // A trim keeps the source width and fills the freed bytes with zero, not with blanks. A port
    // that returned the short string is wrong before it ever reaches key time.
    [InlineData("TRIM(\"abc   \")", "abc\0\0\0")]
    [InlineData("TRIM(\"   \")", "\0\0\0")]
    [InlineData("TRIM(\"abc\")", "abc")]
    [InlineData("LTRIM(\"   abc\")", "abc\0\0\0")]
    [InlineData("LTRIM(\"abc\")", "abc")]
    [InlineData("ALLTRIM(\"  abc  \")", "abc\0\0\0\0")]
    public void ATrimNarrowsTheValueWithoutNarrowingTheField(string expression, string expected) =>
        Text(expression).Should().Be(expected);

    [Theory]
    [InlineData("TRIM(\"abc   \")", 3)]
    [InlineData("TRIM(\"   \")", 0)]
    [InlineData("ALLTRIM(\"  abc  \")", 3)]
    [InlineData("UPPER(\"abc   \")", 6)]
    public void ATrimRecordsHowFarTheValueStillReaches(string expression, int reach) =>
        Value(expression).TrimmedLength.Should().Be(reach);

    [Theory]
    [InlineData("SUBSTR(\"ABCDEF\",2,3)", "BCD")]
    [InlineData("SUBSTR(\"ABCDEF\",1,6)", "ABCDEF")]
    [InlineData("SUBSTR(\"ABCDEF\",5,10)", "EF")]
    [InlineData("SUBSTR(\"ABCDEF\",3)", "CDEF")]
    [InlineData("LEFT(\"ABCDEF\",3)", "ABC")]
    [InlineData("LEFT(\"ABC\",10)", "ABC")]
    [InlineData("RIGHT(\"ABCDEF\",3)", "DEF")]
    [InlineData("RIGHT(\"ABC\",10)", "ABC")]
    public void TakingPartOfAStringNeverReachesPastWhatIsThere(string expression, string expected) =>
        Text(expression).Should().Be(expected);

    [Theory]
    [InlineData("PADL(\"X\",5)", "    X")]
    [InlineData("PADR(\"X\",5)", "X    ")]
    [InlineData("PADL(\"ABCDEF\",3)", "ABC")]
    [InlineData("SPACE(5)", "     ")]
    [InlineData("SPACE(0)", "")]
    [InlineData("CHR(65)", "A")]
    [InlineData("CHR(0)", "\0")]
    public void PaddingAndFillingProduceTheDeclaredWidth(string expression, string expected) =>
        Text(expression).Should().Be(expected);

    [Theory]
    // Plain joining keeps both operands whole, interior blanks included.
    [InlineData("\"AB\"+\"CD\"", "ABCD")]
    [InlineData("\"AB  \"+\"CD\"", "AB  CD")]
    // Subtraction joins them and moves the left one's trailing blanks to the end, which is what
    // keeps a key built from two fields fixed-length.
    [InlineData("\"AB  \"-\"CD\"", "ABCD  ")]
    [InlineData("\"AB\"-\"CD\"", "ABCD")]
    [InlineData("\"  \"-\"CD\"", "CD  ")]
    // A trim anywhere makes the next plain join squeeze out zero bytes instead.
    [InlineData("TRIM(\"AB  \")+\"CD\"", "ABCD\0\0")]
    public void JoiningTwoStringsHasThreeFormsAndOneWidth(string expression, string expected) =>
        Text(expression).Should().Be(expected);

    // ------------------------------------------------------------ arithmetic

    [Theory]
    [InlineData("1+2", 3)]
    [InlineData("5-3", 2)]
    [InlineData("1*3", 3)]
    [InlineData("7/2", 3.5)]
    [InlineData("2**10", 1024)]
    [InlineData("2^10", 1024)]
    [InlineData("-1", -1)]
    [InlineData("2.5+2.5", 5)]
    public void ArithmeticIsOrdinary(string expression, double expected) =>
        Value(expression).Number.Should().Be(expected);

    [Theory]
    [InlineData("1/0")]
    [InlineData("0/0")]
    [InlineData("-1/0")]
    public void DividingByZeroAnswersZeroRatherThanFailing(string expression) =>
        Value(expression).Number.Should().Be(0);

    // ------------------------------------------------------------ comparison

    [Theory]
    // Equality compares the common prefix and stops, so it is true in both directions. This is a
    // divergence from Visual FoxPro and it is the reference's answer that stored keys were built on.
    [InlineData("\"AAAA\" = \"AAA\"", true)]
    [InlineData("\"AAA\" = \"AAAA\"", true)]
    [InlineData("\"AAA\" = \"AAA\"", true)]
    [InlineData("\"AAA\" = \"AAB\"", false)]
    [InlineData("\"AAA\" <> \"AAAA\"", false)]
    [InlineData("\"AAA\" # \"AAB\"", true)]
    [InlineData("\"\" = \"ANYTHING\"", true)]
    public void EqualityComparesTheCommonPrefixOnly(string expression, bool expected) =>
        Value(expression).Logical.Should().Be(expected);

    [Theory]
    // The strict forms break a tie on the common prefix by which operand is wider.
    [InlineData("\"AAAA\" > \"AAA\"", true)]
    [InlineData("\"AAA\" > \"AAAA\"", false)]
    [InlineData("\"AAA\" < \"AAAA\"", true)]
    [InlineData("\"AAAA\" < \"AAA\"", false)]
    [InlineData("\"ABD\" > \"ABC\"", true)]
    [InlineData("\"ABC\" < \"ABD\"", true)]
    public void OrderingBreaksAnEqualPrefixByWidth(string expression, bool expected) =>
        Value(expression).Logical.Should().Be(expected);

    [Theory]
    // And the inclusive forms do not, because they are the strict kernels run on their own node,
    // where the reference never set the tie-break flag. So a wider string is both greater than its
    // own prefix and not greater than it, depending on which operator asks.
    [InlineData("\"AAAA\" >= \"AAA\"", true)]
    [InlineData("\"AAAA\" <= \"AAA\"", true)]
    [InlineData("\"AAA\" >= \"AAAA\"", true)]
    [InlineData("\"AAA\" <= \"AAAA\"", true)]
    [InlineData("\"ABD\" >= \"ABC\"", true)]
    [InlineData("\"ABC\" >= \"ABD\"", false)]
    [InlineData("\"ABC\" =< \"ABD\"", true)]
    [InlineData("\"ABD\" => \"ABC\"", true)]
    public void TheInclusiveFormsIgnoreWidthEntirely(string expression, bool expected) =>
        Value(expression).Logical.Should().Be(expected);

    [Theory]
    [InlineData("2 > 1", true)]
    [InlineData("1 > 2", false)]
    [InlineData("1 >= 1", true)]
    [InlineData("1 <= 1", true)]
    [InlineData("1 = 1", true)]
    [InlineData("1 <> 2", true)]
    [InlineData(".T. = .T.", true)]
    [InlineData(".T. <> .F.", true)]
    [InlineData(".T. = .F.", false)]
    [InlineData("CTOD(\"01/15/00\") > CTOD(\"01/14/00\")", true)]
    [InlineData("DATETIME(2026,1,1) > DATETIME(2020,1,1)", true)]
    [InlineData("DATETIME(2020,1,1) = DATETIME(2020,1,1)", true)]
    public void ComparingTheOtherTypesComparesTheirValues(string expression, bool expected) =>
        Value(expression).Logical.Should().Be(expected);

    [Theory]
    [InlineData("\"AB\" $ \"XABY\"", true)]
    [InlineData("\"Z\" $ \"XABY\"", false)]
    [InlineData("\"\" $ \"XABY\"", true)]
    [InlineData("\"XABY\" $ \"AB\"", false)]
    [InlineData("\"XABY\" $ \"XABY\"", true)]
    public void ContainmentLooksForTheLeftStringAnywhereInTheRight(string expression, bool expected) =>
        Value(expression).Logical.Should().Be(expected);

    [Fact]
    public void ContainmentTakesTheNeedleOnlyAsFarAsATrimLeftIt()
    {
        // The one place the shortened reach of a trimmed value is read instead of its width. Without
        // it the needle would carry its zero padding and match nothing.
        Value("TRIM(\"AB  \") $ \"XABY\"").Logical.Should().BeTrue();
    }

    // --------------------------------------------------------------- logical

    [Theory]
    [InlineData(".T. .AND. .F.", false)]
    [InlineData(".T. .AND. .T.", true)]
    [InlineData(".T. .OR. .F.", true)]
    [InlineData(".F. .OR. .F.", false)]
    [InlineData(".NOT. .T.", false)]
    [InlineData("! .F.", true)]
    [InlineData(".NOT. 1 = 2", true)]
    [InlineData(".T. .AND. .T. .AND. .F.", false)]
    [InlineData(".F. .OR. .F. .OR. .T.", true)]
    public void TheLogicalOperatorsIncludingTheirVariadicForms(string expression, bool expected) =>
        Value(expression).Logical.Should().Be(expected);

    [Theory]
    [InlineData("IIF(.T.,\"YES\",\"NO\")", "YES")]
    [InlineData("IIF(.F.,\"YES\",\"NO\")", "NO\0")]
    [InlineData("IIF(.F.,\"YES\",\"LONGER\")", "LONGER")]
    [InlineData("IIF(.T.,\"YES\",\"LONGER\")", "YES\0\0\0")]
    public void ChoosingBetweenTwoStringsLaysTheChoiceIntoTheWiderWidth(
        string expression,
        string expected) => Text(expression).Should().Be(expected);

    [Theory]
    [InlineData("IIF(.T.,1,2)", 1)]
    [InlineData("IIF(.F.,1,2)", 2)]
    public void ChoosingBetweenTwoNumbers(string expression, double expected) =>
        Value(expression).Number.Should().Be(expected);

    // ----------------------------------------------------------------- empty

    [Theory]
    // A string is empty when nothing in it is other than a blank, so an all-blank string is empty.
    [InlineData("EMPTY(\"\")", true)]
    [InlineData("EMPTY(\"   \")", true)]
    [InlineData("EMPTY(\"x\")", false)]
    [InlineData("EMPTY(\" x \")", false)]
    // A number is empty at zero, a julian date at zero, and a truth value when it is false.
    [InlineData("EMPTY(0)", true)]
    [InlineData("EMPTY(1)", false)]
    [InlineData("EMPTY(CTOD(\"01/15/00\"))", false)]
    [InlineData("EMPTY(CTOD(\"99/99/9999\"))", false)]
    [InlineData("EMPTY(.F.)", true)]
    [InlineData("EMPTY(.T.)", false)]
    public void EmptinessIsAskedDifferentlyOfEachType(string expression, bool expected) =>
        Value(expression).Logical.Should().Be(expected);

    // ----------------------------------------------------------------- dates

    [Theory]
    // Reading a date is picture-driven, so text past the picture is discarded rather than parsed.
    [InlineData("DTOS(CTOD(\"01/15/00\"))", "20000115")]
    [InlineData("DTOS(CTOD(\"12/31/99\"))", "20991231")]
    [InlineData("DTOS(CTOD(\"01/15/2000\"))", "20200115")]
    // Reading it from the sortable form takes a full year and has no picture at all.
    [InlineData("DTOS(STOD(\"20000115\"))", "20000115")]
    [InlineData("DTOS(STOD(\"19700115\"))", "19700115")]
    [InlineData("DTOS(STOD(\"20240229\"))", "20240229")]
    // An unreadable date is minus one going in and eight blanks coming out.
    [InlineData("DTOS(CTOD(\"99/99/9999\"))", "        ")]
    public void ADateGoesRoundThroughItsJulianDayUnchanged(string expression, string expected) =>
        Text(expression).Should().Be(expected);

    [Fact]
    public void AnUnreadableDateIsMinusOneAndNotZero()
    {
        // The two are distinct: zero is a blank date and minus one is text that is not a date. They
        // render the same and they do not compare the same.
        Value("CTOD(\"99/99/9999\")").Number.Should().Be(-1);
        Value("STOD(\"        \")").Number.Should().Be(0);
    }

    [Theory]
    [InlineData("DAY(CTOD(\"01/15/00\"))", 15)]
    [InlineData("MONTH(CTOD(\"01/15/00\"))", 1)]
    [InlineData("YEAR(CTOD(\"01/15/00\"))", 2000)]
    [InlineData("DAY(STOD(\"19701231\"))", 31)]
    [InlineData("MONTH(STOD(\"19701231\"))", 12)]
    [InlineData("YEAR(STOD(\"19701231\"))", 1970)]
    // A blank date answers zero for all three rather than failing.
    [InlineData("DAY(STOD(\"        \"))", 0)]
    [InlineData("MONTH(STOD(\"        \"))", 0)]
    [InlineData("YEAR(STOD(\"        \"))", 0)]
    public void ReadingAFieldOutOfADate(string expression, double expected) =>
        Value(expression).Number.Should().Be(expected);

    [Theory]
    [InlineData("STOD(\"20000115\") + 1", "20000116")]
    [InlineData("STOD(\"20000115\") - 1", "20000114")]
    [InlineData("1 + STOD(\"20000115\")", "20000116")]
    [InlineData("STOD(\"20000228\") + 1", "20000229")]
    // A blank date stays blank: the reference short-circuits rather than treating zero as a day.
    [InlineData("STOD(\"        \") + 1", "        ")]
    [InlineData("STOD(\"        \") - 1", "        ")]
    public void DateArithmeticShortCircuitsOnABlankDate(string expression, string expected) =>
        Text($"DTOS({expression})").Should().Be(expected);

    [Fact]
    public void SubtractingTwoDatesCountsTheDaysBetweenThem()
    {
        Value("STOD(\"20000115\") - STOD(\"20000114\")").Number.Should().Be(1);
        Value("STOD(\"20000101\") - STOD(\"19991231\")").Number.Should().Be(1);
        Value("STOD(\"20000115\") - STOD(\"20000115\")").Number.Should().Be(0);
    }

    [Fact]
    public void ATwoDigitYearIsRefusedWhenNoCenturyIsConfigured()
    {
        // ADR-42's whole point. The reference takes the century off the system clock, so its answer
        // for a two-digit year is only true in the century it ran in; this engine will not guess.
        ExpressionValue value = Evaluate("CTOD(\"01/15/00\")", Context(ExpressionOptions.CodeBase));

        value.Number.Should().Be(-1);
    }

    [Fact]
    public void ABuiltTimestampCarriesItsDateAndItsFoldedTimeOfDay()
    {
        ExpressionValue value = Value("DATETIME(2026,1,2,3,4,5)");

        value.TimestampDay.Should().Be((int)FoxDate.ToJulian(2026, 1, 2));
        value.TimestampTime.Should().Be((3 * 3600000) + (4 * 60000) + 5000);
    }

    // ---------------------------------------------------------------- others

    [Theory]
    // Reading a number out of text takes the leading numeric run and stops, so it neither fails nor
    // answers zero for text that merely starts with digits.
    [InlineData("VAL(\"123.45\")", 123.45)]
    [InlineData("VAL(\"01/15/2000\")", 1)]
    [InlineData("VAL(\"99/99/9999\")", 99)]
    [InlineData("VAL(\"not a number\")", 0)]
    [InlineData("VAL(\"\")", 0)]
    [InlineData("VAL(\"  leading blanks\")", 0)]
    [InlineData("VAL(\"-5\")", -5)]
    public void ReadingANumberOutOfTextIsAPrefixReadAndNotAParse(string expression, double expected) =>
        Value(expression).Number.Should().Be(expected);

    [Theory]
    [InlineData("L2BIN(1)", new byte[] { 1, 0, 0, 0 })]
    [InlineData("L2BIN(0)", new byte[] { 0, 0, 0, 0 })]
    [InlineData("L2BIN(-1)", new byte[] { 255, 255, 255, 255 })]
    [InlineData("L2BIN(65536)", new byte[] { 0, 0, 1, 0 })]
    public void ANumberAsFourBinaryBytesIsLittleEndian(string expression, byte[] expected) =>
        Bytes(expression).Should().Equal(expected);

    [Theory]
    [InlineData("KEYEQUAL(\"ABC\",\"ABC\")", true)]
    [InlineData("KEYEQUAL(\"ABC\",\"ABD\")", false)]
    [InlineData("KEYEQUAL(\"ABC\",\"AB\")", true)]
    public void KeyEqualityWithoutATagIsAPrefixComparison(string expression, bool expected) =>
        Value(expression).Logical.Should().Be(expected);

    [Fact]
    public void TheRecordFunctionsAnswerFromTheContextAndNotFromTheRecord()
    {
        EvaluationContext context = new()
        {
            Record = Record(),
            RecordNumber = 7,
            RecordCount = 42,
            Deleted = true,
        };

        Evaluate("RECNO()", context).Number.Should().Be(7);
        Evaluate("RECCOUNT()", context).Number.Should().Be(42);
        Evaluate("DELETED()", context).Logical.Should().BeTrue();
        Encoding.Latin1.GetString(Evaluate("DEL()", context).Bytes).Should().Be("*");
    }

    [Fact]
    public void TheClockFunctionsAnswerFromTheContextToo()
    {
        // Supplied rather than read, for the same reason as the century: a value off the host's clock
        // is a value that differs between two runs of one query.
        EvaluationContext context = new() { Today = 2451559, TimeOfDay = (13 * 3600000) + (5 * 60000) + 9000 };

        Evaluate("DATE()", context).Number.Should().Be(2451559);
        Encoding.Latin1.GetString(Evaluate("TIME()", context).Bytes).Should().Be("13:05:09");
    }

    // ------------------------------------------------------------------ null

    [Fact]
    public void AbsenceInAnArithmeticOperandDiscardsTheArithmetic()
    {
        // Either side absent makes the result absent, and its value zero rather than what the
        // operation would have produced.
        ExpressionValue value = Evaluate("K_NNUM + 1", Context(record: Record(nullBits: 0x01)));

        value.IsNull.Should().BeTrue();
        value.Number.Should().Be(0);
    }

    [Fact]
    public void AbsenceInOneComparisonOperandOnlyMakesTheAnswerAbsentAndFalse()
    {
        ExpressionValue absent = Evaluate("K_NNUM = 0", Context(record: Record(nullBits: 0x01)));
        absent.IsNull.Should().BeTrue();
        absent.Logical.Should().BeFalse();

        // And true for inequality, which is the one place the forced answer is not false.
        Evaluate("K_NNUM <> 0", Context(record: Record(nullBits: 0x01))).Logical.Should().BeTrue();
    }

    [Fact]
    public void TheNegationOfAnAbsentAnswerIsAbsentAndFalse()
    {
        // .NOT. does not negate an absent operand: it answers false and stays absent
        // (e4functi.c:2247-2261). Negating the comparison's forced false would put every null row
        // into a filter that only meant to exclude the zeros.
        ExpressionValue value = Evaluate(".NOT.(K_NNUM = 0)", Context(record: Record(nullBits: 0x01)));

        value.IsNull.Should().BeTrue();
        value.Logical.Should().BeFalse();
    }

    [Fact]
    public void TheNegationOfAPresentAnswerIsItsOpposite()
    {
        ExpressionValue value = Evaluate(".NOT.(K_NNUM = 0)", Context(record: Record()));

        value.IsNull.Should().BeFalse();
        value.Logical.Should().BeFalse();
        Evaluate(".NOT.(K_NNUM = 1)", Context(record: Record())).Logical.Should().BeTrue();
    }

    [Fact]
    public void APresentValueInANullableFieldComparesAsUsual()
    {
        ExpressionValue value = Evaluate("K_NNUM = 0", Context(record: Record()));

        value.IsNull.Should().BeFalse();
        value.Logical.Should().BeTrue();
    }

    // ------------------------------------------------- what the optimizer needs

    [Fact]
    public void ASubexpressionCanBeEvaluatedOnItsOwnByTheIndexOfItsRoot()
    {
        // What decides whether a tag can stand in for a subexpression is what that subexpression's
        // constant side is worth, so the evaluator answers at a node and not only at the root.
        CompiledExpression compiled = Compile("K_TEXT = \"SM\"");
        ExpressionEvaluator evaluator = new(compiled);

        int constant = compiled.Nodes.Count - 2;
        Encoding.Latin1.GetString(evaluator.Evaluate(constant, Context()).Bytes).Should().Be("SM");
    }

    [Fact]
    public void AConstantSubexpressionNeedsNoRecordAtAll()
    {
        CompiledExpression compiled = Compile("K_TEXT = \"SM\"");
        ExpressionEvaluator evaluator = new(compiled);

        ExpressionValue value = evaluator.Evaluate(compiled.Nodes.Count - 2, new EvaluationContext());

        Encoding.Latin1.GetString(value.Bytes).Should().Be("SM");
    }

    [Fact]
    public void AConstantsDeclaredLengthIsItsOwnAndNotTheKeysItIsComparedAgainst()
    {
        // Two characters against a twenty-character field, and that difference is what turns a
        // comparison into a range over a tag rather than a wrong equality.
        CompiledExpression compiled = Compile("K_TEXT = \"SM\"");

        compiled.Nodes[^2].Length.Should().Be(2);
        compiled.Nodes[0].Length.Should().Be(20);
    }

    [Theory]
    [InlineData("\"SM\"", 0, true)]
    [InlineData("1", 0, true)]
    [InlineData(".T.", 0, true)]
    [InlineData(".FALSE.", 0, true)]
    [InlineData("CTOD(\"01/15/00\")", 1, true)]
    [InlineData("STOD(\"20000115\")", 1, true)]
    [InlineData("K_TEXT", 0, false)]
    [InlineData("UPPER(\"AB\")", 1, false)]
    [InlineData("1+1", 2, false)]
    public void WhetherASubtreeNeedsARecordIsAnsweredFromTheRowsAlone(
        string expression,
        int root,
        bool constant)
    {
        // The reference's own predicate, transcribed including what it leaves out: a literal answers
        // from its row, the two functions that read a date out of text answer by walking their
        // subtree for a field load, and everything else answers no — even an expression over
        // literals whose value plainly cannot change.
        new ExpressionEvaluator(Compile(expression)).IsConstant(root).Should().Be(constant);
    }

    [Fact]
    public void ANarrowedFloatLiteralIsNotCountedConstantThoughItIsOne()
    {
        // The reference's predicate has no case for it, so a comparison against a binary float field
        // is predicted never optimizable. Narrowing that here would be this port disagreeing with
        // the reference about which queries can be optimized, which is worse than the missed
        // optimization.
        CompiledExpression compiled = Compile("K_FLT > 0");

        compiled.Nodes[1].Row.Should().Be(FunctionRows.FloatConstant);
        new ExpressionEvaluator(compiled).IsConstant(1).Should().BeFalse();
    }

    // ----------------------------------------------------------------- plumbing

    private static readonly IFieldMetadata WithFloat = new TableFieldMetadata(
        [Field("K_FLT", 'H', 4)], "T");

    // ------------------------------------------------- conversions under a tag

    [Fact]
    public void UnderACollatedTagAStringsAscendingConversionIsItsCollatedKey()
    {
        // The operand is weighed with the tag's collation at evaluation time (e4functi.c:3042-3062),
        // into the same heads-and-tails form a collated tag stores for the plain string.
        byte[] text = Encoding.Latin1.GetBytes("cafe");
        byte[] expected = new byte[2 * text.Length];
        CollatedKey.Write(
            text,
            CollationWeights.TableFor(CollationName.General, 1252),
            CollationWeights.ExpansionsFor(CollationName.General, 1252),
            includeTails: true,
            expected);

        UnderTag("ASCEND(\"cafe\")", CollationName.General).Should().Equal(expected);
        UnderTag("DESCEND(\"cafe\")", CollationName.General).Should().Equal(expected.Select(b => (byte)~b));
    }

    [Fact]
    public void UnderAMachineTagAStringsConversionIsAsWithoutATag()
    {
        UnderTag("ASCEND(\"cafe\")", CollationName.Machine).Should().Equal(Bytes("ASCEND(\"cafe\")"));
        UnderTag("DESCEND(\"cafe\")", CollationName.Machine).Should().Equal(Bytes("DESCEND(\"cafe\")"));
    }

    private static byte[] UnderTag(string expression, CollationName collation) =>
        new ExpressionEvaluator(TypeResolver.Resolve(
                ExpressionParser.Parse(expression, Fields), TagContext.For(collation, 1252)))
            .Evaluate(Context()).Bytes.ToArray();

    private static CompiledExpression Compile(string expression) =>
        TypeResolver.Resolve(ExpressionParser.Parse(
            expression,
            expression.Contains("K_FLT", StringComparison.Ordinal) ? WithFloat : Fields));

    private static ExpressionValue Evaluate(string expression, EvaluationContext context) =>
        new ExpressionEvaluator(Compile(expression)).Evaluate(context);

    private static ExpressionValue Value(string expression) => Evaluate(expression, Context());

    private static string Text(string expression) => Encoding.Latin1.GetString(Value(expression).Bytes);

    private static byte[] Bytes(string expression) => Value(expression).Bytes.ToArray();

    /// <summary>
    /// Builds the settings the reference's own default build ran under, plus its century.
    /// </summary>
    private static EvaluationContext Context(
        ExpressionOptions? options = null,
        RecordBuffer? record = null) => new()
    {
        Record = record,
        NullFlags = record is null ? null : Field("_NullFlags", '0', 1),
        Options = options ?? ExpressionOptions.CodeBase.WithCentury(20),
    };

    /// <summary>
    /// Builds a record with a numeric field of zero and a null bitmap.
    /// </summary>
    private static RecordBuffer Record(byte nullBits = 0)
    {
        RecordBuffer record = new(1 + 12 + 1);
        Span<byte> raw = record.Raw;

        raw[0] = (byte)' ';
        Encoding.Latin1.GetBytes("       0.000").CopyTo(raw[1..]);
        raw[13] = nullBits;

        return record;
    }

    private static FieldDefinition Field(
        string name,
        char type,
        int length,
        int decimals = 0,
        int? nullBit = null) =>
        new(name, type, type, length, decimals, name == "_NullFlags" ? 13 : 1, false, false, false, false, nullBit);
}
