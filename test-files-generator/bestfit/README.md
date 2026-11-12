# bestfit — Windows' best-fit supplement for `STR()` of a wide field

A Windows-only developer tool, like the generator beside it, and not part of the solution. It writes
one library source file, `net/CodeBase.Net/Expressions/WindowsBestFitSupplement.cs`, which is checked
in; building or testing the library never runs it.

## Why it exists

CodeBase turns a `'W'` field into text for `STR()` with `WideCharToMultiByte(CP_ACP, 0, …)`
(`e4functi.c:2694-2716`), so Windows' best-fit mapping decides what a character the code page lacks
becomes. The port narrows by the table's code page through .NET's code-page encodings
(`System.Text.Encoding.CodePages`), whose best-fit agrees with Windows' on every code point except a
few per code page — spaces, dashes and quotation marks from General Punctuation, which .NET turns into
`?`. On cp1251 U+2000 is one of them, and U+2000 is what CodeBase fills an unassigned wide character
with. The tool asks Windows for each of the 65,536 UTF-16 code units on each single-byte code page the
library names, compares with .NET, and writes down the differences (ADR-77).

It stops with an error if .NET maps a code point to anything but `?` where Windows differs, or if
either side produces more than one byte — the supplement is only valid while .NET is the one that
falls short.

## Running it

On Windows, from the repository root:

```
dotnet run --project test-files-generator/bestfit -- net/CodeBase.Net/Expressions/WindowsBestFitSupplement.cs
```

The comparison is against `System.Text.Encoding.CodePages` 8.0.0, the version the test projects
register. Rerun it when that version changes; an unchanged result regenerates the file byte-identical.
