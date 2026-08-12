using System.Collections;

namespace CodeBase.Net;

/// <summary>
/// A table's index tags, in the order the index file lists them, addressable by position or by name.
///
/// Empty when the table declares no production index. The shape mirrors [c]FieldCollection[/c] so that
/// the two feel the same to use, including matching names without regard to case: the engine upper-cases
/// a tag's name when it opens the index, and a caller should not have to know that.
///
/// A compound index lists its tags in the order of its hidden tag-name tree, which is alphabetical.
/// </summary>
public sealed class TagCollection : IReadOnlyList<Tag>
{
    private readonly IReadOnlyList<Tag> tags;
    private readonly Dictionary<string, Tag> byName;

    internal TagCollection(IReadOnlyList<Tag> tags)
    {
        this.tags = tags;
        byName = new Dictionary<string, Tag>(StringComparer.OrdinalIgnoreCase);

        foreach (Tag tag in tags)
            byName.TryAdd(tag.Name, tag);
    }

    /// <summary>
    /// Gets the number of tags.
    /// </summary>
    public int Count => tags.Count;

    /// <summary>
    /// Gets a tag by its position, counting from zero.
    /// </summary>
    /// <param name="index">The position.</param>
    public Tag this[int index] => tags[index];

    /// <summary>
    /// Gets a tag by name.
    /// </summary>
    /// <param name="name">The tag's name, matched without regard to case.</param>
    /// <exception cref="CodeBaseException">
    /// The table has no tag of that name. The message lists the ones it does have, because that is what
    /// a caller needs to see next.
    /// </exception>
    public Tag this[string name] =>
        byName.TryGetValue(name, out Tag? tag)
            ? tag
            : throw new CodeBaseException(
                ErrorCode.Info,
                Count == 0
                    ? $"There is no tag named '{name}': this table has no index."
                    : $"There is no tag named '{name}'. The table has: {string.Join(", ", tags.Select(t => t.Name))}.");

    /// <summary>
    /// Looks a tag up by name without throwing when it is absent.
    /// </summary>
    /// <param name="name">The tag's name, matched without regard to case.</param>
    /// <param name="tag">The tag, or null.</param>
    /// <returns>Whether the table has a tag of that name.</returns>
    public bool TryGet(string name, out Tag? tag) => byName.TryGetValue(name, out tag);

    /// <summary>
    /// Returns an enumerator over the tags, in the order the index lists them.
    /// </summary>
    /// <returns>The enumerator.</returns>
    public IEnumerator<Tag> GetEnumerator() => tags.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
