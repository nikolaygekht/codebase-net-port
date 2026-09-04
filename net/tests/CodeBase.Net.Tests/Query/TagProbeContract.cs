using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.Queries;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Query;

/// <summary>
/// What every tag probe promises, whether it is reading an index file or a list in memory.
///
/// The list in memory is a second implementation written from the same description, and nothing makes
/// the two agree by itself. A stand-in that reported the end of the keys where a real tag reported
/// one more would make every test built on it confidently wrong, so both are run through these.
///
/// The promises are about stored order, which is the order keys sort in and not the order a tag is
/// walked in -- those differ for a descending tag, and reconciling them is the probe's whole job.
///
/// Unlike the contract the byte sources are held to, these are stated [i]relative[/i] to whatever keys
/// an implementation holds rather than over a fixed set. A source can be created over any bytes a test
/// invents; a tag cannot be created over invented keys without writing an index file, which is not
/// something this library can do yet. So the real implementation brings a corpus tag's keys and the
/// stand-in is loaded with the same ones, read back out of it.
/// </summary>
public abstract class TagProbeContract
{
    /// <summary>Creates a probe over the keys this implementation holds.</summary>
    /// <returns>The probe.</returns>
    private protected abstract ITagProbe CreateProbe();

    /// <summary>Gets those keys, blank-trimmed, in sorted order.</summary>
    private protected abstract IReadOnlyList<string> KeysHeld { get; }

    [Fact]
    public void First_LandsOnTheLowestKey()
    {
        ITagProbe probe = CreateProbe();

        probe.First().Should().BeTrue();
        Text(probe).Should().Be(KeysHeld[0]);
    }

    [Fact]
    public void Step_WalksEveryKeyOnceInSortedOrder()
    {
        ITagProbe probe = CreateProbe();
        List<string> walked = [];

        for (bool on = probe.First(); on; on = probe.Step())
            walked.Add(Text(probe));

        walked.Should().Equal(KeysHeld);
    }

    [Fact]
    public void Step_ReportsTheEndRatherThanWrappingOrRepeating()
    {
        ITagProbe probe = CreateProbe();

        probe.SeekAtOrAfter(probe.KeyFor(QueryValues.Text(KeysHeld[^1]))).Should().BeTrue();

        // There may be several records under the last key; walk off the end of the group.
        while (probe.Step())
        {
        }

        probe.Step().Should().BeFalse();
    }

    [Fact]
    public void SeekAtOrAfter_LandsOnTheFirstKeyEqualToTheValue()
    {
        ITagProbe probe = CreateProbe();
        string wanted = KeysHeld[KeysHeld.Count / 2];

        probe.SeekAtOrAfter(probe.KeyFor(QueryValues.Text(wanted))).Should().BeTrue();
        Text(probe).Should().Be(wanted);

        // The first of the group: the key before it must be a different one.
        int landed = KeysHeld.ToList().IndexOf(wanted);
        for (int before = 0; before < landed; before++)
            KeysHeld[before].Should().NotBe(wanted);
    }

    [Fact]
    public void SeekAtOrAfter_LandsOnTheLowestKeyWhenTheValueIsBelowEverything()
    {
        ITagProbe probe = CreateProbe();

        probe.SeekAtOrAfter(Below(probe)).Should().BeTrue();
        Text(probe).Should().Be(KeysHeld[0]);
    }

    [Fact]
    public void SeekAtOrAfter_ReportsNothingWhenEveryKeySortsBelowTheValue()
    {
        ITagProbe probe = CreateProbe();

        probe.SeekAtOrAfter(Above(probe)).Should().BeFalse();
    }

    /// <summary>
    /// A value shorter than the keys names the group that begins with it, rather than one key padded
    /// out to the width. That is what makes a short constant a prefix, and it is the promise the
    /// widening rule in decomposition is built on.
    /// </summary>
    [Fact]
    public void AShortValueComparesOnItsOwnBytesAlone()
    {
        ITagProbe probe = CreateProbe();

        // The lowest key may be blank -- CDXBASE's does -- and a blank has no first character to
        // take a prefix of, so the check runs against the lowest key that has one.
        string whole = KeysHeld.First(key => key.Length > 1);
        KeySearch prefix = probe.KeyFor(QueryValues.Text(whole[..1]));

        probe.SeekAtOrAfter(prefix).Should().BeTrue();
        prefix.Compare(probe.Current.Key)
            .Should().Be(0, "{0} begins with {1}", Text(probe), whole[..1]);
    }

    /// <summary>
    /// Two comparands taken from one probe are both still valid, because a caller holds several at
    /// once: a range has two ends, and every value struck out of it is a third.
    /// </summary>
    /// <value>
    /// The promise that a buffer reused between calls would break, and the one a stand-in gets right
    /// by accident -- it allocates -- while an implementation careful about allocation does not. That
    /// asymmetry is exactly what a shared contract is for.
    /// </value>
    [Fact]
    public void TwoComparandsTakenFromOneProbeAreBothStillValid()
    {
        ITagProbe probe = CreateProbe();
        string low = KeysHeld[0];
        string high = KeysHeld[^1];

        KeySearch first = probe.KeyFor(QueryValues.Text(low));
        KeySearch second = probe.KeyFor(QueryValues.Text(high));

        probe.First().Should().BeTrue();
        first.Compare(probe.Current.Key).Should().Be(0, "the lowest key still matches the first");
        second.Compare(probe.Current.Key).Should().BeLessThan(0, "and the last key is above it");
    }

    /// <summary>A value that sorts at or below every printable key.</summary>
    private static KeySearch Below(ITagProbe probe) => probe.KeyFor(QueryValues.Text(" "));

    /// <summary>A value that sorts above every printable key.</summary>
    private static KeySearch Above(ITagProbe probe) => probe.KeyFor(QueryValues.Text("ÿ"));

    private static string Text(ITagProbe probe) =>
        System.Text.Encoding.Latin1.GetString(probe.Current.Key).TrimEnd();
}
