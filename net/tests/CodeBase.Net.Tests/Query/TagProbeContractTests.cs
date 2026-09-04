using CodeBase.Net.Cdx;
using CodeBase.Net.Expressions;
using CodeBase.Net.Query;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Query;

/// <summary>
/// The contract, over a real tag of a real index file.
/// </summary>
/// <value>
/// CDXBASE's duplicate-heavy character tag: ten characters wide, six distinct values across
/// thirty-two records, so groups of equal keys and the boundaries between them are both present.
/// </value>
[Trait("Layer", "Component")]
public sealed class TagProbeContractTests : TagProbeContract
{
    private static readonly Lazy<IReadOnlyList<string>> LazyKeys = new(ReadKeys);

    private protected override IReadOnlyList<string> KeysHeld => LazyKeys.Value;

    /// <summary>Gets the keys the real tag holds, so the stand-in can be loaded with the same.</summary>
    internal IReadOnlyList<string> Keys => LazyKeys.Value;

    private protected override ITagProbe CreateProbe() => Open().Probe;

    /// <summary>
    /// Opens the tag and takes a probe over it, keeping the table alive for the probe's lifetime.
    /// </summary>
    private static (Table Table, ITagProbe Probe) Open()
    {
        var engine = new CodeBaseEngine();
        Table table = engine.OpenTable(Corpus.PathOf("CDXBASE.DBF"));
        Tag tag = table.Tags["T_DUP"];

        return (table, new TagProbe(
            tag.Inner,
            SeekConverter.For(
                tag.Inner.Header,
                [.. table.Fields],
                table.CodePageNumber ?? CodePageMap.UnmarkedCodePage,
                ExpressionOptions.Invariant,
                "CDXBASE")));
    }

    private static IReadOnlyList<string> ReadKeys()
    {
        (Table table, ITagProbe probe) = Open();
        using (table)
        {
            List<string> keys = [];

            for (bool on = probe.First(); on; on = probe.Step())
                keys.Add(System.Text.Encoding.Latin1.GetString(probe.Current.Key).TrimEnd());

            return keys;
        }
    }
}

/// <summary>
/// The same contract, over the list in memory that stands in for a tag everywhere else.
/// </summary>
/// <value>
/// Loaded with the keys read out of the real tag, so the two implementations answer the same
/// questions about the same data and any disagreement is the stand-in's.
/// </value>
[Trait("Layer", "Unit")]
public sealed class SortedKeyProbeContractTests : TagProbeContract
{
    private static readonly Lazy<IReadOnlyList<string>> LazyKeys =
        new(() => new TagProbeContractTests().Keys);

    private protected override IReadOnlyList<string> KeysHeld => LazyKeys.Value;

    private protected override ITagProbe CreateProbe() =>
        SortedKeyProbe.OfText(10, [.. KeysHeld]);
}
