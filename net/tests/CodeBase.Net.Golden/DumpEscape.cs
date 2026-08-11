using System.Globalization;

namespace CodeBase.Net.Golden;

/// <summary>
/// Reads the quoted byte strings the dump writes field values as.
///
/// The generator escapes a byte string into C-ish text: a quote and a backslash are backslash
/// escaped, any byte from 0x20 to 0x7E is written verbatim, and everything else becomes a
/// backslash-x pair of upper-case hexadecimal digits. See [c]dumpEscaped[/c] in
/// [c]test-files-generator/src/dump.cpp[/c].
///
/// Two consequences shape this reader. A value can contain spaces, so a line cannot be split on
/// whitespace before its quoted parts are taken off it. And the bytes are bytes, never text: they
/// are returned unchanged and are decoded, if at all, by the test that knows the code page.
/// </summary>
internal static class DumpEscape
{
    /// <summary>
    /// Reads one quoted value and leaves the position just past its closing quote.
    /// </summary>
    /// <param name="line">The line being read.</param>
    /// <param name="index">
    /// Where the opening quote is. Advanced past the closing quote on return, so a caller can keep
    /// reading the rest of the line.
    /// </param>
    /// <returns>The bytes the quoted text stands for.</returns>
    /// <exception cref="InvalidDataException">
    /// The text is not a quoted value, ends without its closing quote, or carries an escape this
    /// reader does not know. Every one of those means the expectations would be silently wrong.
    /// </exception>
    public static byte[] ReadQuoted(string line, ref int index)
    {
        if (index >= line.Length || line[index] != '"')
            throw new InvalidDataException($"Expected a quoted value at position {index} of: {line}");

        List<byte> bytes = [];
        int i = index + 1;

        while (i < line.Length && line[i] != '"')
        {
            if (line[i] != '\\')
            {
                bytes.Add((byte)line[i]);
                i++;
                continue;
            }

            if (i + 1 >= line.Length)
                throw new InvalidDataException($"A backslash ends the line: {line}");

            char escaped = line[i + 1];
            switch (escaped)
            {
                case '"':
                case '\\':
                    bytes.Add((byte)escaped);
                    i += 2;
                    break;

                case 'x':
                    if (i + 3 >= line.Length ||
                        !byte.TryParse(
                            line.AsSpan(i + 2, 2),
                            NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture,
                            out byte value))
                    {
                        throw new InvalidDataException(
                            $"A hex escape at position {i} is cut short or is not hexadecimal: {line}");
                    }

                    bytes.Add(value);
                    i += 4;
                    break;

                default:
                    throw new InvalidDataException(
                        $"Escape '\\{escaped}' at position {i} is one this reader does not know: {line}");
            }
        }

        if (i >= line.Length)
            throw new InvalidDataException($"A quoted value is never closed: {line}");

        index = i + 1;
        return [.. bytes];
    }

    /// <summary>
    /// Moves the position past any spaces.
    /// </summary>
    /// <param name="line">The line being read.</param>
    /// <param name="index">Where to start. Advanced to the first character that is not a space.</param>
    public static void SkipSpaces(string line, ref int index)
    {
        while (index < line.Length && line[index] == ' ')
            index++;
    }

    /// <summary>
    /// Reads the word at the position and leaves the position on the space that ended it.
    /// </summary>
    /// <param name="line">The line being read.</param>
    /// <param name="index">Where the word starts. Advanced past its last character.</param>
    /// <returns>The characters up to the next space, or to the end of the line.</returns>
    public static string ReadWord(string line, ref int index)
    {
        int start = index;
        while (index < line.Length && line[index] != ' ')
            index++;

        return line[start..index];
    }
}
