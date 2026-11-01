using AwesomeAssertions;
using CodeBase.Net.IO;
using CodeBase.Net.Memo;
using CodeBase.Net.Tests.Cdx;
using Xunit;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// That each public member which hands a question on to another part of the library reaches it.
/// </summary>
/// <value>
/// ADR-67: one test per forwarding member, over a table built in memory, with the answer read off
/// the bytes the image lays down. What the decoders themselves compute is gated where they live --
/// the field decoders against the corpus, the memo reader against the memo corpus -- so these only
/// prove the member asks the right one, for the right field of the right record.
/// </value>
[Trait("Layer", "Component")]
public sealed class TablePublicSurfaceTests
{
    [Fact]
    public void TheHeaderPropertiesReadTheHeader()
    {
        (CodeBaseEngine engine, Table table) = MemoTableImage.Open();
        using (engine)
        {
            table.Version.Should().Be(0x30);
            table.LastUpdate.Should().Be((26, 1, 1));
            table.RecordLength.Should().Be(30);
            table.CodePageByte.Should().Be(0);
            table.HasMemo.Should().BeTrue();
            table.MemoBlockSize.Should().Be(MemoTableImage.BlockSize);
            table.Descriptors.Select(d => d.Name).Should().Equal("NOTE", "FLAG", "PRICE", "BORN", "STAMP");
        }
    }

    [Fact]
    public void ATableWithNoMemoFileSaysSo()
    {
        (CodeBaseEngine engine, Table table) = TableImage.Open(" REC001");
        using (engine)
        {
            table.HasMemo.Should().BeFalse();
            table.MemoBlockSize.Should().BeNull();
        }
    }

    [Fact]
    public void TheTypedReadersReadTheCurrentRecordsField()
    {
        (CodeBaseEngine engine, Table table) = MemoTableImage.Open();
        using (engine)
        {
            table.Go(1);
            table.GetBoolean(table.Fields["FLAG"]).Should().BeTrue();
            table.GetDecimal(table.Fields["PRICE"]).Should().Be(12.50m);
            table.GetDate(table.Fields["BORN"]).Should().Be(new DateOnly(1987, 6, 15));
            table.GetDateTime(table.Fields["STAMP"]).Should().Be(new DateTime(2000, 1, 15, 12, 34, 56));

            table.Go(2);
            table.GetBoolean(table.Fields["FLAG"]).Should().BeFalse();
            table.GetDecimal(table.Fields["PRICE"]).Should().Be(-3m);
            table.GetDate(table.Fields["BORN"]).Should().BeNull("a blank date is no date");
            table.GetDateTime(table.Fields["STAMP"]).Should().BeNull();
        }
    }

    [Fact]
    public void TheMemoReadersReadTheEntryTheRecordPointsAt()
    {
        (CodeBaseEngine engine, Table table) = MemoTableImage.Open();
        using (engine)
        {
            table.Go(1);
            var note = table.Fields["NOTE"];

            table.GetMemoBlock(note).Should().Be(MemoTableImage.MemoBlock);
            table.GetMemoLength(note).Should().Be(MemoTableImage.Memo.Length);
            table.GetMemoBytes(note).Should().Equal(System.Text.Encoding.ASCII.GetBytes(MemoTableImage.Memo));
            table.GetMemoType(note).Should().Be(MemoType.Text);
            table.GetMemoString(note).Should().Be(MemoTableImage.Memo);
        }
    }

    [Fact]
    public void ARecordWithNoMemoReadsAsAnEmptyOne()
    {
        (CodeBaseEngine engine, Table table) = MemoTableImage.Open();
        using (engine)
        {
            table.Go(2);
            var note = table.Fields["NOTE"];

            table.GetMemoBlock(note).Should().Be(0);
            table.GetMemoLength(note).Should().Be(0);
            table.GetMemoString(note).Should().BeEmpty();
        }
    }

    [Fact]
    public void AMemoPointerWithNoMemoFileIsRefused()
    {
        // The header and the descriptors disagree; reading block 8 of nothing would be a guess.
        (CodeBaseEngine engine, Table table) = MemoTableImage.Open(declareMemo: false);
        using (engine)
        {
            table.Go(1);

            table.Invoking(t => t.GetMemoBytes(t.Fields["NOTE"]))
                .Should().Throw<CodeBaseException>()
                .Where(e => e.Code == ErrorCode.Data && e.Message.Contains("NOTE"));
        }
    }

