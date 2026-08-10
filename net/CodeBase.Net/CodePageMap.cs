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
    /// Finds the encoding for a code page, falling back where the table names none.
    /// </summary>
    /// <param name="codePage">The code page the table names.</param>
    /// <param name="fallback">
    /// What to use when the table names no usable code page, or null to use the code page an
    /// unmarked table is read as.
    /// </param>
    /// <returns>The encoding record text should be read with.</returns>
    /// <exception cref="CodeBaseException">
    /// The encoding is not available, which on modern .NET means no provider for the legacy code
    /// pages has been registered.
    /// </exception>
    public static Encoding EncodingFor(CodePage codePage, Encoding? fallback)
    {
        int number = codePage switch
        {
            CodePage.Cp437 => 437,
            CodePage.Cp850 => 850,
            CodePage.Cp1252 => 1252,
            CodePage.Cp1250 => 1250,

            // Unmarked, unknown, and the placeholder alike: the table does not say, so the caller
            // decides. Falling back to what an unmarked table means keeps the default behaviour the
            // same as the C library's.
            _ => 0,
        };

        if (number == 0)
            return fallback ?? Lookup(UnmarkedCodePage);

        return Lookup(number);
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
                "or set CodeBaseEngine.DefaultEncoding to an encoding you already hold.",
                failure);
        }
    }
}
