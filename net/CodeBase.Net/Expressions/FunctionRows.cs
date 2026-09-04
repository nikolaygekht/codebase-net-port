namespace CodeBase.Net.Expressions;

/// <summary>
/// The rows of the function table that the engine refers to by name.
///
/// The table itself is data and its order is semantic, so the passes over it name rows by index and
/// not by searching for a name. The reference implementation does the same, and it derives every one
/// of these indexes from its neighbour by addition (e4expr.h:160-300), with compile-time assertions
/// at four places to catch a row inserted in the wrong band. The indexes are transcribed here as the
/// absolute numbers they work out to, and a unit test asserts that each one still lands on the row
/// whose name it claims, which is the same protection the assertions give the C.
///
/// These are zero-based, as the reference's own are, and as the corpus dump reports them.
///
/// Governing specification: EXPRESSIONS.md section 5.
/// </summary>
internal static class FunctionRows
{
    /// <summary>The loader for a character field, before the concatenation rewrite reaches it.</summary>
    public const int FieldCharacter = 0;

    /// <summary>The loader that materializes a character field, chosen when a kernel needs the bytes.</summary>
    /// <value>
    /// Not a row the parser ever chooses. Overload resolution rewrites a character loader into this
    /// one where the value has to be laid down somewhere a kernel can work on it (e4parse.c:361-362),
    /// which is why a field under [c]UPPER[/c] carries this row and a bare field does not.
    /// </value>
    public const int FieldCharacterMaterialized = 3;

    /// <summary>The loader for a logical field.</summary>
    public const int FieldLogical = 5;

    /// <summary>The loader for a date field as a julian double, which a promotion selects.</summary>
    public const int FieldDateAsDouble = 6;

    /// <summary>The loader for a date field as its eight stored characters.</summary>
    public const int FieldDateAsText = 7;

    /// <summary>The loader for a numeric field as a double, which a promotion selects.</summary>
    public const int FieldNumericAsDouble = 8;

    /// <summary>The loader for a numeric field as the text the record stores.</summary>
    public const int FieldNumericAsText = 9;

    /// <summary>The loader for a currency field.</summary>
    public const int FieldCurrency = 10;

    /// <summary>The loader for a binary double field.</summary>
    public const int FieldDouble = 11;

    /// <summary>The loader for a four-byte integer field.</summary>
    public const int FieldInteger = 12;

    /// <summary>The loader for a timestamp field.</summary>
    public const int FieldTimestamp = 16;

    /// <summary>The loader for an integer field as a double, which a promotion selects.</summary>
    public const int FieldIntegerAsDouble = 18;

    /// <summary>The loader for a currency field as a double, which a promotion selects.</summary>
    public const int FieldCurrencyAsDouble = 19;

    /// <summary>The loader for a four-byte binary float field.</summary>
    public const int FieldBinaryFloat = 24;

    /// <summary>The loader for an integer field as a binary float, which a promotion selects.</summary>
    public const int FieldIntegerAsFloat = 25;

    /// <summary>The loader for a memo or general field.</summary>
    public const int FieldMemo = 26;

    /// <summary>The loader for a numeric literal.</summary>
    public const int NumericConstant = 27;

    /// <summary>The loader for a string literal.</summary>
    public const int StringConstant = 28;

    /// <summary>The loader for a numeric literal narrowed to a binary float, which a promotion selects.</summary>
    /// <value>
    /// The only promotion that targets a constant rather than a field, and the only one guarded on
    /// the producer's row: it fires only where the producer is already the numeric literal loader
    /// (e4parse.c:274-279).
    /// </value>
    public const int FloatConstant = 29;

    /// <summary>The first row of the logical constants and the prefix negation, which take no operands worth typing.</summary>
    public const int FirstLogicalWord = 30;

    /// <summary>The last row of that group, which is the prefix negation spelled with a bang.</summary>
    public const int LastLogicalWord = 35;

    /// <summary>Logical or, which is variadic.</summary>
    public const int Or = 36;

    /// <summary>Logical and, which is variadic.</summary>
    public const int And = 37;

    /// <summary>Joins two strings.</summary>
    public const int Concatenate = 38;

    /// <summary>Joins two strings where a trim has already been seen in the expression.</summary>
    public const int ConcatenateAfterTrim = 41;

    /// <summary>Joins two strings moving the left one's trailing blanks to the end.</summary>
    public const int ConcatenateMovingBlanks = 46;

    /// <summary>Subtracting, whose four rows are numeric, date minus date, date minus numeric and float.</summary>
    public const int Subtract = 47;

    /// <summary>The first of the nine rows that answer inequality.</summary>
    public const int FirstComparison = 51;

    /// <summary>The first of the eight rows that answer greater-or-equal.</summary>
    public const int GreaterOrEqual = 60;

    /// <summary>The first of the eight rows that answer less-or-equal.</summary>
    public const int LessOrEqual = 68;

    /// <summary>The first of the eight rows that answer equality.</summary>
    public const int Equal = 76;

    /// <summary>Greater than, which needs to know which operand was longer.</summary>
    public const int Greater = 84;

    /// <summary>Less than, which needs to know which operand was longer.</summary>
    public const int Less = 91;

    /// <summary>The last of the comparison rows.</summary>
    public const int LastComparison = 97;

    /// <summary>Raising to a power, in both its spellings.</summary>
    public const int Power = 98;

