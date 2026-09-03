namespace CodeBase.Net.Expressions;

/// <summary>
/// The two settings the reference implementation calls a language, made explicit.
///
/// They are not localisation. No locale interface appears anywhere in the reference: the date picture
/// comes from a compile-time switch and the century, when the picture carries no century digits,
/// comes from a read of the system clock. So it is two constants and a clock read, and this makes
/// both a caller's choice.
///
/// The default has no century inference at all, which makes the clock-dependent path unreachable
/// unless a caller asks for it. A two-digit year with no century configured is refused rather than
/// guessed, because guessing means a stored key whose meaning changes with the calendar.
///
/// Governing decision: ADR-42. Governing specification: EXPRESSIONS.md section 7.3.
/// </summary>
public sealed class ExpressionOptions
{
    /// <summary>
    /// The picture the reference's default build carries.
    /// </summary>
    private const string ReferencePicture = "MM/DD/YY";

    /// <summary>
    /// The settings that infer nothing, which is what this engine does unless asked otherwise.
    /// </summary>
    /// <value>
    /// A four-digit picture, so no century has to be inferred and the clock is never read. Text that
    /// does not carry a full year is refused by the picture rather than completed by a guess.
    /// </value>
    public static ExpressionOptions Invariant { get; } = new("CCYYMMDD", null);

    /// <summary>
    /// The settings that reproduce the reference implementation's default build.
    /// </summary>
    /// <value>
    /// Its picture, and no century, which leaves a two-digit year refused. A caller who wants the
    /// reference's own answer for a two-digit year has to say which century, because the reference's
    /// answer was whatever century the machine was in when it ran.
    /// </value>
    public static ExpressionOptions CodeBase { get; } = new(ReferencePicture, null);

    private ExpressionOptions(string datePicture, int? century)
    {
        DatePicture = datePicture;
        Century = century;
    }

    /// <summary>
    /// Gets the picture a date is read and written with.
    /// </summary>
    /// <value>
    /// Its length is byte-affecting: a formatted date's result length is the picture's length, so a
    /// stored key built with one picture cannot be rebuilt with a picture of a different length. Two
    /// pictures of equal length are indistinguishable from the file, which is why this is a setting
    /// and not an inference.
    /// </value>
    public string DatePicture { get; }

    /// <summary>
    /// Gets the century a two-digit year belongs to, or null to refuse one.
    /// </summary>
    /// <value>
    /// Twenty means the twenty-first century, matching the reference's own division of the clock's
    /// year by one hundred. Null refuses, which is the default.
    /// </value>
    public int? Century { get; }

    /// <summary>
    /// Returns these settings with a century to complete a two-digit year with.
    /// </summary>
    /// <param name="century">The century, as the reference computes it: the year divided by a hundred.</param>
    /// <returns>The settings.</returns>
    public ExpressionOptions WithCentury(int century) => new(DatePicture, century);

    /// <summary>
    /// Returns these settings with a different date picture.
    /// </summary>
    /// <param name="picture">The picture.</param>
    /// <returns>The settings.</returns>
    public ExpressionOptions WithDatePicture(string picture) => new(picture, Century);
}
