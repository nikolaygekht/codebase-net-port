using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Dbf;
using Xunit;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// The one accessor every decoder reads through, and the containment it is there to guarantee.
///
/// Step 001 proved that resolved fields always add up to the record. This is the other half: that
/// nothing can read past the record even if a field says it should. The two together are what let
/// every decoder take a span without checking anything itself.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class RecordBufferTests
{
    [Fact]
    public void ANewBuffer_IsAsWideAsTheRecord()
    {
        new RecordBuffer(88).Length.Should().Be(88);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ARecordWidthThatIsNotPositive_IsRefused(int recordLength)
    {
        Action act = () => new RecordBuffer(recordLength);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Field_HandsOutExactlyTheFieldsBytes()
    {
        RecordBuffer buffer = BufferOf(" ALPHA     12345");
        FieldDefinition name = Field("NAME", 'C', length: 5, offset: 1);
        FieldDefinition digits = Field("DIGITS", 'C', length: 5, offset: 11);

        Encoding.ASCII.GetString(buffer.Field(name)).Should().Be("ALPHA");
        Encoding.ASCII.GetString(buffer.Field(digits)).Should().Be("12345");
    }

    [Fact]
    public void Slice_MayReachIntoTheFollowingFieldBecauseTheCLibraryDoes()
    {
        // Several fixed-width types read their natural width and ignore the declared length, so a
        // short descriptor makes the decoder read on into the next field. That stays inside the
        // record, so it is allowed. See Decision 10.
        RecordBuffer buffer = BufferOf(" ABCDEFGH");

        Encoding.ASCII.GetString(buffer.Slice(1, 8, "a short datetime")).Should().Be("ABCDEFGH");
    }

    [Theory]
    [InlineData(1, 9)]
    [InlineData(9, 1)]
    [InlineData(-1, 2)]
    [InlineData(0, -1)]
    [InlineData(int.MaxValue, 1)]
    public void Slice_ThatWouldLeaveTheRecord_IsRefused(int offset, int length)
    {
        // Refused rather than truncated: a short field decodes into a plausible wrong value, which
        // is the failure this library cares most about.
        RecordBuffer buffer = BufferOf(" ABCDEFGH");

        Action act = () => buffer.Slice(offset, length, "a field");

        act.Should().Throw<CodeBaseException>().WithMessage("*does not lie inside its record*");
    }

    [Fact]
    public void Deleted_IsAnyByteOtherThanASpace()
    {
        // d4deleted compares against a space rather than against an asterisk, so a file with a
        // stray byte there reads as deleted. See Decision 12.
        BufferOf(" hello").Deleted.Should().BeFalse();
        BufferOf("*hello").Deleted.Should().BeTrue();
        BufferOf("Xhello").Deleted.Should().BeTrue();
        BufferOf("\0hello").Deleted.Should().BeTrue();
    }

    [Fact]
    public void Blank_ReplacesTheWholeRecord()
    {
        RecordBuffer buffer = BufferOf("*stale ");
        byte[] blank = Encoding.ASCII.GetBytes("       ");

        buffer.Blank(blank);

        buffer.Deleted.Should().BeFalse();
        Encoding.ASCII.GetString(buffer.Slice(0, buffer.Length, "all")).Should().Be("       ");
    }

    [Fact]
    public void Blank_WithARecordOfADifferentWidth_IsRefused()
    {
        RecordBuffer buffer = BufferOf(" abc");

        Action act = () => buffer.Blank(new byte[3]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Field_WhateverTheLayoutSays_EitherStaysInsideTheRecordOrIsRefused()
    {
        // The guarantee stated over layouts nobody chose. Seeded, so a failure reproduces. Half the
        // fields are placed to fit and half at random: an entirely random placement almost never
        // fits, and a run that was all refusals would assert nothing while reporting success. The
        // counts below are what say that did not happen.
        Random random = new(20260811);
        int inside = 0;
        int refused = 0;

        for (int iteration = 0; iteration < 2000; iteration++)
        {
            int recordLength = random.Next(1, 200);
            RecordBuffer buffer = new(recordLength);
            bool fitting = random.Next(2) == 0;

            int length = fitting ? random.Next(0, recordLength) : random.Next(0, 400);
            int offset = fitting
                ? random.Next(0, recordLength - length + 1)
                : random.Next(-10, 400);

            FieldDefinition field = Field("F", 'C', length, offset);

            try
            {
                ReadOnlySpan<byte> span = buffer.Field(field);

                span.Length.Should().Be(length);
                (offset >= 0 && offset + length <= recordLength).Should().BeTrue(
                    "a span was handed out for a field that does not lie inside the record");
                inside++;
            }
            catch (CodeBaseException)
            {
                (offset < 0 || offset + length > recordLength).Should().BeTrue(
                    "a field lying inside the record was refused");
                refused++;
            }
        }

        inside.Should().BeGreaterThan(500);
        refused.Should().BeGreaterThan(500);
    }

    private static RecordBuffer BufferOf(string contents)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(contents);
        RecordBuffer buffer = new(bytes.Length);
        bytes.CopyTo(buffer.Raw);
        return buffer;
    }

    /// <summary>
    /// Builds a field at an arbitrary offset, including offsets the resolver would never produce.
    /// </summary>
    private static FieldDefinition Field(string name, char type, int length, int offset) =>
        new(name, type, type, length, 0, offset, false, false, false, false, null);
}
