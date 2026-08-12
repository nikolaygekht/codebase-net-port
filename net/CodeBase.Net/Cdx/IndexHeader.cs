using System.Buffers.Binary;
using System.Text;

namespace CodeBase.Net.Cdx;

/// <summary>
/// A tag header, which is also the file header when it is the tag directory's.
///
/// One thousand and twenty-four bytes: a block of fields, then a 512-byte area holding the key
/// expression and the FOR clause as text. Every number in it is little-endian except the change
/// counter, which is big-endian — endianness in this format belongs to the field, not to the file.
///
/// The header does not say what type its keys are. That matters more than it looks: the pad byte a
/// key's trail count stands for depends on the type, so a machine-collated tag has to be told
/// (ADR-26). It also does not say how deep the tree is, how many keys it holds, or which records it
/// covers.
///
/// Governing specification: CDX-FORMAT.md section 3.
/// </summary>
internal sealed class IndexHeader
{
    /// <summary>
    /// The number of bytes a header occupies, expression area included.
    /// </summary>
    public const int Size = 1024;

    /// <summary>
    /// The largest key a Visual FoxPro index can hold.
    /// </summary>
    /// <value>
    /// I4MAX_KEY_SIZE (d4defs.h:2128). Longer keys are a CodeBase extension that needs 16-bit
    /// compression counters and non-standard block sizes, and this port refuses them.
    /// </value>
    public const int MaxKeyLength = 240;

    /// <summary>The offset of the expression area within the header.</summary>
    private const int ExpressionAreaOffset = 512;

    private IndexHeader(
        uint root,
        uint freeList,
        uint version,
        int keyLength,
        byte typeCode,
        byte signature,
        BlockAddressing addressing,
        CollationName collation,
        string collationText,
        bool descending,
        byte[] expression,
        byte[] filter)
    {
        Root = root;
        FreeList = freeList;
        Version = version;
        KeyLength = keyLength;
        TypeCode = typeCode;
        Signature = signature;
        Addressing = addressing;
        Collation = collation;
        CollationText = collationText;
        Descending = descending;
        ExpressionBytes = expression;
        FilterBytes = filter;
    }

    /// <summary>Gets the node number of the tree's root block.</summary>
    public uint Root { get; }

    /// <summary>Gets the head of the file's chain of freed blocks, zero when there is none.</summary>
    /// <value>
    /// Only the tag directory's copy is maintained; a regular tag's is written once at creation and
    /// never updated (i4index.c:935). Nothing in the read path follows the chain.
    /// </value>
    public uint FreeList { get; }

    /// <summary>Gets the whole-file change counter, which readers use to detect another writer.</summary>
    /// <value>
    /// Stored big-endian, and only maintained in the tag directory's header: a regular tag's is
    /// always zero. Read and reported here, never acted on, because this port opens index files for
    /// exclusive reading.
    /// </value>
    public uint Version { get; }

    /// <summary>Gets the number of bytes in every key of this tag.</summary>
    /// <value>
    /// Fixed for the tag, and not the width of the field the key came from: a GENERAL-collated
    /// character key is twice its field's width, because it carries a block of secondary weights
    /// after the primary ones.
    /// </value>
    public int KeyLength { get; }

    /// <summary>Gets the option byte exactly as stored.</summary>
    public byte TypeCode { get; }

    /// <summary>Gets what the option byte says, named.</summary>
    public TagOptions Options => (TagOptions)TypeCode;

    /// <summary>Gets a value indicating whether this header belongs to a compound file.</summary>
    /// <value>
    /// The test the C library makes, which is "0x40 or above" rather than a single bit
    /// (i4index.c:1760). True for the tag directory of a compound file; false for a single-tag file,
    /// where this header is the one tag.
    /// </value>
    public bool IsCompound => TypeCode >= (byte)TagOptions.Compound;

    /// <summary>Gets a value indicating whether this header is the hidden tag-name tree.</summary>
    /// <value>
    /// Below 0x80 a header carries expression text and above it does not, which is the same
    /// distinction (i4init.c:420).
    /// </value>
    public bool IsTagDirectory => TypeCode >= (byte)TagOptions.TagDirectory;

    /// <summary>Gets the byte the C library declares unused, reported for diagnostics only.</summary>
    /// <value>
    /// Genuinely not reliable: within a single corpus file the tag created with the index carries
    /// 0x01 and the tag added afterwards carries 0x00. Never read it to decide anything.
    /// </value>
    public byte Signature { get; }

    /// <summary>Gets how node numbers turn into byte offsets in this file.</summary>
    public BlockAddressing Addressing { get; }

    /// <summary>Gets which collation the stored keys were built with.</summary>
    public CollationName Collation { get; }

    /// <summary>Gets the collation name as the header spells it, for messages.</summary>
    public string CollationText { get; }

    /// <summary>Gets a value indicating whether the tag is traversed from its greatest key down.</summary>
    /// <value>
    /// Keys are stored ascending either way. The flag inverts traversal and nothing else, so the
    /// stored bytes of a descending tag are the same as they would be ascending.
    /// </value>
    public bool Descending { get; }

    /// <summary>Gets the key expression as stored, without its terminating NUL.</summary>
    public byte[] ExpressionBytes { get; }

    /// <summary>Gets the FOR clause as stored, without its terminating NUL, empty when there is none.</summary>
    public byte[] FilterBytes { get; }

    /// <summary>Gets the key expression as text, decoded as ASCII with unknown bytes replaced.</summary>
    /// <value>
    /// Best effort and for humans: nothing in the read path evaluates an expression, so a header
    /// whose text this port cannot make sense of is still perfectly readable.
    /// </value>
    public string Expression => DecodeText(ExpressionBytes);