    [Fact]
    public void AnEmptyMemoFieldReadsAsEmptyEvenWithNoMemoFile()
    {
        // Block zero is no memo, so there is nothing to follow and no file is needed to say so.
        (CodeBaseEngine engine, Table table) = MemoTableImage.Open(declareMemo: false);
        using (engine)
        {
            table.Go(2);

            table.GetMemoLength(table.Fields["NOTE"]).Should().Be(0);
        }
    }

    [Fact]
    public void TheFieldListCountsAndFindsByName()
    {
        (CodeBaseEngine engine, Table table) = MemoTableImage.Open();
        using (engine)
        {
            table.Fields.Count.Should().Be(5);

            table.Fields.TryGet("price", out var found).Should().BeTrue("names are found whatever their case");
            found!.Name.Should().Be("PRICE");
            table.Fields.TryGet("NONE", out _).Should().BeFalse();
        }
    }

    [Fact]
    public void ATagDescribesItself()
    {
        (CodeBaseEngine engine, Table table) = IndexedTableImage.Open(["A"], [("A", 1)]);
        using (engine)
        {
            Tag tag = table.Tags[0];

            tag.Filter.Should().BeEmpty();
            tag.Collation.Should().BeEmpty("the tag is in machine order");
            tag.ToString().Should().Contain(tag.Name).And.Contain(IndexedTableImage.FieldName);
        }
    }

    [Fact]
    public void SteppingBackInATagThatIsNotSelectedMovesToThePreviousKey()
    {
        (CodeBaseEngine engine, Table table) = IndexedTableImage.Open(
            ["CHERRY", "APPLE", "BANANA"], [("APPLE", 2), ("BANANA", 3), ("CHERRY", 1)]);
        using (engine)
        {
            Tag tag = table.Tags[0];
            table.Go(1);

            table.GoPreviousIndexed(tag).Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(3);
            table.GoPreviousIndexed(tag).Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(2);
            table.GoPreviousIndexed(tag).Should().Be(GoResult.NoRecord);
        }
    }

    [Fact]
    public void SteppingInATagFromPastTheEndReentersAtItsLastKey()
    {
        (CodeBaseEngine engine, Table table) = IndexedTableImage.Open(
            ["CHERRY", "APPLE", "BANANA"], [("APPLE", 2), ("BANANA", 3), ("CHERRY", 1)]);
        using (engine)
        {
            Tag tag = table.Tags[0];

            table.Bottom();
            table.Skip(1);
            table.GoNextIndexed(tag).Should().Be(GoResult.NoRecord, "nothing follows the end");

            table.Bottom();
            table.Skip(1);
            table.GoPreviousIndexed(tag).Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(1, "CHERRY is the tag's last key");
        }
    }

    [Fact]
    public void SteppingInATagFromNoRecordIsRefused()
    {
        // A seek that misses leaves the table on no record at all (TableSeekTests).
        (CodeBaseEngine engine, Table table) = IndexedTableImage.Open(
            ["A"], [("A", 1)], encoding: System.Text.Encoding.Latin1);
        using (engine)
        {
            table.SelectTag(table.Tags[0]);
            table.Seek("Z").Should().Be(GoResult.NoRecord);

            table.Invoking(t => t.GoNextIndexed(t.Tags[0]))
                .Should().Throw<CodeBaseException>()
                .Where(e => e.Code == ErrorCode.Info && e.Message.Contains("needs a position to step from"));
        }
    }

    [Fact]
    public void SteppingInATagFromARecordTheTagDoesNotHoldFindsNothing()
    {
        // Record 2 has no entry, so there is no place in the tag to step from.
        (CodeBaseEngine engine, Table table) = IndexedTableImage.Open(["A", "B"], [("A", 1)]);
        using (engine)
        {
            table.Go(2);

            table.GoNextIndexed(table.Tags[0]).Should().Be(GoResult.NoRecord);
        }
    }

    [Fact]
    public void TheEnginesBlockCacheSettingsAreItsOwn()
    {
        using CodeBaseEngine engine = new() { BlockCache = BlockCacheMode.Always, BlockCacheBlocks = 64 };

        engine.BlockCache.Should().Be(BlockCacheMode.Always);
        engine.BlockCacheBlocks.Should().Be(64);
    }
}
