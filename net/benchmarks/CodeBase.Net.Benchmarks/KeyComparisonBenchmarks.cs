using BenchmarkDotNet.Attributes;
using CodeBase.Net.Cdx;

namespace CodeBase.Net.Benchmarks;

/// <summary>
/// The two comparison rules, priced against each other over the same bytes.
///
/// [c]KeySearch.Compare[/c] has two paths. A value with no trailing pad is a prefix and is compared with
/// [c]SequenceCompareTo[/c], which is vectorized. A value that carried trailing pad stands for the whole
/// key, and that path is a **byte-at-a-time loop** rebuilding the comparand as it goes:
/// [c]mine = i < Length ? value[i] : padByte[/c].
///
/// Every query in step 009's audit filled its key exactly, so the scalar path was never measured
/// ([c]DESIGN.md[/c] item 2) — and it is the path a real short seek takes, because
/// [c]TableTagCursor.SearchFor[/c] pads a non-prefix character value out to the key width before
/// handing it over.
///
/// Every arm compares the same 20 bytes against the same stored keys and must return the same answers;
/// only the rule and its implementation differ. What sub-step 3 landed was not "build the comparand
/// once" but "stop taking apart a comparand that is already built": the caller's buffer *already* holds
/// the pad, so when its width is the key width the whole thing goes through [c]SequenceCompareTo[/c]
/// untouched — which is why the arm that does it by hand and the arm that goes through the library
/// should now agree.
/// </summary>
[MemoryDiagnoser]
public class KeyComparisonBenchmarks
{
    /// <summary>Comparisons per invocation, so the figure reported is per comparison.</summary>
    private const int Comparisons = 256;

    /// <summary>The key width under test, matching PERF10K's character tag.</summary>
    private const int KeyLength = 20;

    /// <summary>How many bytes of the search value carry content, the rest being pad.</summary>
    private const int Content = 5;

    private byte[][] keys = null!;
    private KeySearch padded;
    private KeySearch prefix;
    private byte[] paddedValue = null!;

    /// <summary>
    /// Builds the stored keys and the two searches.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        // Stored keys that share the first bytes with the value, so the comparison runs deep into the
        // key before it decides. A comparison that differs at byte zero measures nothing.
        keys = new byte[Comparisons][];
        for (int i = 0; i < Comparisons; i++)
        {
            byte[] key = new byte[KeyLength];
            key.AsSpan().Fill((byte)' ');
            "SMITH"u8.CopyTo(key);
            key[Content + (i % (KeyLength - Content))] = (byte)('A' + (i % 26));
            keys[i] = key;
        }

        // The padded search: five bytes of content and fifteen pad, exactly as SearchFor leaves it for
        // a non-prefix character seek. Into strips the pad to set Length, and Compare then rebuilds it.
        paddedValue = new byte[KeyLength];
        paddedValue.AsSpan().Fill((byte)' ');
        "SMITH"u8.CopyTo(paddedValue);
        padded = KeySearch.Into(paddedValue, KeyLength, KeyLength, (byte)' ');

        // The prefix search over the same twenty bytes: no trailing pad to strip, so it takes the
        // vectorized path over its whole width.
        byte[] prefixValue = new byte[KeyLength];
        prefixValue.AsSpan().Fill((byte)'X');
        "SMITH"u8.CopyTo(prefixValue);
        prefix = KeySearch.Into(prefixValue, KeyLength, KeyLength, (byte)' ');

        if (!padded.ComparesPadded)
            throw new InvalidOperationException("the padded arm is not taking the padded path");

        if (prefix.ComparesPadded)
            throw new InvalidOperationException("the prefix arm is taking the padded path");
    }

    /// <summary>
    /// The vectorized path, for scale.
    /// </summary>
    /// <returns>The sum of the comparisons, so none can be discarded.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = Comparisons, Description = "prefix rule, SequenceCompareTo")]
    public int Prefix()
    {
        int sum = 0;

        for (int i = 0; i < Comparisons; i++)
            sum += prefix.Compare(keys[i]);

        return sum;
    }

    /// <summary>
    /// The padded path as the library had it: a byte-at-a-time loop rebuilding the comparand.
    /// </summary>
    /// <returns>The sum of the comparisons, which must equal the library arm's.</returns>
    /// <remarks>
    /// Written out here because the library no longer takes this path for a full-width value, and a
    /// claim about what was removed can only be checked against something that still does it. It is
    /// [c]KeySearch.Compare[/c]'s loop verbatim, including the [c]Math.Min[/c] and the per-byte
    /// reconstruction of the comparand.
    /// </remarks>
    [Benchmark(OperationsPerInvoke = Comparisons, Description = "padded rule, scalar loop (was)")]
    public int PaddedScalarAsWas()
    {
        int sum = 0;
        ReadOnlySpan<byte> content = paddedValue.AsSpan(0, Content);

        for (int i = 0; i < Comparisons; i++)
        {
            ReadOnlySpan<byte> key = keys[i];
            int width = Math.Min(key.Length, KeyLength);
            int answer = 0;

            for (int j = 0; j < width; j++)
            {
                byte mine = j < Content ? content[j] : (byte)' ';

                if (key[j] != mine)
                {
                    answer = key[j] < mine ? -1 : 1;
                    break;
                }
            }

            sum += answer;
        }

        return sum;
    }

    /// <summary>
    /// The padded path through the library as it now stands.
    /// </summary>
    /// <returns>The sum of the comparisons.</returns>
    [Benchmark(OperationsPerInvoke = Comparisons, Description = "padded rule, the library now")]
    public int PaddedLibrary()
    {
        int sum = 0;

        for (int i = 0; i < Comparisons; i++)
            sum += padded.Compare(keys[i]);

        return sum;
    }

    /// <summary>
    /// The candidate fix: compare the caller's buffer whole, because it already holds the pad.
    /// </summary>
    /// <returns>The sum of the comparisons, which must equal the scalar arm's.</returns>
    /// <remarks>
    /// Kept beside the library arm as a floor: this is the comparison with nothing else around it, so
    /// the gap between the two is what [c]KeySearch[/c] itself costs. The condition the library checks
    /// is that the value's original width equals the key width, which [c]SearchFor[/c] guarantees for a
    /// non-prefix character value and which the numeric paths satisfy as well.
    /// </remarks>
    [Benchmark(OperationsPerInvoke = Comparisons, Description = "padded rule, whole-buffer SequenceCompareTo (hand-written)")]
    public int PaddedVectorized()
    {
        int sum = 0;
        ReadOnlySpan<byte> comparand = paddedValue.AsSpan(0, KeyLength);

        for (int i = 0; i < Comparisons; i++)
            sum += keys[i].AsSpan(0, KeyLength).SequenceCompareTo(comparand);

        return sum;
    }
}
