using System.Globalization;
using System.Text;
using CodeBase.Net.TestUtils;

namespace CodeBase.Net.Golden;

/// <summary>
/// The expression oracle, read back out of the dump the generator took.
///
/// Every expression in it was put through the reference implementation's own parser, and what came
/// back was recorded: the compiled node array, the value, or the code it was refused with. The
/// sections are not interchangeable, which is why they are kept apart here rather than concatenated:
/// the clock-dependent ones have no recorded value at all, the refusals are refusals, and the
/// last section is a list of names the reference refuses that this port deliberately accepts.
///
/// This reader takes the expression texts, the compiled shape of each one, and the refusals, which is
/// what the scanner and the parser are gated on. The result types, the lengths and the values are
/// read by the passes that are gated on them.
///
/// An unknown section is refused rather than skipped, so that a dump that grows one cannot leave a
/// golden test quietly comparing an empty list.
/// </summary>
internal sealed class CorpusExpressionDump
{
    private const string FileName = "CDXEXPR.expr.dump.txt";

    private static readonly string[] KnownSections =
    [
        "[constant expressions]",
        "[field expressions]",
        "[clock-dependent expressions]",
        "[rejections]",
        "[vfp-only]",
        "[tag context]",
    ];

    private CorpusExpressionDump(
        string datePicture,
        int century,
        IReadOnlyList<DumpExpression> constantExpressions,
        IReadOnlyList<DumpExpression> fieldExpressions,
        IReadOnlyList<DumpExpression> clockDependentExpressions,
        IReadOnlyList<DumpRejection> rejections,
        IReadOnlyList<DumpRejection> visualFoxProOnly,
        IReadOnlyList<DumpTagExpression> tagContext)
    {
        DatePicture = datePicture;
        Century = century;
        ConstantExpressions = constantExpressions;
        FieldExpressions = fieldExpressions;
        ClockDependentExpressions = clockDependentExpressions;
        Rejections = rejections;
        VisualFoxProOnly = visualFoxProOnly;
        TagContextExpressions = tagContext;
    }

    /// <summary>Gets the date picture the reference was built with, from the dump's header.</summary>
    /// <value>
    /// Read rather than assumed, because it decides what a date read out of text means and how long
    /// a formatted one is. The generator records it for exactly that reason.
    /// </value>
    public string DatePicture { get; }

    /// <summary>Gets the century the reference's clock was in when the dump was taken.</summary>
    /// <value>
    /// Every date the reference read from a two-digit year is only true within this century, so a
    /// gate that did not read this would be asserting against a moving target.
    /// </value>
    public int Century { get; }

    /// <summary>Gets the expressions with no record dimension, in dump order.</summary>
    public IReadOnlyList<DumpExpression> ConstantExpressions { get; }

    /// <summary>Gets the expressions over the table's fields, in dump order.</summary>
    public IReadOnlyList<DumpExpression> FieldExpressions { get; }

    /// <summary>Gets the expressions whose value comes from the clock, recorded for shape only.</summary>
    public IReadOnlyList<DumpExpression> ClockDependentExpressions { get; }

    /// <summary>Gets what the reference implementation refused, and the code it refused with.</summary>
    public IReadOnlyList<DumpRejection> Rejections { get; }

    /// <summary>
    /// Gets the Visual FoxPro function names the reference implementation has no row for.
    /// </summary>
    /// <value>
    /// Recorded as refusals and deliberately not gated as such: this port accepts all thirteen, and
    /// Visual FoxPro itself is their oracle. They are read here so that the scanner can be shown to
    /// read them as ordinary calls, which is what makes accepting them possible at all.
    /// </value>
    public IReadOnlyList<DumpRejection> VisualFoxProOnly { get; }

