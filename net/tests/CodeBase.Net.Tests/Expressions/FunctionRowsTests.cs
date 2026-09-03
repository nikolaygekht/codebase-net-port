using AwesomeAssertions;
using CodeBase.Net.Expressions;
using Xunit;

namespace CodeBase.Net.Tests.Expressions;

/// <summary>
/// The named row indexes against the table they index into.
///
/// The reference implementation derives every one of these by addition from its neighbour and puts
/// compile-time assertions at four places to catch a row inserted in the wrong band. The port writes
/// the absolute numbers instead, which is clearer and gives up that protection, so this is what
/// replaces it: each constant has to land on the row whose name it claims. A row inserted or removed
/// anywhere below one of these moves it, and this says so by name.
/// </summary>
public sealed class FunctionRowsTests
{
    [Theory]
    [InlineData(FunctionRows.Str, "STR")]
    [InlineData(FunctionRows.StrZero, "STRZERO")]
    [InlineData(FunctionRows.SubStr, "SUBSTR")]
    [InlineData(FunctionRows.DateToSortableText, "DTOS")]
    [InlineData(FunctionRows.DateToText, "DTOC")]
    [InlineData(FunctionRows.TimestampToText, "TTOC")]
    [InlineData(FunctionRows.Space, "SPACE")]
    [InlineData(FunctionRows.Left, "LEFT")]
    [InlineData(FunctionRows.Right, "RIGHT")]
    [InlineData(FunctionRows.PadLeft, "PADL")]
    [InlineData(FunctionRows.PadRight, "PADR")]
    [InlineData(FunctionRows.Timestamp, "DATETIME")]
    public void EachNamedRowIsTheRowItClaims(int row, string name) =>
        FunctionTable.Row(row).Name.Should().Be(name);

    [Theory]
    [InlineData(FunctionRows.FieldCharacter, 'C')]
    [InlineData(FunctionRows.FieldLogical, 'L')]
    [InlineData(FunctionRows.FieldDateAsText, 'D')]
    [InlineData(FunctionRows.FieldNumericAsText, 'N')]
    [InlineData(FunctionRows.FieldCurrency, 'Y')]
    [InlineData(FunctionRows.FieldDouble, 'n')]
    [InlineData(FunctionRows.FieldInteger, 'I')]
    [InlineData(FunctionRows.FieldTimestamp, 'T')]
    [InlineData(FunctionRows.FieldBinaryFloat, 'H')]
    [InlineData(FunctionRows.FieldMemo, 'C')]
    [InlineData(FunctionRows.NumericConstant, 'n')]
    [InlineData(FunctionRows.StringConstant, 'C')]
    public void EachLoaderRowReturnsTheTypeItIsNamedFor(int row, char type)
    {
        // The loaders have no names, so what identifies them is what they return and that they take
        // nothing. It is weaker than a name and it still moves if a row is inserted above them.
        FunctionTable.Row(row).Name.Should().BeNull();
        FunctionTable.Row(row).ParameterCount.Should().Be(0);
        ((char)FunctionTable.Row(row).ReturnType).Should().Be(type);
    }
}
