using AwesomeAssertions;
using CodeBase.Net.Dbf;
using Xunit;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// What resolving fields promises for input the corpus cannot hold, because every corpus file is valid.
///
/// The rules a caller can rely on are proved against real files by the golden layer. What is left
/// for here is refusal, tolerance, and the guarantee that holds whatever the bytes say.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class FieldResolverTests
{
    private static readonly IDbfFormatVariant VisualFoxPro = IDbfFormatVariant.Resolve(0x30);
    private static readonly IDbfFormatVariant Legacy = IDbfFormatVariant.Resolve(0x03);

    [Fact]
    public void Resolve_AccumulatesOffsetsAndIgnoresTheStoredOnes()
    {
        // Every stored offset here is a lie; the resolved ones still add up to the record.
        ResolvedFields resolved = Resolve(
            recordLength: 1 + 10 + 4 + 8,
            Descriptor("A", 'C', length: 10, storedOffset: 999),
            Descriptor("B", 'I', length: 4, storedOffset: -1),
            Descriptor("C", 'D', length: 8, storedOffset: 0));

        resolved.Fields.Select(f => f.RecordOffset).Should().Equal(1, 11, 15);
    }

    [Fact]
    public void Resolve_CombinesTheTwoLengthBytesOfACharacterField()
    {
        // A character field wider than 255 bytes keeps the high half of its length where other
        // types keep a decimal count.
        ResolvedFields resolved = Resolve(
            recordLength: 1 + 300,
            Descriptor("WIDE", 'C', length: 44, decimals: 1));

        resolved.Fields[0].Length.Should().Be(300);
        resolved.Fields[0].Decimals.Should().Be(0, "the byte was a length, not a decimal count");
    }

    [Fact]
    public void Resolve_UpperCasesNames()
    {
        Resolve(recordLength: 2, Descriptor("mixed", 'L', length: 1))
            .Fields[0].Name.Should().Be("MIXED");
    }

    [Fact]
    public void Resolve_TreatsALowerCaseTypeLetterAsItsType()
    {
        // The engine upper-cases the type when it opens a table, so this is a character field.
        Resolve(recordLength: 11, Descriptor("F", 'c', length: 10))
            .Fields[0].Type.Should().Be('C');
    }

    [Fact]
    public void Resolve_FieldsThatDoNotAccountForTheRecord_AreRejectedAsCorruptData()
    {
        Action act = () => Resolve(recordLength: 99, Descriptor("A", 'C', length: 10));

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Data);
    }

    [Theory]
    [InlineData('W')]
    [InlineData('O')]
    [InlineData('V')]
    [InlineData('P')]
    [InlineData('Q')]
    [InlineData('R')]
    [InlineData('1')]
    [InlineData('4')]
    [InlineData('7')]
    [InlineData('~')]
    [InlineData('\0')]
    public void Resolve_ATypeThisLibraryDoesNotRead_IsRejected(char type)
    {
        // The letters are a mix of real CodeBase extensions and rubbish. Both are outside what this
        // version reads, and both are refused the same way.
        Action act = () => Resolve(recordLength: 9, Descriptor("F", type, length: 8));

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.FieldType);
    }

    [Theory]
    [InlineData('B')]
    [InlineData('H')]
    [InlineData('Y')]
    [InlineData('T')]
    public void Resolve_AVisualFoxProTypeOnAnOlderTable_IsRejectedAsCorruptData(char type)
    {
        Action act = () => Resolve(Legacy, recordLength: 9, Descriptor("F", type, length: 8));

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Resolve_AnIntegerThatIsNotFourBytes_IsRejectedAsCorruptData()
    {
        // The one length rule a release build of the C library enforces on a type this port reads.
        Action act = () => Resolve(recordLength: 4, Descriptor("N", 'I', length: 3));

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Data);
    }

    [Theory]
    [InlineData('D', 7)]     // a date that is not eight bytes
    [InlineData('L', 2)]     // a logical that is not one
    [InlineData('M', 5)]     // a memo reference that is neither four nor ten
    [InlineData('G', 7)]
    [InlineData('C', 0)]     // a character field of no width at all
    [InlineData('B', 6)]     // a double that is not eight bytes
    [InlineData('T', 4)]
    [InlineData('Y', 7)]
    public void Resolve_ALengthTheCLibraryToleratesIsTolerated(char type, byte length)
    {
        // Refusing these would refuse files the reference implementation opens. What keeps it safe
        // is that the field still lies inside the record, not that the length looked plausible.
        ResolvedFields resolved = Resolve(recordLength: 1 + length, Descriptor("F", type, length: length));

        resolved.Fields[0].Length.Should().Be(length);
        (resolved.Fields[0].RecordOffset + resolved.Fields[0].Length).Should().Be(1 + length);
    }

    [Fact]
    public void Resolve_ANumericWhoseDecimalsExceedItsWidth_IsTolerated()
    {
        Resolve(recordLength: 6, Descriptor("N", 'N', length: 5, decimals: 9))
            .Fields[0].Decimals.Should().Be(9);
    }

    [Fact]
    public void Resolve_ASystemFieldUnderAnotherName_IsRejectedAsCorruptData()
    {
        // Matched against the stored name with its case, as the C library does.
        Action act = () => Resolve(
            recordLength: 1 + 10 + 1,
            Descriptor("NAME", 'C', length: 10),
            Descriptor("_NULLFLAGS", '0', length: 1));

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Resolve_ASystemFieldOnAnOlderTable_IsRejectedAsCorruptData()
    {
        Action act = () => Resolve(
            Legacy,
            recordLength: 1 + 10 + 1,
            Descriptor("NAME", 'C', length: 10),
            Descriptor("_NullFlags", '0', length: 1));

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Resolve_ASystemFieldThatIsNotLast_StaysAnOrdinaryField()
    {
        // Only a trailing one is hidden, matching the count the C library reports.
        ResolvedFields resolved = Resolve(
            recordLength: 1 + 1 + 10,
            Descriptor("_NullFlags", '0', length: 1),
            Descriptor("NAME", 'C', length: 10));

        resolved.NullFlags.Should().BeNull();
        resolved.Fields.Should().HaveCount(2);
    }

    [Fact]
    public void Resolve_ALegacyTableHasNoNullableOrBinaryFieldsHoweverItsFlagsAreSet()
    {
        ResolvedFields resolved = Resolve(
            Legacy,
            recordLength: 11,
            Descriptor("F", 'C', length: 10, flags: FieldFlags.Nullable | FieldFlags.Binary));

        resolved.Fields[0].IsNullable.Should().BeFalse();
        resolved.Fields[0].IsBinary.Should().BeFalse();
        resolved.Fields[0].Type.Should().Be('C', "the binary marking that would make it Z is unread");
    }

    [Fact]
    public void Resolve_WhateverTheDescriptorsSay_EveryFieldLiesInsideTheRecordOrNothingIsReturned()
    {
        // The guarantee that has to hold for input nobody anticipated, so it is asserted over
        // generated descriptors rather than chosen ones. Seeded, so a failure can be reproduced.
        //
        // Half the record lengths are chosen to fit the descriptors and half at random, because a
        // purely random one almost never matches and the whole run would then be refusals — a
        // property test that asserts nothing while reporting success. The count below is what says
        // it did not happen. Computing a width here duplicates a rule the resolver owns, which is
        // legitimate for choosing an input and would not be for asserting an outcome.
        Random random = new(20260809);
        int resolvedCount = 0;

        for (int iteration = 0; iteration < 2000; iteration++)
        {
            FieldDescriptor[] descriptors = new FieldDescriptor[random.Next(1, 6)];
            int fittingLength = 1;

            for (int i = 0; i < descriptors.Length; i++)
            {
                char type = "CNFDLMGIBYTH0WQ~"[random.Next(16)];
                byte length = (byte)random.Next(256);
                byte decimals = (byte)random.Next(256);

                descriptors[i] = Descriptor(
                    $"F{i}", type, length, decimals, (FieldFlags)random.Next(256));

                fittingLength += type is 'C' or '0' ? length + (decimals << 8) : length;
            }

            int recordLength = random.Next(2) == 0 ? fittingLength : random.Next(0, 4096);

            ResolvedFields resolved;
            try
            {
                resolved = FieldResolver.Resolve(descriptors, VisualFoxPro, recordLength);
            }
            catch (CodeBaseException)
            {
                continue;   // refused, which is the other half of the promise
            }

            resolvedCount++;

            foreach (FieldDefinition field in resolved.Fields.Append(resolved.NullFlags).OfType<FieldDefinition>())
            {
                field.RecordOffset.Should().BeGreaterThanOrEqualTo(1);
                (field.RecordOffset + field.Length).Should().BeLessThanOrEqualTo(recordLength);
            }
        }

        resolvedCount.Should().BeGreaterThan(100,
            "a run in which everything was refused would assert nothing about containment");
    }

    private static FieldDescriptor Descriptor(
        string name,
        char type,
        byte length,
        byte decimals = 0,
        FieldFlags flags = FieldFlags.None,
        int storedOffset = 1) =>
        FieldDescriptor.Parse(DescriptorBytes.Build(name, type, storedOffset, length, decimals, flags));

    private static ResolvedFields Resolve(int recordLength, params FieldDescriptor[] descriptors) =>
        Resolve(VisualFoxPro, recordLength, descriptors);

    private static ResolvedFields Resolve(
        IDbfFormatVariant variant,
        int recordLength,
        params FieldDescriptor[] descriptors) =>
        FieldResolver.Resolve(descriptors, variant, recordLength);
}
