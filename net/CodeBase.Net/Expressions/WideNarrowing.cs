using System.Buffers.Binary;
using System.Text;

namespace CodeBase.Net.Expressions;

/// <summary>
/// Turns a wide string into a single-byte code page's bytes the way Windows does, for STR of a W field.
///
/// CodeBase calls WideCharToMultiByte with the host's ANSI code page and no flags
/// (e4functi.c:2694-2716), so Windows' best-fit mapping decides what a character the code page lacks
/// becomes, and a question mark stands for the rest. The port narrows by the table's code page
/// instead (ADR-77). .NET's code-page encodings best-fit the same way except for a few code points,
/// which [clink=CodeBase.Net.Expressions.WindowsBestFitSupplement]WindowsBestFitSupplement[/clink]
/// supplies, so the two together give Windows' answer.
///
/// One byte per UTF-16 code unit, which is what WideCharToMultiByte gives on a single-byte code page:
/// each half of a surrogate pair becomes its own question mark. A multi-byte code page is refused
/// rather than narrowed into a width that no longer counts characters.
///
/// The code page is asked for on first use, as a table's case map is, so a table whose code page is
/// unavailable still opens and compiles its expressions.
/// </summary>
internal sealed class WideNarrowing
{
    private readonly Func<int> codePage;
    private byte[]? table;

    private WideNarrowing(Func<int> codePage)
    {
        this.codePage = codePage;
    }

    /// <summary>
    /// Creates a narrowing for the code page a table's text is in.
    /// </summary>
    /// <param name="codePage">Supplies the code page number, asked for once and only when first needed.</param>
    /// <returns>The narrowing.</returns>
    public static WideNarrowing For(Func<int> codePage) => new(codePage);

    /// <summary>
    /// Narrows little-endian UTF-16 into as many bytes as the destination holds, one per code unit.
    /// </summary>
    /// <param name="wide">The UTF-16 bytes, at least twice the destination's length.</param>
    /// <param name="destination">Where to write.</param>
    /// <exception cref="CodeBaseException">
    /// The code page is multi-byte, or no encoding provider supplies it.
    /// </exception>
    public void Narrow(ReadOnlySpan<byte> wide, Span<byte> destination)
    {
        byte[] map = LazyInitializer.EnsureInitialized(ref table, () => Build(codePage()));

        for (int i = 0; i < destination.Length; i++)
            destination[i] = map[BinaryPrimitives.ReadUInt16LittleEndian(wide[(2 * i)..])];
    }

    /// <summary>
    /// Builds the byte every code unit narrows to on one code page.
    /// </summary>
    private static byte[] Build(int number)
    {
        Encoding encoding = BestFitEncoding(number);

        if (!encoding.IsSingleByte)
        {
            throw new CodeBaseException(
                ErrorCode.NotSupported,
                $"STR of a wide field narrows to the table's code page, and code page {number} is " +
                "multi-byte, where one character is no longer one byte. Only single-byte code pages " +
                "are supported.");
        }

        WindowsBestFitSupplement.ByCodePage.TryGetValue(number, out IReadOnlyDictionary<char, byte>? supplement);

        byte[] map = new byte[char.MaxValue + 1];
        Span<char> unit = stackalloc char[1];
        Span<byte> narrowed = stackalloc byte[4];

        for (int codeUnit = 0; codeUnit <= char.MaxValue; codeUnit++)
        {
            unit[0] = (char)codeUnit;

            if (supplement is not null && supplement.TryGetValue(unit[0], out byte windows))
            {
                map[codeUnit] = windows;
                continue;
            }

            // A lone surrogate, which every code unit here is when taken alone, gives the question
            // mark; so does any other code point the encoding neither holds nor best-fits.
            map[codeUnit] = encoding.GetBytes(unit, narrowed) == 1 ? narrowed[0] : (byte)'?';
        }

        return map;
    }

    /// <summary>
    /// Looks up the encoding with its default fallbacks, which are .NET's best-fit and a question mark.
    /// </summary>
    private static Encoding BestFitEncoding(int number)
    {
        try
        {
            return Encoding.GetEncoding(number);
        }
        catch (Exception failure) when (failure is NotSupportedException or ArgumentException)
        {
            throw new CodeBaseException(
                ErrorCode.NotSupported,
                $"STR of a wide field narrows to the table's code page, and code page {number} is not " +
                "available. Register System.Text.Encoding.CodePages with " +
                "Encoding.RegisterProvider(CodePagesEncodingProvider.Instance) at start-up.",
                failure);
        }
    }
}
