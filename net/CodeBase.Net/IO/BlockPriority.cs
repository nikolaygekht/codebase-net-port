namespace CodeBase.Net.IO;

/// <summary>
/// What a cached block is worth keeping, and therefore the order blocks are retired in.
///
/// The reference keeps five separate LRU lists rather than one, and the reason is a failure a single
/// list has: **a table scan would evict the index tree**, which is exactly when a query needs it. A
/// scan touches thousands of data blocks once each, so on one list it would push out the tree it is
/// being driven by. Five lists make that impossible rather than unlikely.
///
/// The values are in the order the reference searches them for a victim — [c]prio[0][/c] first
/// ([c]D4OPT.C:324-328[/c]), which is why [c]Other[/c] sorts before data and data before index at the
/// same level, and the high lists are last resorts.
/// </summary>
internal enum BlockPriority
{
    /// <summary>
    /// Neither a table nor an index: a memo file, and anything else opened through the cache.
    /// </summary>
    /// <remarks>Retired first. The reference's [c]other[/c] list, at [c]prio[0][/c].</remarks>
    Other = 0,

    /// <summary>
    /// A table block read in the ordinary way.
    /// </summary>
    /// <remarks>The reference's [c]dbfLo[/c], at [c]prio[1][/c].</remarks>
    DataLow = 1,

    /// <summary>
    /// An index block that holds keys — a leaf.
    /// </summary>
    /// <remarks>
    /// The reference's [c]indexLo[/c], at [c]prio[2][/c]. Leaves outnumber interior nodes by the
    /// tree's fan-out, and a walk touches each of them about once, so they are the index blocks that
    /// can be spared.
    /// </remarks>
    IndexLow = 2,

    /// <summary>
    /// A table block worth more than the others, which the reference decides by hit rate.
    /// </summary>
    /// <remarks>
    /// The reference's [c]dbfHi[/c], at [c]prio[3][/c]. Nothing is admitted here yet: promoting a
    /// data block by how often it is hit is one of the heuristics this implementation leaves to the
    /// faithful one ([c]o4opt.c:1028[/c]).
    /// </remarks>
    DataHigh = 3,

    /// <summary>
    /// An index block that holds child pointers — a root or a branch.
    /// </summary>
    /// <remarks>
    /// The reference's [c]indexHi[/c], at [c]prio[4][/c], and it is retired last. Every descent in
    /// the tag passes through the root and its branches, so a few blocks carry almost all of the
    /// reuse. The reference reaches the same place by inspecting the block's bytes to see whether it
    /// is a leaf ([c]o4opt.c:1096-1120[/c]); here the caller says so instead, because it has just
    /// parsed the header and already knows.
    /// </remarks>
    IndexHigh = 4,
}
