using System.Text;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using CodeBase.Net.IO;

namespace CodeBase.Net;

/// <summary>
/// The entry point: opens tables and owns them until it is closed.
///
/// An engine owns every table it opens, so closing the engine closes them all. A caller that keeps
/// one in a using statement cannot leak a file handle by forgetting a table, and one that wants a
/// table closed sooner can close it directly.
/// </summary>
public sealed class CodeBaseEngine : IDisposable
{
    private readonly DbfOpener opener;
    private readonly CachingSourceFactory sources;
    private readonly List<Table> open = [];
    private bool closed;

    /// <summary>
    /// Initializes a new engine reading the real filesystem.
    /// </summary>
    public CodeBaseEngine()
        : this(FileSystem.Instance, FileSystem.Instance)
    {
    }

    /// <summary>
    /// Initializes a new engine reading through the given boundaries, for tests.
    /// </summary>
    internal CodeBaseEngine(IRandomAccessSourceFactory factory, ICompanionFileResolver companions)
    {
        sources = new CachingSourceFactory(factory);
        opener = new DbfOpener(sources, companions);
    }

    /// <summary>
    /// Gets or sets the encoding used for tables that name no code page of their own.
    /// </summary>
    /// <value>
    /// Applies to a table whose language-driver byte is absent or names a code page this library
    /// does not know, which are the two cases where the file itself does not say. A table that does
    /// name one is always read with that, whatever this is set to.
    ///
    /// Null, the default, means code page 437, which is how the C library reads an unmarked table.
    /// Setting this to an encoding you already hold also avoids needing an encoding provider
    /// registered for those tables. See ADR-17.
    /// </value>
    public Encoding? DefaultEncoding { get; set; }

    /// <summary>
    /// Gets or sets the date picture and century policy expressions are read under.
    /// </summary>
    /// <value>
    /// The reference implementation takes both implicitly — the picture from a compile-time switch
    /// and the century from a read of the system clock — so a two-digit year meant one thing in 1999
    /// and another now. This library makes them a choice, and the default infers no century at all,
    /// which means a tag whose expression reads a two-digit year is refused rather than keyed
    /// differently on a different machine. Set this to reproduce a particular build's answers. See
    /// ADR-42.
    /// </value>
    public ExpressionOptions ExpressionOptions { get; set; } = ExpressionOptions.Invariant;

    /// <summary>
    /// Gets or sets which files may have their blocks kept in memory between reads.
    /// </summary>
    /// <value>
    /// [c]BlockCacheMode.WhenExclusive[/c] by default, which is the reference's own shipped value and
    /// **caches nothing today**, because this library opens every file shared and cannot yet exclude a
    /// writer. Set it to [c]BlockCacheMode.Always[/c] when you know nothing else is writing the files,
    /// and read what that costs on the mode itself before you do.
    ///
    /// The decision is taken per file when the file is opened, so this affects tables opened
    /// afterwards and not tables already open.
    /// </value>
    public BlockCacheMode BlockCache
    {
        get => sources.Mode;
        set => sources.Mode = value;
    }

    /// <summary>
    /// Gets or sets how many blocks the cache may hold, zero meaning no cache at all.
    /// </summary>
    /// <value>
    /// 2048 by default, which is a megabyte of 512-byte blocks and holds a small table with its index
    /// whole. What the right default is across table sizes is a measurement this library has not taken;
    /// nothing is allocated until a file qualifies for caching, so the value costs nothing until it is
    /// used.
    /// </value>
    public int BlockCacheBlocks
    {
        get => sources.Windows;
        set => sources.Windows = value;
    }

    /// <summary>
    /// Gets the tables this engine currently has open.
    /// </summary>
    public IReadOnlyList<Table> OpenTables => open;

    /// <summary>
    /// Opens a table, and the memo file accompanying it if its header declares one.
    /// </summary>
    /// <param name="path">The table file.</param>
    /// <returns>The open table, which this engine also owns.</returns>
    /// <exception cref="CodeBaseException">
    /// The file contradicts itself or its own length, names a field type this library cannot read,
    /// or declares a memo file that is not beside it.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The engine has been closed.</exception>
    public Table OpenTable(string path)
    {
        ObjectDisposedException.ThrowIf(closed, this);

        Table table = new(path, opener.Open(path), DefaultEncoding, ExpressionOptions, Forget);
        open.Add(table);
        return table;
    }

    /// <summary>
    /// Forgets every block this engine has cached, for every table it has open.
    /// </summary>
    /// <returns>How many blocks were given up.</returns>
    /// <exception cref="ObjectDisposedException">The engine has been closed.</exception>
    /// <remarks>
    /// [c]code4optSuspend[/c]. What makes [c]BlockCacheMode.Always[/c] a door rather than a one-way
    /// door: a reader who knows the files may have changed under it discards what the cache believes
    /// and reads them again. It costs the next read of each block, and nothing else.
    ///
    /// This is not a substitute for locking. It says "forget what you saw", not "you are now current":
    /// another process can write between this call and the next read, and until locking exists there is
    /// nothing that can promise otherwise.
    /// </remarks>
    public int DropCachedBlocks()
    {
        ObjectDisposedException.ThrowIf(closed, this);

        return sources.InvalidateAll();
    }

    /// <summary>
    /// Closes the engine and every table it still has open.
    /// </summary>
    public void Dispose()
    {
        if (closed)
            return;

        closed = true;

        // Over a copy: closing a table asks the engine to forget it, which changes the list.
        foreach (Table table in open.ToArray())
            table.Dispose();

        open.Clear();
    }

    /// <summary>
    /// Stops tracking a table that has closed itself.
    /// </summary>
    private void Forget(Table table) => open.Remove(table);
}
