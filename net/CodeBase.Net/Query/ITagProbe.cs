using CodeBase.Net.Cdx;

namespace CodeBase.Net.Query;

/// <summary>
/// One tag's keys, in stored order, seekable and steppable.
///
/// This is the only place the optimizer touches a file, and it presents the keys in the order they
/// are [b]stored[/b] rather than the order the tag is walked in. The two differ for a descending tag,
/// whose keys are stored ascending like everyone else's and whose flag only inverts traversal -- and
/// a constraint is a statement about values, so the ordering it needs is the values'. Mapping the one
/// onto the other belongs here and nowhere above.
///
/// Narrow on purpose: four movements and two questions, which is little enough to stand up in memory
/// over a sorted list of keys. That is what lets the whole of leaf evaluation -- the seek positions,
/// the range walk, the exclusions, the empty range -- be tested without an index file.
/// </summary>
internal interface ITagProbe
{
    /// <summary>Gets how many bytes one of the tag's keys occupies.</summary>
    int KeyLength { get; }

    /// <summary>Gets the entry the probe is on, which is only valid after a movement returned true.</summary>
    IndexEntry Current { get; }

    /// <summary>
    /// Turns a constraint's value into something the tag's keys can be compared against.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The comparand.</returns>
    /// <value>
    /// A value shorter than the tag's keys stays short, and comparing against it then compares only
    /// its own bytes -- which is what makes a short character constant name a group of keys rather
    /// than one.
    /// </value>
    KeySearch KeyFor(ConstraintValue value);

    /// <summary>
    /// Moves to the lowest key the tag holds.
    /// </summary>
    /// <returns>True unless the tag is empty.</returns>
    bool First();

    /// <summary>
    /// Moves to the first key at or after a value.
    /// </summary>
    /// <param name="key">The value to reach.</param>
    /// <returns>True unless every key sorts before it.</returns>
    bool SeekAtOrAfter(KeySearch key);

    /// <summary>
    /// Moves to the next key up.
    /// </summary>
    /// <returns>True unless the probe was on the highest key.</returns>
    bool Step();
}