    /// <summary>Gets the FOR clause as text, empty when the tag has none.</summary>
    public string Filter => DecodeText(FilterBytes);

    /// <summary>Gets the pad byte this header settles on its own, or null when it cannot.</summary>
    /// <value>
    /// A space for the tag directory and a NUL for any collated tag. Null for a machine-collated tag,
    /// which is the case the expression engine has to answer (ADR-26, ADR-27).
    /// </value>
    public byte? PadByte =>
        IsTagDirectory ? KeyPadding.Space :
        Collation != CollationName.Machine ? KeyPadding.Nul :
        null;

    /// <summary>
    /// Reads a tag header.
    /// </summary>
    /// <param name="bytes">The header block, 1024 bytes of it.</param>
    /// <param name="what">What is being read, for the message if it cannot be.</param>
    /// <returns>The decoded header.</returns>
    /// <exception cref="CodeBaseException">
    /// The block is short, or it describes an index this library will not read: a root that is no
    /// block, an option byte below 0x20 which means uncompressed leaves, a key length outside
    /// 1 to 240, a collation whose weights are not built in, or expression text that runs past the
    /// header.
    /// </exception>
    public static IndexHeader Parse(ReadOnlySpan<byte> bytes, string what)
    {
        if (bytes.Length < Size)
        {
            throw new CodeBaseException(
                ErrorCode.Index,
                $"A tag header is {Size} bytes; only {bytes.Length} were available reading {what}.");
        }

        uint root = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        uint freeList = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        uint version = BinaryPrimitives.ReadUInt32BigEndian(bytes[8..]);
        int keyLength = BinaryPrimitives.ReadInt16LittleEndian(bytes[12..]);
        byte typeCode = bytes[14];
        byte signature = bytes[15];

        BlockAddressing addressing = BlockAddressing.Resolve(
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..]));

        (CollationName collation, string collationText) = ReadCollation(bytes.Slice(0x1EE, 8), what);

        bool descending = BinaryPrimitives.ReadInt16LittleEndian(bytes[0x1F6..]) != 0;
        int filterLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes[0x1FA..]);
        int expressionLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes[0x1FE..]);

        CheckOpenable(root, typeCode, keyLength, what);

        // Both lengths include a terminating NUL, and a header with no text at all still declares one
        // byte. The filter is only there when the option bit says so; where it is not, its declared
        // length is the one NUL and reading it would give a byte of the padding.
        byte[] expression = ReadText(bytes, ExpressionAreaOffset, expressionLength, what, "expression");
        byte[] filter = ((TagOptions)typeCode).HasFlag(TagOptions.HasFilter)
            ? ReadText(bytes, ExpressionAreaOffset + expressionLength, filterLength, what, "filter")
            : [];

        return new IndexHeader(
            root, freeList, version, keyLength, typeCode, signature,
            addressing, collation, collationText, descending, expression, filter);
    }

    private static void CheckOpenable(uint root, byte typeCode, int keyLength, string what)
    {
        // The three refusals the C library makes at open (i4index.c:1706), and for the same reasons:
        // block zero is the header, 0xFFFFFFFF means the root is unknown, and an option byte without
        // the compact bit describes uncompressed leaves this port does not read.
        if (root == 0 || root == BlockAddressing.NoNode)
        {
            throw new CodeBaseException(
                ErrorCode.Index,
                $"The header of {what} names root node {root}, which is not a block.");
        }

        if (typeCode < (byte)TagOptions.Compact)
        {
            throw new CodeBaseException(
                ErrorCode.NotSupported,
                $"The header of {what} has option byte 0x{typeCode:X2}, below the compact bit 0x20, " +
                $"so its leaves are uncompressed. That is a FoxPro 2.x index; the original CodeBase " +
                $"library refuses it too.");
        }

        if (keyLength <= 0 || keyLength > MaxKeyLength)
        {
            throw new CodeBaseException(
                keyLength <= 0 ? ErrorCode.Index : ErrorCode.NotSupported,
                $"The header of {what} declares a key length of {keyLength}; this library reads 1 to " +
                $"{MaxKeyLength}.");
        }
    }

    private static (CollationName, string) ReadCollation(ReadOnlySpan<byte> bytes, string what)
    {
        int length = bytes.IndexOf((byte)0);
        string name = Encoding.ASCII.GetString(length < 0 ? bytes : bytes[..length]);

        if (name.Length == 0)
            return (CollationName.Machine, name);

        if (name == "GENERAL")
            return (CollationName.General, name);

        // A CBnnnnn collation is CodeBase-only and its weights live in a separate file, so there is
        // no way to read one correctly and no way to test that we did.
        throw new CodeBaseException(
            ErrorCode.NotSupported,
            $"The header of {what} names collation '{name}'. This library reads machine order and " +
            $"GENERAL; a CBnnnnn collation is loaded from a file the index does not carry.");
    }

    private static byte[] ReadText(ReadOnlySpan<byte> bytes, int offset, int declaredLength, string what, string which)
    {
        if (declaredLength < 1 || offset + declaredLength > Size)
        {
            throw new CodeBaseException(
                ErrorCode.Index,
                $"The header of {what} puts {declaredLength} bytes of {which} text at offset " +
                $"{offset}, which does not fit its {Size} bytes.");
        }

        // The declared length counts the terminating NUL, so one byte means no text.
        ReadOnlySpan<byte> text = bytes.Slice(offset, declaredLength - 1);
        return text.ToArray();
    }

    // Expression text is ASCII in every file either library writes. A byte above 0x7F becomes a
    // question mark rather than an exception, because nothing in the read path depends on the text.
    private static string DecodeText(byte[] bytes) =>
        bytes.Length == 0 ? string.Empty : Encoding.ASCII.GetString(bytes);
}
