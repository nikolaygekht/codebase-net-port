# Quality tools

Two command-line tools that measure what the test suite does not show by itself. Neither is part of
the solution, and neither is checked in: `fetch.bat` / `fetch.sh` clone them from Microsoft's
repositories into this directory and build them, and `.gitignore` keeps the clones out of git.

| Tool | Repository | Measures |
|---|---|---|
| `crap4csharp` | [microsoft/crap4csharp](https://github.com/microsoft/crap4csharp) | CRAP per member: `CC² × (1 − coverage)³ + CC` — complexity that tests do not cover |
| `mutate4csharp` | [microsoft/mutate4csharp](https://github.com/microsoft/mutate4csharp) | Mutants per file: whether the tests notice when an operator or constant is flipped |

Both are C# ports of Robert C. Martin's `crap4java` and `mutate4java`, the tools his
[`ub-mode`](https://github.com/unclebob/ub-mode) rules are paired with. Both are CC0. Why these and
not others: ADR-60 in [`claude/ARCHITECTURE-DECISIONS.md`](../claude/ARCHITECTURE-DECISIONS.md).
Where they fit in a step: [`claude/DEV_APPROACH.md`](../claude/DEV_APPROACH.md) §4.

## Getting them

Needs `git` and the .NET 8 SDK (the tools' own `global.json` asks for 8.0.406 or a later feature
band). From anywhere:

```bat
tools\fetch.bat
```

```sh
sh tools/fetch.sh
```

The first run clones; every later run pulls the **latest** commit and rebuilds. Nothing is pinned:
to update, run the script again. Each run prints the commit it built, so a number can be traced to a
tool version.

## crap4csharp

Run from `net/`, with the library's files listed explicitly:

```sh
cd net
dotnet run --project ../tools/crap4csharp/src/Crap4CSharp -c Release --no-build -- \
    $(find CodeBase.Net -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*')
```

```powershell
cd net
$files = Get-ChildItem CodeBase.Net -Recurse -Filter *.cs |
    Where-Object FullName -notmatch '\\(bin|obj)\\' | Resolve-Path -Relative
dotnet run --project ..\tools\crap4csharp\src\Crap4CSharp -c Release --no-build -- $files
```

One file, or a few, works the same way: `-- CodeBase.Net/Queries/TagEligibility.cs`. A run takes
seconds: it builds Debug, runs the unit tests once with coverage, and prints every member sorted by
CRAP, highest first.

Only the files changed since the last commit, new ones included — what `DEV_APPROACH.md` §4 runs
after each sub-step. With nothing changed the list is empty and the tool says so and exits 0.

```sh
cd net
dotnet run --project ../tools/crap4csharp/src/Crap4CSharp -c Release --no-build -- \
    $(git ls-files -m -o --exclude-standard -- 'CodeBase.Net/*.cs')
```

```powershell
cd net
$files = git ls-files -m -o --exclude-standard -- 'CodeBase.Net/*.cs'
dotnet run --project ..\tools\crap4csharp\src\Crap4CSharp -c Release --no-build -- $files
```

What to know before reading the numbers:

- **It looks for code in `src/` unless given files.** With no arguments, with a directory, or with
  `--changed`, it searches `<dir>/src/`, which this repository does not have. Pass files.
- **It runs `CodeBase.Net.Tests` only — layers 1–3, not the Golden layer.** It finds the test
  project by the `<Project>.Tests` name. Code covered only by corpus tests shows as 0%. That is the
  signal `DEV_APPROACH.md` §4 asks for, not a tool fault: such code has no test at its own level.
- **It filters tests by `type!=IntegrationTests`.** No test here carries a `type` trait, so all of
  them run.
- **The threshold is fixed at 8.0**; above it the exit code is 2. It cannot be configured. There is
  no gate on it here yet.
- **Exit 0 does not mean tested.** CRAP at 0% coverage is `CC² + CC`, so a member with one branch
  scores 6.0 and passes. Read the `Cov%` column.
- **It writes `coverage/` in the directory it runs from**, and deletes it at the start of the next
  run. Gitignored.

First run, 2026-10-30: 663 members, 93 above 8.0, 129 at 0% — most of those in `Queries/` and the
expression evaluator, which the Golden layer covers and the unit layers do not.

## mutate4csharp

One file per run, from `net/`:

```sh
cd net
dotnet run --project ../tools/mutate4csharp/src/Mutate4CSharp -c Release --no-build -- \
    CodeBase.Net/Queries/TagEligibility.cs --scan
```

`--scan` lists the mutation sites and nothing else: it runs no test and writes nothing. Without it,
the tool runs the baseline tests with coverage, then each mutant on a covered site, and reports which
were killed and which survived. `--help` lists the rest.

What to know before a full run — **so far only `--scan` has been run here**:

- **A clean run writes a manifest comment at the end of the source file.** It is how the next run
  skips scopes that did not change. That changes production code, so whether to keep those footers
  is undecided; until it is, run only on a committed tree and restore the file afterwards.
- **It mutates files on disk.** With more than one worker it copies the project into
  `mutation-workers/` (gitignored) and mutates the copies; with one worker its README does not say.
  The copy-aside rule in `DEV_APPROACH.md` §4 applies until that is known.
- Like `crap4csharp`, it runs only `CodeBase.Net.Tests`, so a mutant that only a Golden test would
  kill survives. Tests tagged `[Trait("Category", "no-mutate")]` are skipped.
