using System.Buffers.Binary;
using System.Text;

namespace CodeBase.Net.Dbf;

/// <summary>
/// One 32-byte field descriptor, exactly as the file stores it.
///
/// This is the stored view, not the engine's view. Creating a table rewrites some of what the
/// caller asked for before storing it: the binary memo type is stored under the memo letter and the
/// binary character type under the character letter, each marked binary in the flags, so the type
/// here is not always the type the table reports for the field. The null-flags field keeps the
/// mixed-case name it was written with, while ordinary names are stored upper-cased.
///
/// Nothing is interpreted or corrected here. In particular the stored record offset is reported as
/// written but is not what the engine uses, and a length is reported whether or not it is one the
/// type can have. Governing specification: DBF-FORMAT.md section 4.
/// </summary>
public readonly struct FieldDescriptor
{
    /// <summary>
    /// The number of bytes a descriptor occupies.
    /// </summary>
    public const int Size = 32;

    /// <summary>
    /// The byte that ends the descriptor region, standing where a further descriptor would begin.
    /// </summary>
    public const byte Terminator = 0x0D;

    private const int NameLength = 11;

    private FieldDescriptor(
        string name,
        char type,
        int storedOffset,
        byte length,
        byte decimals,
        FieldFlags flags,
        byte hasTag)
    {
        Name = name;
        Type = type;
        StoredOffset = storedOffset;
        Length = length;
        Decimals = decimals;
        Flags = flags;
        HasTag = hasTag;
    }

    /// <summary>
    /// Gets the field name as stored, with its case preserved.
    /// </summary>
    /// <value>
    /// Up to ten characters, ended by a zero byte. Case matters here: the engine upper-cases names
    /// when it opens a table, so the name a field reports can differ from this one, and the
    /// null-flags field is recognized by comparing these stored bytes exactly.
    /// </value>
    public string Name { get; }

    /// <summary>
    /// Gets the type letter as stored, without upper-casing it.
    /// </summary>
    /// <value>
    /// The engine upper-cases this when it opens a table, so a lower-case letter here describes the
    /// same type. Reported as stored because this is the stored view.
    /// </value>
    public char Type { get; }

    /// <summary>
    /// Gets the offset of the field within a record, as the file records it.
    /// </summary>
    /// <value>
    /// Informational only. The engine recomputes offsets by accumulating field lengths and ignores
    /// this value, because a file may disagree with itself here and the accumulated offsets are the
    /// ones that add up to the record length.
    /// </value>
    public int StoredOffset { get; }

    /// <summary>
    /// Gets the stored length byte.
    /// </summary>
    /// <value>
    /// For a character field this is only the low byte of a length that can reach 65535; the high
    /// byte is stored where other types keep their decimal count. Combining the two is the reader's
    /// job, not this view's.
    /// </value>
    public byte Length { get; }

    /// <summary>
    /// Gets the stored decimal-count byte.
    /// </summary>
    /// <value>
    /// The number of decimal places for a numeric field. For a character field it is not a decimal
    /// count at all but the high byte of the field length.
    /// </value>
    public byte Decimals { get; }

    /// <summary>
    /// Gets the properties recorded for the field.
    /// </summary>
    /// <value>Meaningful only on a Visual FoxPro table; zero on tables written before it.</value>
    public FieldFlags Flags { get; }

    /// <summary>
    /// Gets the byte marking that the field has a tag in the production index.
    /// </summary>
    /// <value>Always zero in the FoxPro build, which is the one this library implements.</value>
    public byte HasTag { get; }

    /// <summary>
    /// Reads one field descriptor.
    /// </summary>
    /// <param name="bytes">
    /// The 32 bytes of the descriptor. Anything beyond the first 32 is ignored.
    /// </param>
    /// <returns>The decoded descriptor.</returns>
    /// <exception cref="CodeBaseException">Fewer than 32 bytes were supplied.</exception>
    public static FieldDescriptor Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Size)
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"A field descriptor is {Size} bytes; only {bytes.Length} remain.");
        }

        return new FieldDescriptor(
            ReadName(bytes[..NameLength]),
            (char)bytes[11],
            BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]),
            bytes[16],
            bytes[17],
            (FieldFlags)bytes[18],
            bytes[31]);
    }

    /// <summary>
    /// Returns the descriptor rendered as its name, type and length.
    /// </summary>
    /// <returns>A short description, for diagnostics and test failure messages.</returns>
    public override string ToString() => $"{Name} {Type}({Length},{Decimals})";

    /// <summary>
    /// Reads the name, which is zero-padded and, for ordinary fields, already upper-cased.
    /// </summary>
    private static string ReadName(ReadOnlySpan<byte> bytes)
    {
        int end = bytes.IndexOf((byte)0);
        if (end < 0)
            end = bytes.Length;

        // ASCII rather than the table's code page: field names are upper-cased ASCII in every
        // in-scope case, and the code page governs record text, not the header.
        return Encoding.ASCII.GetString(bytes[..end]);
    }
}
