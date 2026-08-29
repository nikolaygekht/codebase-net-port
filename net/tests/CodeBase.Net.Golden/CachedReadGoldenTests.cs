using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.IO;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// That turning the block cache on changes nothing a file decodes to.
///
/// This is the cache's gate, and it is built out of the suite that already exists rather than beside
/// it. Every other golden test asserts the port against the C library's own dumps **uncached**; these
/// assert the cached port against the uncached one, over every table, every record, every field and
/// every key of every tag. The two together give the guarantee the plan asked for, transitively: if
/// uncached matches the C library and cached matches uncached, cached matches the C library.
///
/// Comparing the two runs instead of re-parameterising the whole suite is also the stronger reading of
/// "identical results": these compare the **entire decoded output**, including values no existing
/// assertion happens to look at, rather than re-checking the same assertions with a different engine
/// setting.
///
/// The comparison is a round-trip between two configurations of the same code, so it invents no
/// expected values (DEV_APPROACH.md section 4).
/// </summary>
[Trait("Layer", "Golden")]
public sealed class CachedReadGoldenTests
{
    /// <summary>Every table the corpus holds, discovered rather than listed.</summary>
    public static TheoryData<string> Tables()
    {
        TheoryData<string> tables = [];
        foreach (string table in Corpus.TableNames)
            tables.Add(table);

        return tables;
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void EveryFieldOfEveryRecordReadsTheSameThroughTheCache(string table)
    {
        List<string> uncached = ScanFields(table, BlockCacheMode.Off);
        List<string> cached = ScanFields(table, BlockCacheMode.Always);

        cached.Should().Equal(uncached, $"{table} must decode identically with the cache on");
        uncached.Should().NotBeEmpty("a table that read nothing proves nothing");
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void EveryTagWalksTheSameThroughTheCache(string table)
    {
        List<string> uncached = WalkTags(table, BlockCacheMode.Off);
        List<string> cached = WalkTags(table, BlockCacheMode.Always);

        cached.Should().Equal(uncached, $"{table}'s tags must walk identically with the cache on");
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void EverySeekLandsTheSameThroughTheCache(string table)
    {
        List<string> uncached = SeekEveryKey(table, BlockCacheMode.Off);
        List<string> cached = SeekEveryKey(table, BlockCacheMode.Always);

        cached.Should().Equal(uncached, $"{table}'s seeks must land identically with the cache on");
    }

    [Fact]
    public void TheCacheIsActuallyBeingUsedByTheseTests()
    {
        // Without this the three above could pass by caching nothing at all, which is exactly what the
        // default mode does. It asserts the arrangement, not the behaviour.
        using CodeBaseEngine engine = Engine(BlockCacheMode.Always);
        using Table table = engine.OpenTable(Corpus.PathOf("CDXDEEP.DBF"));

        Tag tag = table.Tags["D_WIDE"];
        table.SelectTag(tag);
        for (GoResult go = table.Top(); go == GoResult.Ok; go = table.Skip(1) == SkipResult.Moved ? GoResult.Ok : GoResult.NoRecord)
        {
        }

        engine.DropCachedBlocks().Should().BeGreaterThan(
            0, "the walk should have left blocks in the cache");
    }

    [Fact]
    public void DroppingCachedBlocksLeavesTheTableReadable()
    {
        using CodeBaseEngine engine = Engine(BlockCacheMode.Always);
        using Table table = engine.OpenTable(Corpus.PathOf("CDXDEEP.DBF"));

        List<string> before = Walk(table);
        engine.DropCachedBlocks();
        List<string> after = Walk(table);

        after.Should().Equal(before, "dropping what is cached must not change what is read");
    }

    private static CodeBaseEngine Engine(BlockCacheMode mode) => new() { BlockCache = mode };

    private static List<string> ScanFields(string table, BlockCacheMode mode)
    {
        using CodeBaseEngine engine = Engine(mode);
        using Table open = engine.OpenTable(Corpus.PathOf(table + ".DBF"));

        List<string> seen = [];

        for (GoResult go = open.Top(); go == GoResult.Ok;)
        {
            seen.Add(RecordText(open));

            if (open.Skip(1) != SkipResult.Moved)
                break;
        }

        return seen;
    }

    private static string RecordText(Table table)
    {
        List<string> parts = [$"#{table.RecordNumber}", table.Deleted ? "deleted" : "live"];

        foreach (FieldDefinition field in table.Fields)
        {
            if (table.IsNull(field))
            {
                parts.Add($"{field.Name}=null");
                continue;
            }

            // Raw bytes rather than a typed read: they are what the file holds, they exist for every
            // type, and they cannot be equal while the decoded value differs.
            parts.Add($"{field.Name}={Convert.ToHexString(table.GetRawBytes(field))}");

            if (field.StoredType is 'M' or 'G')
                parts.Add($"{field.Name}.memo={Convert.ToHexString(table.GetMemoBytes(field))}");
        }

        return string.Join('|', parts);
    }

    private static List<string> WalkTags(string table, BlockCacheMode mode)
    {
        using CodeBaseEngine engine = Engine(mode);
        using Table open = engine.OpenTable(Corpus.PathOf(table + ".DBF"));

        List<string> seen = [];

        foreach (Tag tag in open.Tags)
        {
            if (!KeysABareField(tag, open))
                continue;

            open.SelectTag(tag);
            seen.Add($"--- {tag.Name}");
            seen.AddRange(Walk(open));
        }

        return seen;
    }

    private static List<string> Walk(Table table)
    {
        List<string> seen = [];

        for (GoResult go = table.Top(); go == GoResult.Ok;)
        {
            seen.Add(RecordText(table));

            if (table.Skip(1) != SkipResult.Moved)
                break;
        }

        return seen;
    }

    private static string Outcome(Table table, string value)
    {
        try
        {
            GoResult found = table.Seek(value);
            return found == GoResult.Ok ? $"{found}:{table.RecordNumber}" : found.ToString();
        }
        catch (Exception refused)
        {
            return refused.GetType().Name;
        }
    }

    private static List<string> SeekEveryKey(string table, BlockCacheMode mode)
    {
        using CodeBaseEngine engine = Engine(mode);
        using Table open = engine.OpenTable(Corpus.PathOf(table + ".DBF"));

        List<string> seen = [];

        foreach (Tag tag in open.Tags)
        {
            if (!KeysABareField(tag, open))
                continue;

            // Every value the tag holds, sought by its own text, which is the case a caller writes
            // and the one that exercises the padded comparison as well.
            open.SelectTag(tag);
            List<string> values = [];

            for (GoResult go = open.Top(); go == GoResult.Ok;)
            {
                values.Add(open.GetString(open.Fields[0]));
                if (open.Skip(1) != SkipResult.Moved)
                    break;
            }

            foreach (string value in values)
            {
                // Refusals are compared too, and deliberately: a tag whose keys are not character
                // does not take a string, so what matters is that both runs refuse the same way. See
                // the note in the step's STATE.md about what that refusal currently looks like.
                seen.Add($"{tag.Name}:{value}={Outcome(open, value)}");
            }
        }

        return seen;
    }

    /// <summary>Answers whether the tag's key expression is a bare field name of the table.</summary>
    /// <param name="tag">The tag whose expression is examined.</param>
    /// <param name="table">The table the tag belongs to, whose fields the expression is matched against.</param>
    /// <returns>[c]true[/c] when the expression names a field, so the tag can be selected today.</returns>
    private static bool KeysABareField(Tag tag, Table table)
    {
        // ADR-28: a tag whose expression is anything else cannot be typed until the expression
        // engine exists, and SelectTag throws. CDXEXPR is in the corpus so step 010 has an
        // oracle to build against; its expression tags become selectable at sub-step 8, and
        // this guard is deleted then -- see claude/dev/010-expr/PLAN.md.
        foreach (FieldDefinition field in table.Fields)
        {
            if (string.Equals(field.Name, tag.Expression, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
