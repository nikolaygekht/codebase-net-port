using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Dbf;
using Xunit;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// What a record looks like when the cursor is not on one.
///
/// Ungated: no corpus dump shows a blank record, because the generator only ever walks real ones.
/// So the expectations here come from reading [c]f4blank[/c], and the step's summary names this as
/// something the corpus does not prove.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class BlankRecordTests
{
    [Fact]
    public void Build_FillsATextShapedFieldWithSpaces()
    {
        byte[] blank = BlankRecord.Build([Field("NAME", 'C', 5, 1), Field("QTY", 'N', 4, 6)], 10);

        Encoding.ASCII.GetString(blank).Should().Be("          ");
    }

    [Theory]
    [InlineData('I')]
    [InlineData('Y')]
    [InlineData('T')]
    [InlineData('B')]
    [InlineData('X')]
    [InlineData('Z')]
    public void Build_FillsAFixedBinaryFieldWithZeros(char type)
    {
        byte[] blank = BlankRecord.Build([Field("F", type, 4, 1)], 5);

        blank.Should().Equal((byte)' ', 0, 0, 0, 0);
    }

    [Theory]
    [InlineData('M')]
    [InlineData('G')]
    public void Build_FillsAFourByteMemoReferenceWithZeros(char type)
    {
        // A blank reference has to parse back as "no memo" in whichever encoding the field uses, and
        // the four-byte binary form reads zeros as none. Witnessed by the corpus: every empty
        // reference in the Visual FoxPro tables is four zero bytes.
        byte[] blank = BlankRecord.Build([Field("F", type, 4, 1)], 5);

        blank.Should().Equal((byte)' ', 0, 0, 0, 0);
    }

    [Theory]
    [InlineData('M')]
    [InlineData('G')]
    public void Build_FillsATenByteMemoReferenceWithSpaces(char type)
    {
        // The other encoding reads blanks as none, so the blank differs by width rather than by
        // type. Witnessed by F2XMEMO, whose empty references are ten spaces.
        byte[] blank = BlankRecord.Build([Field("F", type, 10, 1)], 11);

        blank.Should().AllBeEquivalentTo((byte)' ');
    }

    [Fact]
    public void Build_LeavesTheDeletionFlagClear()
    {
        byte[] blank = BlankRecord.Build([Field("F", 'I', 4, 1)], 5);

        blank[0].Should().Be((byte)' ');
    }

    [Fact]
    public void Build_FillsPaddingTheFieldsDoNotAccountFor()
    {
        // A record wider than its fields is a file that disagrees with itself. Whatever is left over
        // still has to be something, and spaces are what a fill produces.
        byte[] blank = BlankRecord.Build([Field("F", 'C', 2, 1)], 8);

        blank.Should().AllBeEquivalentTo((byte)' ');
    }

    private static FieldDefinition Field(string name, char type, int length, int offset) =>
        new(name, type, type, length, 0, offset, false, false, false, false, null);
}
