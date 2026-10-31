using System.Text;

namespace CodeBase.Net.Expressions;

/// <summary>
/// Changes the case of a table's text by the table's own code page, keeping every value's length in bytes.
///
/// UPPER and LOWER decode a character, change its case with Unicode's invariant mapping, and encode
/// it back into the same code page. The result is kept only where it is a character of that code page
/// taking exactly as many bytes as the one it replaces; anywhere else the character is left as it
/// stands, because a value's width is fixed and a key built from it has to be. The reference maps
/// through the host's ANSI code page or ASCII, Visual FoxPro through the host's; this is the mapping
/// that does not depend on the machine (ADR-65).
///
/// A single-byte code page gets a 256-entry table for each direction. A multi-byte one gets the
/// mapping of each character it can encode, and a value is walked a whole character at a time, so a
/// trail byte that happens to equal an ASCII letter is never changed on its own. Both are built from
/// the encoding on first use and are immutable afterwards.
/// </summary>
internal sealed class CaseMap
{
    private readonly Func<Encoding>? source;
    private Built? built;

    private CaseMap(Func<Encoding>? source)
    {
        this.source = source;
    }

    /// <summary>
    /// Gets the map that changes the ASCII letters and nothing else.
    /// </summary>
    /// <value>
    /// What an unmarked table gets when the host has named no encoding for it, which is what CodeBase
    /// does for one (c4upper); a-z are the same letters in every ASCII-compatible code page.
    /// </value>
    public static CaseMap Ascii { get; } = new(null);

    /// <summary>
    /// Gets the map for an encoding, built when it is first used.
    /// </summary>
    /// <param name="encoding">Supplies the encoding; called once, on the first use.</param>
    /// <returns>The map.</returns>
    public static CaseMap For(Func<Encoding> encoding) => new(encoding);

    /// <summary>
    /// Writes the upper case of a value.
    /// </summary>
    /// <param name="value">The stored bytes.</param>
    /// <param name="destination">Where to write, at least as long as the value.</param>
    public void ToUpper(ReadOnlySpan<byte> value, Span<byte> destination) => Map(value, destination, upper: true);

    /// <summary>
    /// Writes the lower case of a value.
    /// </summary>
    /// <param name="value">The stored bytes.</param>
    /// <param name="destination">Where to write, at least as long as the value.</param>
    public void ToLower(ReadOnlySpan<byte> value, Span<byte> destination) => Map(value, destination, upper: false);

    private void Map(ReadOnlySpan<byte> value, Span<byte> destination, bool upper)
    {
        if (source is null)
        {
            MapAscii(value, destination, upper);
            return;
        }

        Built tables = LazyInitializer.EnsureInitialized(ref built, () => Built.From(source()));

        if (tables.Bytes is { } bytes)
        {
            byte[] table = upper ? bytes.Upper : bytes.Lower;
            for (int i = 0; i < value.Length; i++)
                destination[i] = table[value[i]];
        }
        else
        {
            var characters = tables.Characters!.Value;
            MapCharacters(value, destination, tables, upper ? characters.Upper : characters.Lower);
        }
    }

    private static void MapAscii(ReadOnlySpan<byte> value, Span<byte> destination, bool upper)
    {
        for (int i = 0; i < value.Length; i++)
        {
            byte b = value[i];
            destination[i] = upper
                ? b is >= (byte)'a' and <= (byte)'z' ? (byte)(b - 32) : b
                : b is >= (byte)'A' and <= (byte)'Z' ? (byte)(b + 32) : b;
        }
    }

    /// <summary>
    /// Walks a multi-byte value a character at a time and replaces each character that has a mapping.
    /// </summary>
    /// <value>
    /// The decoder is fed one byte at a time, so the bytes of each character are known exactly; a
    /// byte that completes no character, or decodes to anything but one mapped character, is copied
    /// as it stands. That keeps a lone lead byte at the end of a cut field, and any byte the code
    /// page does not define, exactly as they were.
    /// </value>
    private static void MapCharacters(
        ReadOnlySpan<byte> value, Span<byte> destination, Built tables, Dictionary<char, byte[]> mapping)
    {
        value.CopyTo(destination);

        Decoder decoder = tables.Decoding.GetDecoder();
        Span<char> decoded = stackalloc char[4];
        int start = 0;

        for (int i = 0; i < value.Length; i++)
        {
            int count = decoder.GetChars(value.Slice(i, 1), decoded, flush: false);
            if (count == 0)
                continue;

            int length = i + 1 - start;
            if (count == 1 && mapping.TryGetValue(decoded[0], out byte[]? mapped) && mapped.Length == length)
                mapped.CopyTo(destination[start..]);

            start = i + 1;
        }
    }

