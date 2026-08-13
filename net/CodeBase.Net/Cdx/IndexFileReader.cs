using System.Text;
using CodeBase.Net.IO;

namespace CodeBase.Net.Cdx;

/// <summary>
/// An open index file and the tags in it.
///
/// The header at offset zero decides the file's shape, once, at open. From 0x40 up it is a tag
/// directory: a hidden tree whose keys are tag names and whose record numbers are the node of each
/// tag's own header. Below that the header is the one tag, and the file's name supplies its name
/// because nothing in the file does.
///
/// Reading the directory uses the same machinery as reading any tag, which is worth having: a leaf
/// decoding bug cannot wait until the first real tag is walked, because opening the file already
/// depends on it.
///
/// Governing specification: CDX-FORMAT.md sections 2 and 2.1.
/// </summary>
internal sealed class IndexFileReader : IDisposable
{
    private readonly IRandomAccessSource source;

    private IndexFileReader(
        IRandomAccessSource source,
        string name,
        IndexHeader header,
        CdxTag? directory,
        IReadOnlyList<CdxTag> tags)
    {
        this.source = source;
        Name = name;
        Header = header;
        Directory = directory;
        Tags = tags;
    }

    /// <summary>Gets the file's name without path or extension, upper-cased.</summary>
    public string Name { get; }

    /// <summary>Gets the header at offset zero: the tag directory's, or the single tag's.</summary>
    public IndexHeader Header { get; }

    /// <summary>Gets a value indicating whether the file holds a tag directory.</summary>
    public bool IsCompound => Header.IsCompound;

    /// <summary>Gets the hidden tag-name tree as the tag it is, or null in a single-tag file.</summary>
    /// <value>
    /// Exposed because it is read exactly like any other tag and is worth being able to check that
    /// way. Its keys are the tag names and its record numbers are the node of each tag's header.
    /// </value>
    public CdxTag? Directory { get; }

    /// <summary>Gets the tags, in the order the file lists them.</summary>
    /// <value>
    /// A compound file lists them in key order, which is the directory's order and therefore
    /// alphabetical. A single-tag file has exactly one.
    /// </value>
    public IReadOnlyList<CdxTag> Tags { get; }

    /// <summary>
    /// Opens an index file.
    /// </summary>
    /// <param name="source">The file, which this reader takes ownership of.</param>
    /// <param name="fileName">
    /// The file's name, with or without a path. Its stem names the tag of a single-tag file, so it is
    /// not decoration.
    /// </param>
    /// <param name="padByteFor">
    /// Supplies the pad byte for a machine-collated tag, which the file does not record. Called once
    /// per such tag, when that tag is first used, and not called at all for a collated one.
    /// </param>
    /// <returns>The open file and its tags.</returns>
    /// <exception cref="CodeBaseException">
    /// The file's header describes an index this library will not read, or the directory contradicts
    /// itself.
    /// </exception>
    public static IndexFileReader Open(IRandomAccessSource source, string fileName, PadByteResolver padByteFor)
    {
        string name = Path.GetFileNameWithoutExtension(fileName).ToUpperInvariant();

        try
        {
            IndexHeader header = IndexHeader.Parse(
                source.ReadExactly(0, IndexHeader.Size, $"the header of {fileName}", ErrorCode.Index),
                fileName);

            NodeReader nodes = new(source, header.Addressing);

            if (!header.IsCompound)
            {
                CdxTag only = new(name, header, padByteFor, nodes);
                return new IndexFileReader(source, name, header, null, [only]);
            }

            // The directory is a tag whose keys are names and whose pad byte is a space, which the
            // file does not have to say because the C library hard-codes it (i4init.c:520-525). Its
            // header answers for itself, so the resolver is never reached for it.
            CdxTag directory = new("*directory*", header, padByteFor, nodes);

            return new IndexFileReader(
                source, name, header, directory,
                ReadDirectory(directory, nodes, fileName, padByteFor));
        }
        catch
        {
            // A caller that gets an exception should not also get an open file handle to clean up.
            source.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gives the tag of a given name.
    /// </summary>
    /// <param name="name">The tag's name, matched without regard to case.</param>
    /// <returns>The tag.</returns>
    /// <exception cref="CodeBaseException">The file has no tag of that name.</exception>
    public CdxTag Tag(string name)
    {
        foreach (CdxTag tag in Tags)
        {
            if (string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase))
                return tag;
        }

        throw new CodeBaseException(
            ErrorCode.Index,
            $"Index file {Name} has no tag named '{name}'. It holds: " +
            string.Join(", ", Tags.Select(t => t.Name)) + ".");
    }

    /// <summary>
    /// Closes the index file.
    /// </summary>
    public void Dispose() => source.Dispose();

    /// <summary>
    /// Walks the tag directory and opens the tag each entry names.
    /// </summary>
    private static IReadOnlyList<CdxTag> ReadDirectory(
        CdxTag directory,
        NodeReader nodes,
        string fileName,
        PadByteResolver padByteFor)
    {
        List<CdxTag> tags = [];

        TagCursor cursor = directory.OpenCursor();
        for (bool any = cursor.Top(); any; any = cursor.Next())
        {
            IndexEntry entry = cursor.Current;
            string name = Encoding.ASCII.GetString(entry.Key).TrimEnd(' ');

            if (name.Length == 0)
            {
                throw new CodeBaseException(
                    ErrorCode.Index,
                    $"The tag directory of {fileName} holds an entry with no name, pointing at node " +
                    $"{entry.Record}.");
            }

            // The entry's "record number" is the node of the tag's own 1024-byte header. Reading it
            // through the same parse as the file header is what makes a tag a tag.
            IndexHeader tagHeader = IndexHeader.Parse(
                nodes.ReadHeader(entry.Record),
                $"tag {name} of {fileName}");

            tags.Add(new CdxTag(name, tagHeader, padByteFor, nodes));
        }

        if (tags.Count == 0)
        {
            throw new CodeBaseException(
                ErrorCode.Index,
                $"The tag directory of {fileName} is empty, so the file describes no tags.");
        }

        return tags;
    }
}
