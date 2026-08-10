using System.Text;

namespace CodeBase.Net;

/// <summary>
/// Turns a table's language-driver byte into a code page, and a code page into an encoding.
///
/// This type registers no encoding provider, deliberately. Registering one changes
/// [c]Encoding.GetEncoding[/c] for every component in the process, including code that has never
/// heard of this library, and that is a decision belonging to whoever composes the application. The
/// cost of leaving it there is one documented line of setup. See ADR-17.
/// </summary>
internal static class CodePageMap
{
    /// <summary>
    /// The code page an unmarked table is read as, matching what the C library assumes.
    /// </summary>
    public const int UnmarkedCodePage = 437;

    /// <summary>
    /// Identifies the code page a language-driver byte names.
    /// </summary>
    /// <param name="languageDriver">The byte the header stores.</param>
    /// <returns>The code page, or a value saying the byte names none this library knows.</returns>
    public static CodePage Resolve(byte languageDriver) =>
        Enum.IsDefined(typeof(CodePage), (int)languageDriver) && languageDriver != unchecked((byte)CodePage.Unknown)
            ? (CodePage)languageDriver
            : CodePage.Unknown;

    /// <summary>
    /// Finds the number of the code page a mark names.
    /// </summary>
    /// <param name="codePage">The code page the table's mark names.</param>
    /// <returns>The code page number, or null where the mark names none this library knows.</returns>
    public static int? NumberFor(CodePage codePage) => codePage switch
    {
        CodePage.Cp437 => 437,
        CodePage.Cp620 => 620,
        CodePage.Cp737 => 737,
        CodePage.Cp850 => 850,
        CodePage.Cp852 => 852,
        CodePage.Cp857 => 857,
        CodePage.Cp861 => 861,
        CodePage.Cp865 => 865,
        CodePage.Cp866 => 866,
        CodePage.Cp874 => 874,
        CodePage.Cp895 => 895,
        CodePage.Cp932 => 932,
        CodePage.Cp936 => 936,
        CodePage.Cp949 => 949,
        CodePage.Cp950 => 950,
        CodePage.Cp1250 => 1250,
        CodePage.Cp1251 => 1251,
        CodePage.Cp1252 => 1252,
        CodePage.Cp1253 => 1253,
        CodePage.Cp1254 => 1254,
        CodePage.Cp1255 => 1255,
        CodePage.Cp1256 => 1256,
        CodePage.Cp10000 => 10000,
        CodePage.Cp10006 => 10006,
        CodePage.Cp10007 => 10007,
        CodePage.Cp10029 => 10029,

        // Unmarked and unrecognized alike: the header does not name a code page, so there is no
        // number to report and the caller's fallback decides.
        _ => null,
    };

    /// <summary>
    /// Finds the encoding for a code page, falling back where the table names none.
    /// </summary>
    /// <param name="codePage">The code page the table names.</param>
    /// <param name="fallback">
    /// What to use when the table names no usable code page, or null to use the code page an
    /// unmarked table is read as.
    /// </param>
    /// <returns>The encoding record text should be read with.</returns>
    /// <exception cref="CodeBaseException">
    /// The encoding is not available, which usually means no provider for the legacy code pages has
    /// been registered, and for two of the marks means the code page has no .NET encoding at all.
    /// </exception>
    public static Encoding EncodingFor(CodePage codePage, Encoding? fallback)
    {
        int? number = NumberFor(codePage);

        // The table does not say, so the caller decides. Falling back to what an unmarked table
        // means keeps the default behaviour the same as the C library's.
        if (number is null)
            return fallback ?? Lookup(UnmarkedCodePage);

        return Lookup(number.Value);
    }

    /// <summary>
    /// Finds an encoding, explaining the one setup step that is the usual reason it is missing.
    /// </summary>
    private static Encoding Lookup(int number)
    {
        try
        {
            return Encoding.GetEncoding(number);
        }
        catch (Exception failure) when (failure is NotSupportedException or ArgumentException)
        {
            throw new CodeBaseException(
                ErrorCode.NotSupported,
                $"Code page {number} is not available. This library does not register encoding " +
                "providers, because doing so would change encodings for the whole process. Add a " +
                "reference to System.Text.Encoding.CodePages and call " +
                "Encoding.RegisterProvider(CodePagesEncodingProvider.Instance) once at start-up, " +
                "or set CodeBaseEngine.DefaultEncoding to an encoding you already hold. Code pages " +
                "620 and 895 are the exception: they are FoxPro-era DOS code pages that Windows " +
                "never defined, so no provider supplies them and a table marked with one can only " +
                "be read through a fallback encoding.",
                failure);
        }
    }
}
