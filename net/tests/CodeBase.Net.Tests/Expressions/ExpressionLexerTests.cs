using AwesomeAssertions;
using CodeBase.Net.Expressions;
using Xunit;

namespace CodeBase.Net.Tests.Expressions;

/// <summary>
/// The scanner, expression by expression, as a table of token streams.
///
/// Layer one. What this catches that nothing else can: the scanner's decisions are all local, they
/// are all cheap to state as text, and almost every one of them is a rule a sensible scanner gets
/// wrong precisely by being sensible. A dot ends a number here. A minus is part of a literal and is
/// nothing else. Three string delimiters and no escapes. Blanks mean three different things in three
/// places. None of that survives being inferred from a parse tree two passes later, and the corpus
/// cannot reach most of it, because every expression a corpus file holds is one the reference already
/// accepted.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class ExpressionLexerTests
{
    [Theory]

    // Values, one at a time.
    [InlineData("K_TEXT", "field:K_TEXT")]
    [InlineData("'abc'", "str:abc")]
    [InlineData("\"abc\"", "str:abc")]
    [InlineData("[abc]", "str:abc")]
    [InlineData("''", "str:")]
    [InlineData("[a\"b']", "str:a\"b'")]
    [InlineData(".T.", "log:.T.")]
    [InlineData(".TRUE.", "log:.TRUE.")]
    [InlineData(".F.", "log:.F.")]
    [InlineData(".FALSE.", "log:.FALSE.")]
    [InlineData(".t.", "log:.t.")]

    // Numeric literals, including the two disambiguations that make them interesting.
    [InlineData("1", "num:1")]
    [InlineData("1.5", "num:1.5")]
    [InlineData(".5", "num:.5")]
    [InlineData("-1", "num:-1")]
    [InlineData("+1", "num:+1")]
    [InlineData("-1.5", "num:-1.5")]
    [InlineData("1.AND.X", "num:1 op:.AND. field:X")]
    [InlineData("1.OR.X", "num:1 op:.OR. field:X")]
    [InlineData("2.5", "num:2.5")]

    // A leading sign belongs to a literal and to nothing else, so the second of these is a
    // subtraction and not a negation of a literal.
    [InlineData("K_NUM-1", "field:K_NUM op:- num:1")]
    [InlineData("K_NUM -1", "field:K_NUM op:- num:1")]
    [InlineData("1 - 2", "num:1 op:- num:2")]

    // Prefix negation, in both spellings, and the collision the dotted operators create.
    [InlineData(".NOT. L1", "not:.NOT. field:L1")]
    [InlineData("!L1", "not:! field:L1")]
    [InlineData("L1.AND.L2", "field:L1 op:.AND. field:L2")]
    [InlineData(".NOT.L1.AND.L2", "not:.NOT. field:L1 op:.AND. field:L2")]
    [InlineData("A.OR.B", "field:A op:.OR. field:B")]
    [InlineData("a.and.b", "field:a op:.and. field:b")]

    // Every operator, so that the row order the table is transcribed in is what decides which of two
    // spellings that share a first character wins.
    [InlineData("A=B", "field:A op:= field:B")]
    [InlineData("A<>B", "field:A op:<> field:B")]
    [InlineData("A#B", "field:A op:# field:B")]
    [InlineData("A>=B", "field:A op:>= field:B")]
    [InlineData("A=>B", "field:A op:=> field:B")]
    [InlineData("A<=B", "field:A op:<= field:B")]
    [InlineData("A=<B", "field:A op:=< field:B")]
    [InlineData("A>B", "field:A op:> field:B")]
    [InlineData("A<B", "field:A op:< field:B")]
    [InlineData("A$B", "field:A op:$ field:B")]
    [InlineData("A**B", "field:A op:** field:B")]
    [InlineData("A^B", "field:A op:^ field:B")]
    [InlineData("A*B", "field:A op:* field:B")]
    [InlineData("A/B", "field:A op:/ field:B")]
    [InlineData("A+B", "field:A op:+ field:B")]
    [InlineData("A-B", "field:A op:- field:B")]

    // Calls, their parameters, and the empty parameter list.
    [InlineData("UPPER(K_TEXT)", "fn:UPPER ( field:K_TEXT )")]
    [InlineData("upper(K_TEXT)", "fn:upper ( field:K_TEXT )")]
    [InlineData("UPPER (K_TEXT)", "fn:UPPER ( field:K_TEXT )")]
    [InlineData("DATE()", "fn:DATE ( )")]
    [InlineData("RECNO()", "fn:RECNO ( )")]
    [InlineData("UPPER(TRIM(K_TEXT))", "fn:UPPER ( fn:TRIM ( field:K_TEXT ) )")]
    [InlineData("STR(K_NUM,12,3)", "fn:STR ( field:K_NUM , num:12 , num:3 )")]
    [InlineData("SUBSTR(\"ABCDEF\", 2, 3)", "fn:SUBSTR ( str:ABCDEF , num:2 , num:3 )")]
    [InlineData("IIF(L,'a','b')", "fn:IIF ( field:L , str:a , str:b )")]

    // Grouping, and a whole filter of the shape a tag carries.
    [InlineData("(A+B)*C", "( field:A op:+ field:B ) op:* field:C")]
    [InlineData(
        "UPPER(K_TEXT) = \"SM\" .AND. K_NUM > 0",
        "fn:UPPER ( field:K_TEXT ) op:= str:SM op:.AND. field:K_NUM op:> num:0")]
    [InlineData("K_TEXT-K_CITY", "field:K_TEXT op:- field:K_CITY")]
    [InlineData("DESCEND(K_TEXT)", "fn:DESCEND ( field:K_TEXT )")]

    // A name of exactly ten characters is a field; eleven is refused, and that is a case of its own
    // below.
    [InlineData("ABCDEFGHIJ", "field:ABCDEFGHIJ")]

    // A qualifier over a name that is no open table is not a qualifier. The reference steps over the
    // minus sign whether or not it found a table, which leaves the rest reading as a comparison.
    [InlineData("OTHER->K_TEXT", "field:OTHER op:> field:K_TEXT")]

    // A name that begins with digits is a name, not a malformed number.
    [InlineData("A12", "field:A12")]
    [InlineData("_A", "field:_A")]
    public void TokensAreWhatTheReferenceWouldHaveRead(string expression, string expected)
    {
        Render(ExpressionLexer.Tokenize(expression)).Should().Be(expected);
    }

    [Fact]
    public void AQualifiedNameIsAFieldOfThatTableWhenTheTableIsOpen()
    {
        // Both spellings of a qualifier. The dotted one is the reason the scanner has to be told
        // which names are tables at all: without that, it cannot tell a qualified field from a field
        // followed by a logical operator.
        Render(ExpressionLexer.Tokenize("MY->NAME", ["MY"])).Should().Be("field:MY->NAME");
        Render(ExpressionLexer.Tokenize("MY.NAME", ["MY"])).Should().Be("field:MY->NAME");
        Render(ExpressionLexer.Tokenize("my.NAME", ["MY"])).Should().Be("field:MY->NAME");
    }

    [Fact]
    public void ADottedNameThatIsNotATableIsAFieldAndThenNoOperatorAtAll()
    {
        // The same text as above with no table of that name: the field is read, and the dot is then
        // asked to be an operator, which it is not.
        Refusal("K_TEXT.K_CITY").Should().Be(ErrorCode.UnrecognizedOperator);
    }

    [Fact]
    public void ALongFieldNameIsRefusedUnlessTheTableSupportsOne()
    {
        Refusal("NOSUCHFIELD").Should().Be(ErrorCode.UnrecognizedValue);

        Render(ExpressionLexer.Tokenize("NOSUCHFIELD", longFieldNames: true))
            .Should().Be("field:NOSUCHFIELD");
    }

    [Theory]

    // There is no negation of anything but a literal, so this writes a tag no engine could read and
    // is refused rather than quietly accepted.
    [InlineData("-K_TEXT", ErrorCode.UnrecognizedValue)]
    [InlineData("-UPPER(K_TEXT)", ErrorCode.UnrecognizedValue)]

    // There are no brace literals; a date comes from a function.
    [InlineData("{01/01/2000}", ErrorCode.UnrecognizedValue)]

    // A string runs to its matching delimiter and there is no escape to extend it.
    [InlineData("'unterminated", ErrorCode.UnterminatedString)]
    [InlineData("[unterminated", ErrorCode.UnterminatedString)]

    // A value that never arrives.
    [InlineData("", ErrorCode.UnrecognizedValue)]
    [InlineData("   ", ErrorCode.UnrecognizedValue)]
    [InlineData("K_TEXT +", ErrorCode.UnrecognizedValue)]
    [InlineData("K_TEXT + ", ErrorCode.UnrecognizedValue)]

    // Text that begins no value.
    [InlineData("K_TEXT + )", ErrorCode.UnrecognizedValue)]
    [InlineData("@", ErrorCode.UnrecognizedValue)]

    // Text that is no operator.
    [InlineData("A B", ErrorCode.UnrecognizedOperator)]
    [InlineData("A ~ B", ErrorCode.UnrecognizedOperator)]

    // A parameter list that ends with the expression is a different refusal from a value that never
    // arrives, and the reference distinguishes them.
    [InlineData("UPPER(", ErrorCode.RightParenthesisMissing)]
    public void TheScannerRefusesWhatTheReferenceRefuses(string expression, ErrorCode expected)
    {
        Refusal(expression).Should().Be(expected);
    }

    [Fact]
    public void BlanksMeanThreeDifferentThingsInThreeDifferentPlaces()
    {
        // Where an operator is expected, and between a name and its parenthesis, every control
        // character is a blank. Where a value is expected only the space is, so a tab there begins
        // nothing (e4parse.c:2531 against :1187 and :2506).
        Render(ExpressionLexer.Tokenize("A\t+ B")).Should().Be("field:A op:+ field:B");
        Render(ExpressionLexer.Tokenize("UPPER\t(A)")).Should().Be("fn:UPPER ( field:A )");

        Refusal("A +\tB").Should().Be(ErrorCode.UnrecognizedValue);
    }

    [Fact]
    public void AnEmptyParameterListIsOnlyEmptyWhenNothingAtAllIsInIt()
    {
        // The reference looks for the closing parenthesis before it skips anything, so a blank
        // inside the brackets is a parameter that never arrives (e4parse.c:1871-1890).
        Render(ExpressionLexer.Tokenize("DATE()")).Should().Be("fn:DATE ( )");

        Refusal("DATE( )").Should().Be(ErrorCode.UnrecognizedValue);
    }

    [Fact]
    public void TheOperatorBandRunsTwoRowsPastTheLastOperator()
    {
        // Not a language anyone writes, and pinned all the same: the reference's operator lookup ends
        // two rows into the named functions, so those two names scan as operators where an operator
        // is expected. It is here because the constant that says so is load-bearing, and because a
        // narrower band would make this port refuse text the reference accepts.
        Render(ExpressionLexer.Tokenize("1 CHR 2")).Should().Be("num:1 op:CHR num:2");
        Render(ExpressionLexer.Tokenize("1 DEL 2")).Should().Be("num:1 op:DEL num:2");

        Refusal("1 STR 2").Should().Be(ErrorCode.UnrecognizedOperator);
    }

    [Fact]
    public void AnOperatorCarriesTheTableRowItWasRecognizedBy()
    {
        // The row is not decoration. It is what the parser reads binding power from, and it is known
        // here because the scanner recognized the operator by matching the table rather than by a
        // list of its own.
        IReadOnlyList<ExpressionToken> tokens = ExpressionLexer.Tokenize("A>=B");

        tokens[1].Row.Should().Be(60);
        FunctionTable.Row(tokens[1].Row).Priority.Should().Be(6);
    }

    /// <summary>
    /// Runs the scanner and returns the code it refused with.
    /// </summary>
    private static ErrorCode Refusal(string expression)
    {
        Action scan = () => ExpressionLexer.Tokenize(expression);
        return scan.Should().Throw<CodeBaseException>().Which.Code;
    }

    /// <summary>
    /// Writes a token stream as text, so that a case is one readable line.
    /// </summary>
    private static string Render(IReadOnlyList<ExpressionToken> tokens) =>
        string.Join(' ', tokens.Select(Render));

    private static string Render(ExpressionToken token) => token.Kind switch
    {
        ExpressionTokenKind.Number => $"num:{token.Text}",
        ExpressionTokenKind.String => $"str:{token.Text}",
        ExpressionTokenKind.LogicalConstant => $"log:{token.Text}",
        ExpressionTokenKind.PrefixNot => $"not:{token.Text}",
        ExpressionTokenKind.Operator => $"op:{token.Text}",
        ExpressionTokenKind.FunctionName => $"fn:{token.Text}",
        ExpressionTokenKind.FieldName =>
            token.Alias is null ? $"field:{token.Text}" : $"field:{token.Alias}->{token.Text}",
        ExpressionTokenKind.LeftParenthesis => "(",
        ExpressionTokenKind.RightParenthesis => ")",
        ExpressionTokenKind.Comma => ",",
        _ => throw new InvalidOperationException($"Unknown token kind {token.Kind}."),
    };
}
