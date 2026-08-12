using System.Buffers.Binary;
using System.Text;
using CodeBase.Net.Cdx;

namespace CodeBase.Net.Tests.Cdx;

/// <summary>
/// Builds index files in memory, for the shapes the corpus does not hold.
///
/// Hand-built bytes are legitimate test [i]input[/i]: a block whose counts contradict each other, a
/// leaf attribute Visual FoxPro 8 writes, an entry packed six bytes wide. What a real key decodes
/// [i]to[/i] still comes from the corpus, and nothing here is ever used as an expectation. See
/// DEV_APPROACH.md section 4.
///
/// The builder writes the layout the specification describes rather than the layout the reader
/// happens to implement, so a disagreement between the two shows up as a failing test rather than as
/// two mistakes cancelling out.
/// </summary>
internal sealed class IndexImage
{
    /// <summary>The block size every image uses, which is the one Visual FoxPro uses.</summary>
    public const int BlockSize = 512;

    /// <summary>The offset of the first tree block, past the 1024-byte header.</summary>
    public const int FirstBlock = 1024;

    private readonly List<byte[]> blocks = [];
    private readonly byte[] header = new byte[IndexHeader.Size];

    private IndexImage()
    {
    }

    /// <summary>
    /// Starts an image whose header describes one tag, with no tag directory.
    /// </summary>
    /// <param name="keyLength">The tag's key length.</param>
    /// <param name="root">The node of the tree's root block.</param>
    /// <param name="typeCode">The option byte, 0x20 for a plain single-tag file.</param>
    /// <param name="expression">The key expression's text.</param>
    /// <param name="descending">Whether the tag is traversed downwards.</param>
    /// <param name="collation">The collation name, empty for machine order.</param>
    /// <returns>The image under construction.</returns>
    public static IndexImage SingleTag(
        int keyLength,
        uint root,
        byte typeCode = 0x20,
        string expression = "F",
        bool descending = false,
        string collation = "")
    {
        IndexImage image = new();
        Span<byte> h = image.header;

        BinaryPrimitives.WriteUInt32LittleEndian(h, root);
        BinaryPrimitives.WriteUInt32LittleEndian(h[4..], 0);
        BinaryPrimitives.WriteUInt32BigEndian(h[8..], 0);
        BinaryPrimitives.WriteInt16LittleEndian(h[12..], (short)keyLength);
        h[14] = typeCode;
        h[15] = 0x01;

        Encoding.ASCII.GetBytes(collation).CopyTo(h[0x1EE..]);
        BinaryPrimitives.WriteInt16LittleEndian(h[0x1F6..], (short)(descending ? 1 : 0));
        BinaryPrimitives.WriteUInt16LittleEndian(h[0x1FA..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(h[0x1FE..], (ushort)(expression.Length + 1));
        Encoding.ASCII.GetBytes(expression).CopyTo(h[512..]);

        return image;
    }

    /// <summary>
    /// Overwrites one byte of the header, for the cases that need a contradiction in it.
    /// </summary>
    /// <param name="offset">Where in the header.</param>
    /// <param name="value">What to put there.</param>
    /// <returns>The image, for chaining.</returns>
    public IndexImage WithHeaderByte(int offset, byte value)
    {
        header[offset] = value;
        return this;
    }

    /// <summary>
    /// Overwrites two bytes of the header as a little-endian number.
    /// </summary>
    /// <param name="offset">Where in the header.</param>
    /// <param name="value">What to put there.</param>
    /// <returns>The image, for chaining.</returns>
    public IndexImage WithHeaderShort(int offset, short value)
    {
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(offset), value);
        return this;
    }

    /// <summary>
    /// Appends a leaf block holding the given keys, packed as the format packs them.
    /// </summary>
    /// <param name="keyLength">The key length, which every key must be.</param>
    /// <param name="keys">The keys and their record numbers, in order.</param>
    /// <param name="padByte">The byte trailing pad is counted in.</param>
    /// <param name="attribute">The attribute word, 2 for a plain leaf and 3 for a root leaf.</param>
    /// <param name="left">The left sibling's node.</param>
    /// <param name="right">The right sibling's node.</param>
    /// <returns>The image, for chaining.</returns>
    public IndexImage WithLeaf(
        int keyLength,
        (string Key, uint Record)[] keys,
        byte padByte = 0x20,
        int attribute = 3,
        uint left = 0xFFFFFFFF,
        uint right = 0xFFFFFFFF)
    {
        byte[] block = new byte[BlockSize];
        Span<byte> span = block;

        // Widths wide enough for anything a test builds, and consistent with each other, which is
        // what the reader insists on: eight bits of record number and eight of each count.
        const int RecordBits = 16;
        const int CountBits = 8;
        const int InfoLength = 4;

        BinaryPrimitives.WriteInt16LittleEndian(span, (short)attribute);
        BinaryPrimitives.WriteInt16LittleEndian(span[2..], (short)keys.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], left);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], right);

        BinaryPrimitives.WriteUInt32LittleEndian(span[14..], 0xFFFF);
        span[18] = 0xFF;
        span[19] = 0xFF;
        span[20] = RecordBits;
        span[21] = CountBits;
        span[22] = CountBits;
        span[23] = InfoLength;

        int textEnd = BlockSize;
        string previous = string.Empty;

        for (int i = 0; i < keys.Length; i++)
        {
            byte[] key = Padded(keys[i].Key, keyLength, padByte);
            int trail = TrailCount(key, padByte);
            int dup = i == 0 || trail == keyLength ? 0 : SharedPrefix(previous, key, keyLength - trail);
            int stored = keyLength - dup - trail;

            textEnd -= stored;
            key.AsSpan(dup, stored).CopyTo(span[textEnd..]);

            ulong packed = keys[i].Record
                | ((ulong)(uint)dup << RecordBits)
                | ((ulong)(uint)trail << (RecordBits + CountBits));

            for (int b = 0; b < InfoLength; b++)
                span[LeafGeometry.InfoArrayOffset + (i * InfoLength) + b] = (byte)(packed >> (b * 8));

            previous = Encoding.Latin1.GetString(key);
        }

        BinaryPrimitives.WriteInt16LittleEndian(
            span[12..],
            (short)(textEnd - LeafGeometry.InfoArrayOffset - (keys.Length * InfoLength)));

        blocks.Add(block);
        return this;
    }

    /// <summary>
    /// Appends an interior block pointing at the given children.
    /// </summary>
    /// <param name="keyLength">The key length.</param>
    /// <param name="entries">The greatest key of each child, its record, and the child's node.</param>
    /// <param name="attribute">The attribute word, 1 for a root and 0 for a level in between.</param>
    /// <returns>The image, for chaining.</returns>
    public IndexImage WithBranch(int keyLength, (string Key, uint Record, uint Child)[] entries, int attribute = 1)
    {
        byte[] block = new byte[BlockSize];
        Span<byte> span = block;

        BinaryPrimitives.WriteInt16LittleEndian(span, (short)attribute);
        BinaryPrimitives.WriteInt16LittleEndian(span[2..], (short)entries.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 0xFFFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], 0xFFFFFFFF);

        int entrySize = keyLength + 8;
        for (int i = 0; i < entries.Length; i++)
        {
            int at = NodeHeader.Size + (i * entrySize);
            Padded(entries[i].Key, keyLength, 0x20).CopyTo(span[at..]);

            // Both numbers big-endian, which is the whole point of an interior node.
            BinaryPrimitives.WriteUInt32BigEndian(span[(at + keyLength)..], entries[i].Record);
            BinaryPrimitives.WriteUInt32BigEndian(span[(at + keyLength + 4)..], entries[i].Child);
        }

        blocks.Add(block);
        return this;
    }

    /// <summary>
    /// Appends a block of raw bytes, for images that need one built by hand.
    /// </summary>
    /// <param name="block">The block, padded or truncated to the block size.</param>
    /// <returns>The image, for chaining.</returns>
    public IndexImage WithRawBlock(byte[] block)
    {
        byte[] padded = new byte[BlockSize];
        block.AsSpan(0, Math.Min(block.Length, BlockSize)).CopyTo(padded);
        blocks.Add(padded);

        return this;
    }

    /// <summary>
    /// Gives the node number a block appended in a given order sits at.
    /// </summary>
    /// <param name="index">Which block, counting from zero.</param>
    /// <returns>Its node number, which with a multiplier of one is its offset.</returns>
    public static uint NodeOf(int index) => (uint)(FirstBlock + (index * BlockSize));

    /// <summary>
    /// Assembles the file.
    /// </summary>
    /// <returns>The whole image, header first.</returns>
    public byte[] Build()
    {
        byte[] image = new byte[IndexHeader.Size + (blocks.Count * BlockSize)];
        header.CopyTo(image, 0);

        for (int i = 0; i < blocks.Count; i++)
            blocks[i].CopyTo(image, IndexHeader.Size + (i * BlockSize));

        return image;
    }

    private static byte[] Padded(string text, int keyLength, byte padByte)
    {
        byte[] key = new byte[keyLength];
        key.AsSpan().Fill(padByte);
        Encoding.Latin1.GetBytes(text).AsSpan(0, Math.Min(text.Length, keyLength)).CopyTo(key);

        return key;
    }

    private static int TrailCount(byte[] key, byte padByte)
    {
        int trail = 0;
        while (trail < key.Length && key[key.Length - 1 - trail] == padByte)
            trail++;

        return trail;
    }

    private static int SharedPrefix(string previous, byte[] key, int limit)
    {
        byte[] before = Encoding.Latin1.GetBytes(previous);
        int shared = 0;

        while (shared < limit && shared < before.Length && before[shared] == key[shared])
            shared++;

        return shared;
    }
}
