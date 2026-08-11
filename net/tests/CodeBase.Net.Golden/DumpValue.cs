using System.Globalization;

namespace CodeBase.Net.Golden;

/// <summary>
/// One field of one record, as the dump records it.
///
/// The bytes are always there. What accompanies them depends on the type, because the generator
/// asks the C library only the questions that type answers: a decoded double for the numeric and
/// currency types, a long for an integer, a bracketed string for a date or a datetime, and for a
/// memo field the length and contents behind the in-record reference. Anything the dump does not
/// carry is absent here rather than defaulted, so a test cannot assert against a zero the reference
/// never produced.
///
/// Equality is not useful on this type because two of its members are byte arrays, which compare by
/// reference. Tests assert its members.
/// </summary>
internal sealed class DumpValue
{
    private DumpValue(
        string name,
        byte[] bytes,
        double? number,
        long? integer,
        string? text,
        bool isNull,
        long? memoLength,
        byte[]? memoBytes)
    {
        Name = name;
        Bytes = bytes;
        Number = number;
        Integer = integer;
        Text = text;
        IsNull = isNull;
        MemoLength = memoLength;
        MemoBytes = memoBytes;
    }

    /// <summary>Gets the field name, upper-cased as the C library reports it.</summary>
    public string Name { get; }

    /// <summary>
    /// Gets the field's bytes as they sit in the record.
    /// </summary>
    /// <value>
    /// For a memo field these are the in-record reference, not the memo contents: the four binary
    /// bytes or the ten ASCII digits naming the block. The contents are separate.
    /// </value>
    public byte[] Bytes { get; }

    /// <summary>
    /// Gets the value the C library decoded with [c]f4double[/c], or null where it asked no such question.
    /// </summary>
    /// <value>
    /// Written with seventeen significant digits, which round-trips a double exactly, so comparing
    /// against this is a comparison against the reference and not against a rounding.
    /// </value>
    public double? Number { get; }

    /// <summary>Gets the value the C library decoded with [c]f4long[/c], or null where it asked no such question.</summary>
    public long? Integer { get; }

    /// <summary>
    /// Gets the string form of a date or datetime field, or null for every other type.
    /// </summary>
    /// <value>
    /// The text between the brackets, spaces included. A blank datetime comes back as eight spaces
    /// followed by a zero time, which is a shape worth asserting rather than trimming away.
    /// </value>
    public string? Text { get; }

    /// <summary>
    /// Gets a value indicating whether the record has this field marked null.
    /// </summary>
    /// <value>
    /// Written only when true, so its absence is the answer rather than a gap. See ADR-16. A field
    /// marked null still holds whatever bytes were last assigned to it.
    /// </value>
    public bool IsNull { get; }

    /// <summary>Gets the length of the memo contents, or null when the field is not a memo.</summary>
    public long? MemoLength { get; }

    /// <summary>Gets the memo contents, or null when the field is not a memo.</summary>
    public byte[]? MemoBytes { get; }

    /// <summary>Gets a value indicating whether the line described a memo field.</summary>
    public bool IsMemo => MemoBytes is not null;

    /// <summary>
    /// Reads one field line of the records section.
    /// </summary>
    /// <param name="line">The line, without its newline.</param>
    /// <returns>The values the line records.</returns>
    /// <exception cref="InvalidDataException">
    /// The line does not have the shape a field line has, or carries a token this reader does not
    /// know. Both are refused rather than ignored, because a token read as absent becomes an
    /// expectation nobody asserts.
    /// </exception>
    public static DumpValue Parse(string line)
    {
        int i = 0;
        DumpEscape.SkipSpaces(line, ref i);
        string name = DumpEscape.ReadWord(line, ref i);
        DumpEscape.SkipSpaces(line, ref i);

        if (name.Length == 0)
            throw new InvalidDataException($"A field line has no field name: {line}");

        byte[] bytes;
        long? memoLength = null;
        byte[]? memoBytes = null;

        // A memo line carries the in-record reference, then the length and contents behind it.
        if (line.AsSpan(i).StartsWith("ref=", StringComparison.Ordinal))
        {
            i += 4;
            bytes = DumpEscape.ReadQuoted(line, ref i);
            DumpEscape.SkipSpaces(line, ref i);

            if (!line.AsSpan(i).StartsWith("len=", StringComparison.Ordinal))
                throw new InvalidDataException($"A memo line has no length after its reference: {line}");

            i += 4;
            memoLength = long.Parse(DumpEscape.ReadWord(line, ref i), CultureInfo.InvariantCulture);
            DumpEscape.SkipSpaces(line, ref i);
            memoBytes = DumpEscape.ReadQuoted(line, ref i);
        }
        else
        {
            bytes = DumpEscape.ReadQuoted(line, ref i);
        }

        double? number = null;
        long? integer = null;
        string? text = null;
        bool isNull = false;

        while (true)
        {
            DumpEscape.SkipSpaces(line, ref i);
            if (i >= line.Length)
                break;

            int equals = line.IndexOf('=', i);
            if (equals < 0)
                throw new InvalidDataException($"Trailing text '{line[i..]}' is not a token: {line}");

            string key = line[i..equals];
            i = equals + 1;

            switch (key)
            {
                case "dbl":
                    number = double.Parse(
                        DumpEscape.ReadWord(line, ref i), CultureInfo.InvariantCulture);
                    break;

                case "long":
                    integer = long.Parse(
                        DumpEscape.ReadWord(line, ref i), CultureInfo.InvariantCulture);
                    break;

                case "str":
                    text = ReadBracketed(line, ref i);
                    break;

                case "null":
                    isNull = DumpEscape.ReadWord(line, ref i) == "1";
                    break;

                default:
                    throw new InvalidDataException(
                        $"Token '{key}' is one this reader does not know: {line}");
            }
        }

        return new DumpValue(name, bytes, number, integer, text, isNull, memoLength, memoBytes);
    }

    /// <summary>
    /// Reads a bracketed string, which may hold spaces and so cannot be read as a word.
    /// </summary>
    private static string ReadBracketed(string line, ref int index)
    {
        if (index >= line.Length || line[index] != '[')
            throw new InvalidDataException($"Expected a bracketed value at position {index} of: {line}");

        int close = line.IndexOf(']', index);
        if (close < 0)
            throw new InvalidDataException($"A bracketed value is never closed: {line}");

        string value = line[(index + 1)..close];
        index = close + 1;
        return value;
    }
}