    /// <summary>
    /// The tables built from one encoding: one pair of byte tables, or one pair of character maps.
    /// </summary>
    private sealed class Built
    {
        private Built(Encoding decoding, (byte[] Upper, byte[] Lower)? bytes, (Dictionary<char, byte[]> Upper, Dictionary<char, byte[]> Lower)? characters)
        {
            Decoding = decoding;
            Bytes = bytes;
            Characters = characters;
        }

        /// <summary>Gets the table's encoding with a replacement fallback, for walking a value without throwing.</summary>
        public Encoding Decoding { get; }

        public (byte[] Upper, byte[] Lower)? Bytes { get; }

        public (Dictionary<char, byte[]> Upper, Dictionary<char, byte[]> Lower)? Characters { get; }

        public static Built From(Encoding encoding)
        {
            Codec codec = new(encoding);

            if (encoding.IsSingleByte)
                return new Built(codec.Relaxed, (ByteTable(codec, upper: true), ByteTable(codec, upper: false)), null);

            return new Built(codec.Relaxed, null, (CharacterMap(codec, upper: true), CharacterMap(codec, upper: false)));
        }

        private static byte[] ByteTable(Codec codec, bool upper)
        {
            byte[] table = new byte[256];

            for (int b = 0; b < 256; b++)
            {
                table[b] = (byte)b;

                if (codec.Decode([(byte)b]) is not { Length: 1 } text)
                    continue;

                if (codec.Mapped(text[0], upper, length: 1) is { } mapped)
                    table[b] = mapped[0];
            }

            return table;
        }

        /// <summary>
        /// Maps every character the code page can encode and whose case changes.
        /// </summary>
        /// <value>
        /// Only the characters whose invariant case differs are tried, which is about fourteen hundred
        /// of the basic plane rather than all of it.
        /// </value>
        private static Dictionary<char, byte[]> CharacterMap(Codec codec, bool upper)
        {
            Dictionary<char, byte[]> map = [];

            for (int code = 0; code <= char.MaxValue; code++)
            {
                char c = (char)code;
                if (char.IsSurrogate(c) || Changed(c, upper) == c)
                    continue;

                if (codec.Encode(c) is not { } own)
                    continue;

                if (codec.Mapped(c, upper, own.Length) is { } mapped)
                    map[c] = mapped;
            }

            return map;
        }

        private static char Changed(char c, bool upper) =>
            upper ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c);

        /// <summary>
        /// Encodes and decodes one character at a time, reporting what does not survive rather than throwing.
        /// </summary>
        /// <value>
        /// A replacement fallback on both sides, whatever the encoding was configured with, so that an
        /// unencodable character costs a comparison and not an exception — there are hundreds of them
        /// to try in a multi-byte code page. A character counts as encoded only if its bytes decode
        /// back to it.
        /// </value>
        private sealed class Codec
        {
            private readonly Encoder encoder;
            private readonly Decoder decoder;

            public Codec(Encoding encoding)
            {
                // A writable copy, because the table's own encoding throws on what it cannot map and
                // an encoder's own fallback does not override that for every implementation.
                Relaxed = (Encoding)encoding.Clone();
                Relaxed.EncoderFallback = EncoderFallback.ReplacementFallback;
                Relaxed.DecoderFallback = DecoderFallback.ReplacementFallback;

                encoder = Relaxed.GetEncoder();
                decoder = Relaxed.GetDecoder();
            }

            /// <summary>Gets the encoding with replacement fallbacks on both sides.</summary>
            public Encoding Relaxed { get; }

            public string? Decode(ReadOnlySpan<byte> bytes)
            {
                decoder.Reset();
                Span<char> chars = stackalloc char[8];
                int count = decoder.GetChars(bytes, chars, flush: true);
                return count == 0 || chars[..count].Contains('�') ? null : new string(chars[..count]);
            }

            public byte[]? Encode(char c)
            {
                encoder.Reset();
                Span<byte> bytes = stackalloc byte[8];
                int count = encoder.GetBytes([c], bytes, flush: true);
                byte[] encoded = bytes[..count].ToArray();

                return Decode(encoded) is { Length: 1 } back && back[0] == c ? encoded : null;
            }

            /// <summary>
            /// Gets the bytes of a character's other case, when they exist and take the given length.
            /// </summary>
            public byte[]? Mapped(char c, bool upper, int length)
            {
                char changed = Changed(c, upper);
                if (changed == c)
                    return null;

                return Encode(changed) is { } encoded && encoded.Length == length ? encoded : null;
            }
        }
    }
}