    /// <summary>Gets the expressions the reference resolved for a tag, recorded for shape only.</summary>
    /// <value>
    /// A tag changes two lengths, so each of these names the tag it was parsed for and carries the
    /// compiled shape that tag gave it. No value is recorded: a tag changes no value.
    /// </value>
    public IReadOnlyList<DumpTagExpression> TagContextExpressions { get; }

    /// <summary>
    /// Gets every expression the reference implementation compiled, across the three value sections.
    /// </summary>
    public IEnumerable<DumpExpression> AcceptedExpressions =>
        ConstantExpressions.Concat(FieldExpressions).Concat(ClockDependentExpressions);

    /// <summary>
    /// Reads the dump out of the corpus.
    /// </summary>
    /// <returns>The parsed dump.</returns>
    public static CorpusExpressionDump Load()
    {
        List<DumpExpression> constants = [];
        List<DumpExpression> fields = [];
        List<DumpExpression> clock = [];
        List<DumpRejection> rejections = [];
        List<DumpRejection> visualFoxPro = [];
        List<DumpExpression> tagged = [];
        List<(string Table, string Tag)> taggedFor = [];
        string section = string.Empty;
        string picture = string.Empty;
        int century = 0;

        // The expression being read, held open until the next expression starts or the section ends,
        // because its node lines follow it rather than sitting on it.
        List<DumpExpression>? target = null;
        string? expression = null;
        char type = '\0';
        int length = 0;
        bool hasTrim = false;
        int nodeCount = 0;
        List<DumpNode> nodes = [];
        byte[]? value = null;
        Dictionary<int, byte[]> records = [];
        Dictionary<int, byte[]> fresh = [];

        void Close()
        {
            if (expression is not null && target is not null)
            {
                target.Add(new DumpExpression(
                    expression, type, length, hasTrim, nodeCount, nodes, value, records, fresh));
            }

            expression = null;
            type = '\0';
            length = 0;
            hasTrim = false;
            nodeCount = 0;
            nodes = [];
            value = null;
            records = [];
            fresh = [];
        }

        foreach (string raw in File.ReadLines(Path.Combine(Corpus.Root, FileName)))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.StartsWith('['))
            {
                Close();
                section = line;
                if (!KnownSections.Contains(section))
                    throw new InvalidDataException($"{FileName} holds an unknown section {section}.");

                target = section switch
                {
                    "[constant expressions]" => constants,
                    "[field expressions]" => fields,
                    "[clock-dependent expressions]" => clock,
                    "[tag context]" => tagged,
                    _ => null,
                };

                continue;
            }

            if (target is not null)
            {
                if (line.StartsWith("expr ", StringComparison.Ordinal))
                {
                    Close();
                    expression = ReadExpression(line, line.IndexOf('"', StringComparison.Ordinal));
                }
                else if (line.StartsWith("compiled ", StringComparison.Ordinal))
                {
                    type = line[line.IndexOf("type='", StringComparison.Ordinal) + "type='".Length];
                    length = ReadNumber(line, "len=");
                    hasTrim = ReadNumber(line, "hasTrim=") != 0;
                    nodeCount = ReadNumber(line, "nodes=");
                }
                else if (line.StartsWith("value ", StringComparison.Ordinal))
                {
                    int quote = line.IndexOf('"', StringComparison.Ordinal);
                    if (quote >= 0)
                        value = DumpEscape.ReadQuoted(line, ref quote);
                }
                else if (line.StartsWith("rec ", StringComparison.Ordinal))
                {
                    int quote = line.IndexOf('"', StringComparison.Ordinal);
                    if (quote >= 0)
                        records[ReadNumber(line, "rec ")] = DumpEscape.ReadQuoted(line, ref quote);
                }
                else if (line.StartsWith("fresh ", StringComparison.Ordinal))
                {
                    // A fresh parse that failed would leave the record ungated, so it is refused.
                    int quote = line.IndexOf('"', StringComparison.Ordinal);
                    if (quote < 0)
                        throw new InvalidDataException($"{FileName} has a fresh reading with no value: {line}");

                    fresh[ReadNumber(line, "fresh ")] = DumpEscape.ReadQuoted(line, ref quote);
                }
                else if (line.StartsWith("tag ", StringComparison.Ordinal))
                {
                    string[] named = line["tag ".Length..].Split(' ')[0].Split('.');
                    taggedFor.Add((named[0], named[1]));
                }
                else if (line.StartsWith("node ", StringComparison.Ordinal))
                {
                    nodes.Add(new DumpNode(
                        ReadNumber(line, "row="),
                        ReadNumber(line, "len="),
                        ReadNumber(line, "numParms="),
                        ReadNumber(line, "numEntries="),
                        ReadNumber(line, "fieldNo="),
                        ReadNumber(line, "i2=")));
                }

                continue;
            }

