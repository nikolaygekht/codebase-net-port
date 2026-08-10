using System.Text;
using CodeBase.Net.Dbf;
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
    internal CodeBaseEngine(IRandomAccessSourceFactory factory, ICompanionFileResolver companions) =>
        opener = new DbfOpener(factory, companions);

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

        Table table = new(path, opener.Open(path), DefaultEncoding, Forget);
        open.Add(table);
        return table;
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