    /// <summary>Multiplying, numeric and then float.</summary>
    public const int Multiply = 100;

    /// <summary>Dividing, numeric and then float.</summary>
    public const int Divide = 102;

    /// <summary>Whether the left string occurs anywhere in the right one.</summary>
    public const int Contains = 104;

    /// <summary>Turns a number into the character it codes for.</summary>
    public const int Chr = 105;

    /// <summary>Answers the record's deletion flag as a character.</summary>
    public const int DeletedFlag = 106;

    /// <summary>Formats a number as text.</summary>
    public const int Str = 107;

    /// <summary>Formats a number as text with leading zeros.</summary>
    public const int StrZero = 109;

    /// <summary>Takes a run of characters out of a string.</summary>
    public const int SubStr = 110;

    /// <summary>Answers the time of day as text.</summary>
    public const int Time = 111;

    /// <summary>Upper cases a string.</summary>
    public const int Upper = 112;

    /// <summary>Formats a date as eight characters.</summary>
    public const int DateToSortableText = 113;

    /// <summary>Formats a date using the engine's date picture.</summary>
    public const int DateToText = 115;

    /// <summary>Formats a timestamp as text.</summary>
    public const int TimestampToText = 117;

    /// <summary>Produces a run of blanks.</summary>
    public const int Space = 120;

    /// <summary>Removes a string's trailing blanks.</summary>
    public const int Trim = 121;

    /// <summary>Removes a string's leading blanks.</summary>
    public const int LeftTrim = 122;

    /// <summary>Removes a string's blanks at both ends.</summary>
    public const int AllTrim = 123;

    /// <summary>Takes characters from the start of a string.</summary>
    public const int Left = 124;

    /// <summary>Takes characters from the end of a string.</summary>
    public const int Right = 125;

    /// <summary>Pads a string on the left.</summary>
    public const int PadLeft = 126;

    /// <summary>Pads a string on the right.</summary>
    public const int PadRight = 127;

    /// <summary>Chooses between two values, whose result is as long as the longer of them.</summary>
    public const int Conditional = 128;

    /// <summary>Builds a timestamp from its parts.</summary>
    public const int Timestamp = 132;

    /// <summary>Reads a date out of eight-character text, with no picture and no century to infer.</summary>
    public const int SortableTextToDate = 134;

    /// <summary>Reads a date out of text, by the picture.</summary>
    public const int TextToDate = 135;

    /// <summary>Answers today's date.</summary>
    public const int Today = 136;

    /// <summary>The day of the month, from a stored date and then from a julian one.</summary>
    public const int Day = 137;

    /// <summary>The month, from a stored date and then from a julian one.</summary>
    public const int Month = 139;

    /// <summary>The year, from a stored date and then from a julian one.</summary>
    public const int Year = 141;

    /// <summary>Answers whether the record is deleted.</summary>
    public const int Deleted = 143;

    /// <summary>How many records the table holds.</summary>
    public const int RecordCount = 144;

    /// <summary>Which record the cursor is on.</summary>
    public const int RecordNumber = 145;

    /// <summary>Reads the leading number out of text.</summary>
    public const int Value = 146;

    /// <summary>Turns a number into a four-byte binary long.</summary>
    public const int LongToBinary = 147;

    /// <summary>A named calculation, which this port does not support.</summary>
    public const int Calculation = 148;

    /// <summary>The first of the five rows that answer whether a value is empty.</summary>
    public const int Empty = 151;

    /// <summary>The first of the seventeen rows that convert a value to a descending key.</summary>
    public const int Descend = 156;

    /// <summary>The first of the eighteen rows that convert a value to an ascending key.</summary>
    public const int Ascend = 173;

    /// <summary>The last row of the ascending group, past which the table has one named row and its terminator.</summary>
    public const int LastAscend = 190;

    /// <summary>Whether two strings are equal once converted to keys.</summary>
    public const int KeyEqual = 191;

    // The band Visual FoxPro adds, which the reference implementation has no row for (ADR-43). Their
    // indexes carry on past the terminator so that every index the reference names keeps its value.

    /// <summary>The absolute value of a number.</summary>
    public const int Absolute = 193;

    /// <summary>A number with its fraction removed, towards zero.</summary>
    public const int Truncate = 194;

    /// <summary>A number rounded to a given number of decimal places.</summary>
    public const int Round = 195;

    /// <summary>The remainder of a division, taking its sign from the divisor.</summary>
    public const int Modulo = 196;

    /// <summary>The smallest of its operands, over numbers and then over strings.</summary>
    public const int Smallest = 197;

    /// <summary>The largest of its operands, over numbers and then over strings.</summary>
    public const int Largest = 199;

    /// <summary>The square root of a number.</summary>
    public const int SquareRoot = 201;

    /// <summary>Minus one, zero or one, by a number's sign.</summary>
    public const int Sign = 202;

    /// <summary>How long a string is.</summary>
    public const int Length = 203;

    /// <summary>Where one string occurs inside another, counting from one.</summary>
    public const int PositionOf = 204;

    /// <summary>The code of a string's first character.</summary>
    public const int CharacterCode = 205;

    /// <summary>Lower cases a string.</summary>
    public const int Lower = 206;

    /// <summary>A string repeated a given number of times.</summary>
    public const int Repeat = 207;
}