            if (section.Length == 0)
            {
                if (line.StartsWith("dateFormat", StringComparison.Ordinal))
                {
                    int quote = line.IndexOf('"', StringComparison.Ordinal);
                    picture = line[(quote + 1)..line.IndexOf('"', quote + 1)];
                }
                else if (line.StartsWith("century", StringComparison.Ordinal))
                {
                    century = ReadNumber(line, "century");
                }

                continue;
            }

            if (section is "[rejections]")
                AddRejection(rejections, line);
            else if (section is "[vfp-only]")
                AddRejection(visualFoxPro, line);
        }

        Close();
        if (tagged.Count != taggedFor.Count)
            throw new InvalidDataException($"{FileName} names a tag for {taggedFor.Count} of {tagged.Count} tag-context expressions.");

        return new CorpusExpressionDump(
            picture, century, constants, fields, clock, rejections, visualFoxPro,
            [.. tagged.Zip(taggedFor, (e, t) => new DumpTagExpression(e, t.Table, t.Tag))]);
    }

    /// <summary>
    /// Reads the number a named field of a dump line carries.
    /// </summary>
    private static int ReadNumber(string line, string field)
    {
        int marker = line.IndexOf(field, StringComparison.Ordinal);
        if (marker < 0)
            throw new InvalidDataException($"{FileName} has a line without {field}: {line}");

        int start = marker + field.Length;
        while (start < line.Length && line[start] == ' ')
            start++;

        int end = start;
        while (end < line.Length && (line[end] == '-' || char.IsAsciiDigit(line[end])))
            end++;

        return int.Parse(line[start..end], CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads one refusal, which is a quoted expression and the code it was refused with.
    /// </summary>
    private static void AddRejection(List<DumpRejection> rejections, string line)
    {
        if (!line.StartsWith('"'))
            return;

        int index = 0;
        string expression = ReadExpression(line, index: 0);
        index = 0;
        DumpEscape.ReadQuoted(line, ref index);

        string rest = line[index..];
        bool accepted = rest.Contains("ACCEPTED", StringComparison.Ordinal);

        int code = 0;
        int marker = rest.IndexOf("errorCode=", StringComparison.Ordinal);
        if (marker >= 0)
        {
            int start = marker + "errorCode=".Length;
            int end = start;
            while (end < rest.Length && (rest[end] == '-' || char.IsAsciiDigit(rest[end])))
                end++;

            code = int.Parse(rest[start..end], CultureInfo.InvariantCulture);
        }

        rejections.Add(new DumpRejection(expression, accepted, code));
    }

    /// <summary>
    /// Reads the quoted expression at a position and returns it as text.
    /// </summary>
    /// <value>
    /// The generator escapes the text as bytes, and an expression can carry a byte that is not
    /// ASCII, so it is decoded one byte to one character. That keeps the text the scanner sees
    /// byte-for-byte what the reference implementation parsed, which no other decoding would.
    /// </value>
    private static string ReadExpression(string line, int index)
    {
        byte[] bytes = DumpEscape.ReadQuoted(line, ref index);
        return Encoding.Latin1.GetString(bytes);
    }
}
