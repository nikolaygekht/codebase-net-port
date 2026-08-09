# For developers

How to build, test and contribute to CodeBase.NET. If you want to know *what the library is*, read
[`README.md`](README.md) first.

## What you need

| To do this | You need |
|---|---|
| Build and test the library | **.NET 8 SDK**. Any OS. |
| Regenerate the test corpus | **Windows + MSVC** (x86 toolchain), and only then |

The second row is deliberate and rare. The original C library is the ground truth for every byte,
but it is consulted *offline*: it generates the golden files, they are checked in, and the tests read
them. **Building or testing CodeBase.NET never compiles or runs C.** See ADR-01 and ADR-02 in
[`claude/ARCHITECTURE-DECISIONS.md`](claude/ARCHITECTURE-DECISIONS.md).

## Repository layout

| Path | Contents |
|------|----------|
| `net/` | Everything .NET — `net/src/` (library), `net/tests/`, and `net/corpus/` |
| `net/corpus/` | Checked-in golden `DBF`/`CDX`/`FPT` files with their expected dumps — see its [README](net/corpus/README.md) |
| `test-files-generator/` | Windows/MSVC tool that drives the original C library to produce the corpus — see its [README](test-files-generator/README.md). Not part of the solution, not a test dependency |
| `original/source/` | The original CodeBase C source. **Read-only reference — never modified** |
| `original/examples/` | Original C examples and real sample files. Supplementary test input only |
| `claude/specs/` | Seven source-cited format specifications — authoritative for on-disk formats |
| `claude/PORTING-PLAN.md` | Scope, architecture, and the capability inventory with priorities and gates |
| `claude/ARCHITECTURE-DECISIONS.md` | Why things are the way they are, and what was rejected |
| `claude/DEV_APPROACH.md` | How a unit of work is designed, tested, planned and executed |
| `claude/dev/` | The per-step record: one folder per step |
| `.claude/skills/` | Agent skills checked into the repo — `sonar` (query an analysis), `docgen-skill` (documentation authoring), `dotnet-coverage`. Local settings (`settings.local.json`) stay untracked |
| `sonar.bat`, `SonarQube.Analysis.xml` | Quality analysis — see [Code quality](#code-quality--sonarqube) |
| `STATE.md` | What is ready, what changed last session, what is next |

### The specifications

| Spec | Covers |
|------|--------|
| `specs/DBF-FORMAT.md` | DBF header, field descriptors, record layout, all field-type encodings, `_NullFlags`, code pages, EOF rules |
| `specs/CDX-FORMAT.md` | CDX file structure, tag directory, node layouts, and the bit-packed leaf compression |
| `specs/KEY-COLLATION.md` | Expression-result → sortable key bytes, and the verbatim collation tables |
| `specs/FPT-MEMO.md` | FPT (and DBT) header/block formats, allocation, and record memo references |
| `specs/LOCKING-TRANSACTIONS.md` | Byte-range lock protocol at VFP offsets, transaction/log format, recovery |
| `specs/EXPRESSIONS.md` | Lexer/parser/precedence, the built-in function table, and key-affecting semantics |
| `specs/API-ERRORS.md` | Public API inventory, `r4*`/`e4*` codes, `CODE4` defaults, and the C → C# mapping |

They were written from the C source with `FILE.C:line` citations. **They are authoritative:** if code
and a spec disagree on a byte layout, the spec wins — or the spec has a bug worth fixing, not working
around silently. Their adversarial re-verification never completed, so spot-check a claim against
real bytes before building on it (`PORTING-PLAN.md` §9, risk R11).

## Building and testing

```bash
dotnet build   net/CodeBase.Net.sln
dotnet test    net/CodeBase.Net.sln
```

*(The solution does not exist yet — the port is pre-implementation. See `STATE.md`.)*

The name is **`CodeBase.Net`**, capital B, everywhere — solution, assembly, root namespace, test
projects, SonarQube project key (ADR-14).

Tests are layered, and the layers are tagged so the fast ones can run alone:

| Layer | What it runs against |
|---|---|
| Unit | Pure entities and functions — no I/O |
| Component | Controllers over an in-memory boundary fake |
| Fault injection | Controllers over a mocked boundary — truncation, `IOException`, contention |
| Golden | Real corpus files and their `.dump.txt` — the gate |

The reasoning behind that split, and the rules about where expected values may come from, is
[`claude/DEV_APPROACH.md`](claude/DEV_APPROACH.md) §4. The one rule you need up front: **never
hand-write expected bytes.** Hand-built bytes are fine as test *input*; expectations come from the
corpus, or from a round-trip invariant.

### What every test project must reference

Coverage and test results reach SonarQube through files produced by `dotnet test`, and those files
only appear if the packages that produce them are referenced. **Every** test project needs these in
addition to the usual test SDK, xUnit and AwesomeAssertions references:

```xml
<PackageReference Include="coverlet.collector" Version="10.0.1">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
<PackageReference Include="LiquidTestReports.Markdown" Version="1.0.9" />
```

| Package | Produces | Consumed by |
|---|---|---|
| `coverlet.collector` | `coverage.opencover.xml` — makes `--collect "XPlat Code Coverage"` work, and `Format=opencover` selects the format Sonar reads | `sonar.cs.opencover.reportsPaths` |
| the test SDK's `trx` logger | `*.trx` — per-test pass/fail results | `sonar.cs.vstest.reportsPaths` |
| `LiquidTestReports.Markdown` | `test-report.md` — the same run, readable by a human | you |

Without the `.trx` file Sonar reports coverage but shows **no test results at all**, which reads as
"this project has no tests". So the loggers are not optional decoration:

```bash
dotnet test net/CodeBase.Net.sln --collect "XPlat Code Coverage" \
    --logger trx --logger "liquid.md;LogFileName=test-report.md" \
    --results-directory TestResults \
    -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
```

`sonar.bat` runs exactly that line. One caveat with **xUnit v3**: `--collect` and `--logger` are
VSTest features, which is how `dotnet test` runs xUnit v3 by default — if a project is ever switched
to Microsoft.Testing.Platform mode, these flags stop producing files and Sonar silently loses both
coverage and results.

## Code quality — SonarQube

Analysis runs on **Windows** via [`sonar.bat`](sonar.bat) at the repository root:

```bat
sonar.bat
```

It runs `sonarscanner begin` → `dotnet build` → `dotnet test` (with the coverage and logger flags
above) → `sonarscanner end`. If the tests fail it still publishes, so the failures show up in Sonar,
and then exits non-zero.

**One-time setup.** Install the scanner (`dotnet tool install --global dotnet-sonarscanner`; it needs
a JRE), then create two files at the repository root — **both are gitignored, neither may be
committed**:

| File | For | Contents |
|---|---|---|
| `.sonar.config.bat` | `sonar.bat` | `set sonar_url=http://host:9010` and `set sonar_token=squ_…` |
| `.sonar.config` | the `sonar` agent skill, which queries results | `url = …` and `token = …`, one per line |

**Settings live in [`SonarQube.Analysis.xml`](SonarQube.Analysis.xml)**, not on the command line:
source exclusions (`original/`, `test-files-generator/`, `net/corpus/`, docs and skills), the coverage
and test-report globs, and the coverage exclusion for benchmarks. Only the project key, the host URL
and the token stay in `sonar.bat` — the first because it identifies the project, the last two because
they are secrets. Change an exclusion in the XML and the diff shows what changed and why.

## API documentation — docgen

API help will be generated by Gehtsoft's **docgen** (see the `docgen-skill` agent skill). The `doc/`
project is not wired up yet, but the comments it will consume are being written now, so they must be
authored to docgen's rules **from the start** — `cs2ds` is not MSDN or DocFX, and the failure mode is
silent: comments look right in the IDE while the generated help is truncated or empty.

The rules that bite most (ADR-15; full set in the skill's `references/source-extraction.md` — load the
skill before writing or editing `///` comments):

- **The first line of `<summary>` is the Brief.** One complete, standalone, plain-text sentence, all
  on one line, never wrapped, with no inline markup. Everything after that first line becomes the
  Details. Wrap the opening sentence and the Brief is cut mid-sentence.
- **Use docgen BBCode, not XML formatting:** `[c]code[/c]`, `[i]…[/i]`, `[b]…[/b]`. `<c>` breaks the
  sentence across three lines; `<i>`/`<b>` are dropped silently.
- **Cross-reference with `[clink=Fully.Qualified.Type]text[/clink]`, never `<see cref>`** (renders
  inert, or empty for BCL types — silently dropping words), and only in a detail paragraph.
- **No angle brackets or `->` arrows in prose**, not even escaped. Write "greater than", "A to B".
- **No `<remarks>`** — emitted nowhere, content vanishes. Fold it into `<summary>` paragraphs.
- Never begin a `///` line with `- ` or `* `; it becomes a stray bullet.
- Structural tags that render reliably: `<summary>`, `<para>`, `<param>`, `<returns>`,
  `<exception cref>`, `<value>`.

```csharp
/// <summary>
/// Reads the DBF header and returns the field descriptors it declares.
///
/// The descriptors are returned as stored, so [c]X[/c] and [c]Z[/c] appear as
/// [c]M[/c] and [c]C[/c] with the binary flag set, exactly as on disk.
/// </summary>
/// <param name="source">The table to read from.</param>
/// <returns>One descriptor per field, in physical order.</returns>
```

The library `.csproj` sets `GenerateDocumentationFile` so the XML doc file exists for docgen to
consume when the `doc/` project is added.

## Regenerating the corpus (Windows only)

Needed only when the corpus lacks a case you require.

```bat
cd test-files-generator
build-lib.bat        :: original C library -> obj\codebase.lib   (~2 min first time)
build-gen.bat        :: src\*.cpp          -> bin\testgen.exe
bin\testgen.exe      :: write test files   -> bin\out\
copy-corpus.bat      :: publish            -> ..\net\corpus\
```

Then review `git status` and commit what changed. Output is byte-stable: regenerating without
changing a case produces identical files (ADR-07).

To add a case, write `test-files-generator/src/case-<name>.cpp`, declare it in `cases.h`, and call it
from `main.cpp`. Utilities are shared; **test data is per-case on purpose**, so tuning one case
cannot move another case's bytes.

## How work is organised

Read [`claude/DEV_APPROACH.md`](claude/DEV_APPROACH.md) before starting anything non-trivial. In
short: work proceeds in small verifiable steps, and each step is **designed and planned in writing
before code is written** —

1. frame the step so it names its own verification;
2. design the classes (Entity / Boundary / Controller, SOLID);
3. plan the test pyramid;
4. decide the seams — what gets faked or mocked;
5. break it into ordered sub-steps;
6. only then execute.

Each step gets a folder, `claude/dev/XXX-step-name/`, holding `DESIGN.md`, `PLAN.md`, `STATE.md` and
`SUMMARY.md`. Start one by copying the template:

```bash
cp -r claude/dev/_template claude/dev/001-step-name
```

## Contributing

- **Trunk-based:** commit to `main`; no feature branches unless there is a reason (ADR-09).
- **Never modify `original/source/`.** It is reference material. Filenames are mixed-case — search
  case-insensitively.
- **Never use `CultureInfo`/`CompareInfo`/`GetSortKey()` anywhere near index keys.** Collation tables
  are ported verbatim; .NET culture APIs cannot reproduce the stored bytes
  (`specs/KEY-COLLATION.md` §8, `PORTING-PLAN.md` §3.4).
- **Endianness is per field, not per format.** Every multi-byte access states its endianness
  explicitly. The list of big-endian islands is in `CLAUDE.md`.
- A capability is done when its gate in `PORTING-PLAN.md` §5 passes — then update its status there.
- New source files carry a **GPL v3** header.

## Licence

GPL v3 (see [`LICENSE`](LICENSE)). The original CodeBase library by Sequiter, Inc. is LGPL v3; this
port is a derivative work relicensed under GPL v3 as permitted by the LGPL.
