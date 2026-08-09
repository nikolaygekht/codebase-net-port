---
name: sonar
description: >-
  Query a SonarQube instance about an analysis that has already been run — list
  projects, summarize and filter issues (bugs, vulnerabilities, code smells,
  security hotspots), inspect code coverage down to exact uncovered line
  numbers, check quality gates, and read rule descriptions. Use whenever the
  user asks about SonarQube findings, coverage/uncovered code, quality gates, or
  what needs fixing according to Sonar. Connection details are read from
  `.sonar.config` in the project root.
---

# SonarQube helper

Work on code on the basis of an **already-performed** SonarQube analysis. This
skill does not run scans; it reads results from the SonarQube Web API.

## Connection config

All connection info lives in **`.sonar.config`** at the project root
(searched for by walking up from the current directory). Format:

```
url   = http://host:9010
token = squ_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
# or, instead of a token:
# username = admin
# password = secret
```

A token is sent as the HTTP basic-auth username with an empty password (the
standard SonarQube scheme). Never hard-code the URL or token in commands — the
tool always reads them from this file. Treat the token as a secret; if the
project is under git, add `.sonar.config` to `.gitignore`.

## How to run things — avoid per-command approval prompts

Use the bundled **`SKILLS/sonar/sonar.py`** helper (Python 3 stdlib only, no
`pip install`, no dependencies). Invoke it as a single stable command so the
user can approve the prefix **once**:

```
python3 SKILLS/sonar/sonar.py <command> [args]
```

Prefer this over ad-hoc `curl` pipelines (which mint a new, differently-shaped
command every time and re-trigger approval). If the user would rather approve
`curl`, that also works — see "Raw curl" below — but `sonar.py` is the default.

Add `--json` to any command to get the raw API response instead of a table
(useful for piping or precise inspection). Add `--config PATH` to point at a
non-default config file.

## Commands

| Command | What it does |
|---|---|
| `status` | Server status + validate the token in `.sonar.config`. |
| `projects [--limit N]` | List all projects (key, last analysis, visibility). |
| `issues PROJECT [--unresolved]` | Issue **summary**: totals, effort, and facet counts by type / severity / impact / language / top rules. |
| `issues-list PROJECT [filters] [--limit N]` | **Detailed** issue list, sorted by file then line. Filters: `--severity`, `--type`, `--impact`, `--rule`, `--file`, `--unresolved`. |
| `hotspots PROJECT [--hstatus TO_REVIEW|REVIEWED] [--limit N]` | Security hotspots. |
| `coverage PROJECT` | Project coverage summary (coverage %, lines/conditions to cover, uncovered). |
| `coverage-files PROJECT [--best] [--limit N]` | Files ranked by coverage — least-covered first (`--best` for highest first). |
| `uncovered PROJECT FILE` | **Exact uncovered and partially-covered line numbers** for one file. `FILE` is the path relative to the project. |
| `measures COMPONENT --metrics m1,m2,...` | Arbitrary metrics for a project or `Project:path/to/file`. |
| `quality-gate PROJECT` | Quality-gate status and each failing condition. |
| `rule KEY` | Show a rule's name/type/severity and description (e.g. `python:S3776`). |
| `raw PATH [key=value ...]` | Escape hatch: GET any Web API path, prints JSON. |

### Filter values

- **severity** (legacy): `INFO`, `MINOR`, `MAJOR`, `CRITICAL`, `BLOCKER`
- **type**: `CODE_SMELL`, `BUG`, `VULNERABILITY`
- **impact** (Clean Code): `INFO`, `LOW`, `MEDIUM`, `HIGH`, `BLOCKER`
- **rule**: a rule key like `javascript:S3504`, `python:S3776`

## Typical workflows

**"What's the state of project X?"**
```
python3 SKILLS/sonar/sonar.py issues X
python3 SKILLS/sonar/sonar.py quality-gate X
python3 SKILLS/sonar/sonar.py coverage X
```

**"Show me the real problems, not style nits"** — bugs and vulnerabilities:
```
python3 SKILLS/sonar/sonar.py issues-list X --type BUG --limit 100
python3 SKILLS/sonar/sonar.py issues-list X --type VULNERABILITY
python3 SKILLS/sonar/sonar.py hotspots X
```

**"Which functions are too complex?"** — one rule at a time:
```
python3 SKILLS/sonar/sonar.py issues-list X --rule python:S3776
```

**"What's untested?"** — drill from project → files → exact lines:
```
python3 SKILLS/sonar/sonar.py coverage X
python3 SKILLS/sonar/sonar.py coverage-files X --limit 25
python3 SKILLS/sonar/sonar.py uncovered X path/to/File.cs
```
Then open `path/to/File.cs` at the reported line numbers to write tests.

**"What does this rule mean and how do I fix it?"**
```
python3 SKILLS/sonar/sonar.py rule javascript:S3504
```

## Interpreting results — important context

- SonarQube's legacy **CRITICAL** severity is dominated by *maintainability code
  smells* (e.g. `var`→`let/const`, duplicated literals, cognitive complexity),
  **not** runtime dangers. For correctness/security risk, look at **`--type BUG`**
  and **`--type VULNERABILITY`** and **hotspots**, not the CRITICAL bucket.
- **0% coverage** usually means *no coverage report was uploaded* during
  analysis (so every executable line counts as "uncovered"), not that tests
  fail. Confirm with `coverage` — if `tests` is absent/0, coverage was never fed
  in. Real coverage requires the CI analysis to attach a report (LCOV for JS,
  coverage.py XML for Python, dotnet-coverage/OpenCover for .NET, etc.).
- Effort/debt values are SonarQube's *estimates* in minutes; treat as rough.

## Raw curl (fallback)

If using `curl` instead of the helper, read config values first and send the
token as the basic-auth user:

```
curl -s -u "$(sed -n 's/^token *= *//p' .sonar.config):" \
  "$(sed -n 's/^url *= *//p' .sonar.config)/api/issues/search?componentKeys=X&ps=1"
```

Useful endpoints: `/api/system/status`, `/api/authentication/validate`,
`/api/projects/search`, `/api/issues/search`, `/api/hotspots/search`,
`/api/measures/component`, `/api/measures/component_tree`, `/api/sources/lines`,
`/api/qualitygates/project_status`, `/api/rules/show`.
