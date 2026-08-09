#!/usr/bin/env python3
"""
sonar.py — a tiny, dependency-free SonarQube Web API client for working on code
on the basis of an analysis that has already been run.

Connection info comes from a `.sonar.config` file (see --config). No install,
no pip packages — Python 3 stdlib only. Every subcommand ultimately performs a
GET against the SonarQube Web API and prints a compact, human-readable table
(add --json for the raw response, e.g. to pipe elsewhere).

Config file format (default: .sonar.config in the project root)::

    url   = http://host:9010
    token = squ_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
    # optionally instead of a token:
    # username = admin
    # password = secret

Auth: a token is sent as HTTP basic-auth username with an empty password
(the standard SonarQube scheme). username/password are used if no token.

Run `python3 sonar.py --help` or `python3 sonar.py <command> --help`.
"""
import argparse
import base64
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request

DEFAULT_TIMEOUT = 30


# --------------------------------------------------------------------------- #
# Config + HTTP
# --------------------------------------------------------------------------- #
def find_config(explicit):
    """Locate .sonar.config: explicit path, else walk up from CWD, else near this script."""
    if explicit:
        return explicit
    d = os.getcwd()
    while True:
        cand = os.path.join(d, ".sonar.config")
        if os.path.isfile(cand):
            return cand
        parent = os.path.dirname(d)
        if parent == d:
            break
        d = parent
    # fall back to project root two levels up from SKILLS/sonar/
    here = os.path.dirname(os.path.abspath(__file__))
    cand = os.path.abspath(os.path.join(here, "..", "..", ".sonar.config"))
    return cand


def load_config(path):
    if not os.path.isfile(path):
        sys.exit(f"error: config file not found: {path}\n"
                 f"create a .sonar.config with 'url = ...' and 'token = ...'.")
    cfg = {}
    with open(path, encoding="utf-8") as fh:
        for raw in fh:
            line = raw.strip()
            if not line or line.startswith("#"):
                continue
            if "=" not in line:
                continue
            k, v = line.split("=", 1)
            cfg[k.strip().lower()] = v.strip()
    if "url" not in cfg:
        sys.exit(f"error: 'url' missing in {path}")
    cfg["url"] = cfg["url"].rstrip("/")
    return cfg


def auth_header(cfg):
    if cfg.get("token"):
        raw = f"{cfg['token']}:"
    elif cfg.get("username"):
        raw = f"{cfg['username']}:{cfg.get('password', '')}"
    else:
        return None
    return "Basic " + base64.b64encode(raw.encode()).decode()


def api_get(cfg, path, params=None, timeout=DEFAULT_TIMEOUT):
    url = cfg["url"] + path
    if params:
        # drop None values, keep explicit empties out
        params = {k: v for k, v in params.items() if v is not None}
        url += "?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(url, method="GET")
    hdr = auth_header(cfg)
    if hdr:
        req.add_header("Authorization", hdr)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            body = resp.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as e:
        detail = e.read().decode("utf-8", "replace")
        sys.exit(f"error: HTTP {e.code} for {url}\n{detail}")
    except urllib.error.URLError as e:
        sys.exit(f"error: cannot reach {url}: {e.reason}")
    if not body.strip():
        return {}
    try:
        return json.loads(body)
    except json.JSONDecodeError:
        return {"_raw": body}


def paged(cfg, path, params, key, page_size=500, cap=None):
    """Iterate a paginated endpoint, yielding items from `key`. Respects `cap`."""
    params = dict(params)
    params["ps"] = page_size
    page = 1
    seen = 0
    while True:
        params["p"] = page
        data = api_get(cfg, path, params)
        items = data.get(key, [])
        if not items:
            break
        for it in items:
            yield it
            seen += 1
            if cap and seen >= cap:
                return
        total = data.get("paging", {}).get("total")
        if total is not None and page * page_size >= total:
            break
        if len(items) < page_size:
            break
        page += 1


def out_json(obj):
    print(json.dumps(obj, indent=2, ensure_ascii=False))


def short(component_key):
    """Strip the 'ProjectKey:' prefix from a component key."""
    return component_key.split(":", 1)[-1] if ":" in component_key else component_key


# --------------------------------------------------------------------------- #
# Commands
# --------------------------------------------------------------------------- #
def cmd_status(cfg, a):
    st = api_get(cfg, "/api/system/status")
    val = api_get(cfg, "/api/authentication/validate")
    if a.json:
        return out_json({"status": st, "auth": val})
    print(f"server   : {cfg['url']}")
    print(f"status   : {st.get('status', '?')}  (version {st.get('version', '?')})")
    print(f"token/ok : {val.get('valid')}")


def cmd_projects(cfg, a):
    items = list(paged(cfg, "/api/projects/search", {}, "components", cap=a.limit))
    if a.json:
        return out_json(items)
    print(f"{'KEY':<32} {'LAST ANALYSIS':<22} VISIBILITY")
    for c in items:
        print(f"{c['key']:<32} {str(c.get('lastAnalysisDate','-')):<22} {c.get('visibility','')}")
    print(f"\n{len(items)} project(s)")


FACETS = ("severities,types,issueStatuses,rules,languages,"
          "impactSoftwareQualities,impactSeverities,cleanCodeAttributeCategories")


def cmd_issues(cfg, a):
    """Summary of issues for a project: totals + facet breakdowns."""
    params = {"componentKeys": a.project, "ps": 1, "facets": FACETS,
              "resolved": "false" if a.unresolved else None}
    data = api_get(cfg, "/api/issues/search", params)
    if a.json:
        return out_json(data)
    total = data.get("paging", {}).get("total", data.get("total", 0))
    effort = data.get("effortTotal", 0)
    print(f"project  : {a.project}")
    print(f"issues   : {total}   (est. effort {effort} min ~= {effort/60:.0f} h)")
    order = ["types", "severities", "impactSeverities", "impactSoftwareQualities",
             "issueStatuses", "languages", "rules"]
    facets = {f["property"]: f["values"] for f in data.get("facets", [])}
    for name in order:
        vals = facets.get(name)
        if not vals:
            continue
        top = vals[:10]
        print(f"\n{name}:")
        for v in top:
            print(f"  {v['count']:>8}  {v['val']}")
        if len(vals) > len(top):
            print(f"  ... (+{len(vals)-len(top)} more)")


def cmd_issues_list(cfg, a):
    """Detailed issue list with filters, sorted by file then line."""
    params = {
        "componentKeys": a.project,
        "severities": a.severity,
        "types": a.type,
        "rules": a.rule,
        "impactSeverities": a.impact,
        "resolved": "false" if a.unresolved else None,
        "s": "FILE_LINE", "asc": "true",
    }
    if a.file:
        params["componentKeys"] = f"{a.project}:{a.file}" if ":" not in a.file else a.file
    items = list(paged(cfg, "/api/issues/search", params, "issues", cap=a.limit))
    if a.json:
        return out_json(items)
    for i, x in enumerate(items, 1):
        print(f"{i:>4}. [{x['rule']}] {short(x['component'])}:{x.get('line','-')}  "
              f"({x.get('severity','')}, {x.get('effort','?')})")
        print(f"      {x.get('message','')}")
    print(f"\n{len(items)} issue(s) shown"
          + (f" (capped at {a.limit})" if a.limit and len(items) >= a.limit else ""))


def cmd_hotspots(cfg, a):
    """Security hotspots for a project."""
    params = {"projectKey": a.project, "status": a.hstatus}
    items = list(paged(cfg, "/api/hotspots/search", params, "hotspots", cap=a.limit))
    if a.json:
        return out_json(items)
    for i, x in enumerate(items, 1):
        print(f"{i:>4}. [{x.get('vulnerabilityProbability','?')}] "
              f"{short(x.get('component',''))}:{x.get('line','-')}  "
              f"{x.get('securityCategory','')}  status={x.get('status','')}")
        print(f"      {x.get('message','')}")
    print(f"\n{len(items)} hotspot(s)")


COVERAGE_METRICS = ("coverage,line_coverage,branch_coverage,lines_to_cover,"
                    "uncovered_lines,conditions_to_cover,uncovered_conditions,"
                    "ncloc,lines,tests,test_success_density")


def cmd_coverage(cfg, a):
    """Project-level coverage summary."""
    data = api_get(cfg, "/api/measures/component",
                   {"component": a.project, "metricKeys": COVERAGE_METRICS})
    if a.json:
        return out_json(data)
    m = {x["metric"]: x.get("value") for x in data.get("component", {}).get("measures", [])}
    if not m:
        print(f"{a.project}: no coverage measures (no coverage report uploaded?)")
        return
    print(f"project  : {a.project}")
    for k in COVERAGE_METRICS.split(","):
        if k in m:
            print(f"  {k:<24} {m[k]}")


def cmd_coverage_files(cfg, a):
    """Least-covered files (ascending coverage), only files that have measures."""
    metrics = "coverage,uncovered_lines,lines_to_cover,uncovered_conditions"
    data = api_get(cfg, "/api/measures/component_tree", {
        "component": a.project, "metricKeys": metrics, "qualifiers": "FIL",
        "metricSort": "coverage", "s": "metric",
        "metricSortFilter": "withMeasuresOnly",
        "asc": "false" if a.best else "true",
        "ps": a.limit or 25,
    })
    if a.json:
        return out_json(data)
    comps = data.get("components", [])
    print(f"{'COV':>7} {'UNCOV':>6} {'TOCOV':>6}  FILE")
    for c in comps:
        m = {x["metric"]: x.get("value") for x in c.get("measures", [])}
        print(f"{str(m.get('coverage','-')):>6}% {str(m.get('uncovered_lines','-')):>6} "
              f"{str(m.get('lines_to_cover','-')):>6}  {c.get('path', c.get('key'))}")
    if not comps:
        print("(no files with coverage measures)")


def cmd_uncovered(cfg, a):
    """Exact uncovered / partially-covered line numbers for one file."""
    comp = a.file if ":" in a.file else f"{a.project}:{a.file}"
    data = api_get(cfg, "/api/sources/lines", {"key": comp})
    if a.json:
        return out_json(data)
    lines = data.get("sources", [])
    if not lines:
        print(f"no source/coverage lines for {comp}")
        return
    uncovered, partial = [], []
    for ln in lines:
        no = ln.get("line")
        covered = ln.get("lineHits")
        cond = ln.get("conditions")
        covcond = ln.get("coveredConditions")
        if covered is not None and covered == 0:
            uncovered.append(no)
        elif cond and covcond is not None and covcond < cond:
            partial.append(f"{no}({covcond}/{cond})")
    print(f"file      : {short(comp)}")
    print(f"uncovered lines ({len(uncovered)}): "
          + (", ".join(map(str, uncovered)) if uncovered else "none"))
    print(f"partial   branches ({len(partial)}): "
          + (", ".join(partial) if partial else "none"))


def cmd_measures(cfg, a):
    """Arbitrary metrics for a component (project or file)."""
    data = api_get(cfg, "/api/measures/component",
                   {"component": a.component, "metricKeys": a.metrics})
    if a.json:
        return out_json(data)
    m = {x["metric"]: x.get("value") for x in data.get("component", {}).get("measures", [])}
    print(f"component : {a.component}")
    for k in a.metrics.split(","):
        print(f"  {k:<28} {m.get(k, '-')}")


def cmd_quality_gate(cfg, a):
    data = api_get(cfg, "/api/qualitygates/project_status", {"projectKey": a.project})
    if a.json:
        return out_json(data)
    st = data.get("projectStatus", {})
    print(f"project      : {a.project}")
    print(f"quality gate : {st.get('status','?')}")
    for c in st.get("conditions", []):
        print(f"  [{c.get('status')}] {c.get('metricKey')} "
              f"{c.get('comparator','')} {c.get('errorThreshold','')} "
              f"(actual {c.get('actualValue','')})")


def cmd_rule(cfg, a):
    """Show a rule's description/details."""
    data = api_get(cfg, "/api/rules/show", {"key": a.key})
    if a.json:
        return out_json(data)
    r = data.get("rule", {})
    print(f"key    : {r.get('key')}")
    print(f"name   : {r.get('name')}")
    print(f"type   : {r.get('type')}   severity: {r.get('severity')}")
    desc = r.get("htmlDesc") or r.get("mdDesc") or ""
    # crude tag strip for readability
    import re
    print(re.sub(r"<[^>]+>", "", desc)[:2000])


def cmd_raw(cfg, a):
    """Escape hatch: GET any Web API path with key=value params. Always JSON."""
    params = {}
    for kv in a.params or []:
        if "=" in kv:
            k, v = kv.split("=", 1)
            params[k] = v
    out_json(api_get(cfg, a.path if a.path.startswith("/") else "/" + a.path, params))


# --------------------------------------------------------------------------- #
# CLI
# --------------------------------------------------------------------------- #
def build_parser():
    p = argparse.ArgumentParser(description="Dependency-free SonarQube Web API client.")
    p.add_argument("--config", help="path to .sonar.config (default: search upward from CWD)")
    p.add_argument("--json", action="store_true", help="print raw JSON instead of a table")
    sub = p.add_subparsers(dest="cmd", required=True)

    def add(name, fn, help_):
        sp = sub.add_parser(name, help=help_)
        sp.set_defaults(fn=fn)
        return sp

    add("status", cmd_status, "server status + token validation")

    sp = add("projects", cmd_projects, "list projects")
    sp.add_argument("--limit", type=int, default=500)

    sp = add("issues", cmd_issues, "issue summary (facet counts) for a project")
    sp.add_argument("project")
    sp.add_argument("--unresolved", action="store_true", help="only unresolved issues")

    sp = add("issues-list", cmd_issues_list, "detailed, filterable issue list")
    sp.add_argument("project")
    sp.add_argument("--severity", help="INFO|MINOR|MAJOR|CRITICAL|BLOCKER (comma-sep)")
    sp.add_argument("--type", help="CODE_SMELL|BUG|VULNERABILITY (comma-sep)")
    sp.add_argument("--impact", help="INFO|LOW|MEDIUM|HIGH|BLOCKER (comma-sep)")
    sp.add_argument("--rule", help="rule key, e.g. javascript:S3776")
    sp.add_argument("--file", help="restrict to a file path (relative to project)")
    sp.add_argument("--unresolved", action="store_true")
    sp.add_argument("--limit", type=int, default=100)

    sp = add("hotspots", cmd_hotspots, "security hotspots for a project")
    sp.add_argument("project")
    sp.add_argument("--hstatus", default="TO_REVIEW", help="TO_REVIEW|REVIEWED")
    sp.add_argument("--limit", type=int, default=100)

    sp = add("coverage", cmd_coverage, "project coverage summary")
    sp.add_argument("project")

    sp = add("coverage-files", cmd_coverage_files, "least- (or best-) covered files")
    sp.add_argument("project")
    sp.add_argument("--best", action="store_true", help="show highest coverage first")
    sp.add_argument("--limit", type=int, default=25)

    sp = add("uncovered", cmd_uncovered, "exact uncovered/partial lines in one file")
    sp.add_argument("project")
    sp.add_argument("file", help="file path relative to project (or full component key)")

    sp = add("measures", cmd_measures, "arbitrary metrics for a component")
    sp.add_argument("component", help="project key or 'Project:path/to/file'")
    sp.add_argument("--metrics", required=True, help="comma-separated metric keys")

    sp = add("quality-gate", cmd_quality_gate, "quality gate status")
    sp.add_argument("project")

    sp = add("rule", cmd_rule, "show a rule's description")
    sp.add_argument("key", help="rule key, e.g. python:S3776")

    sp = add("raw", cmd_raw, "GET any Web API path (escape hatch)")
    sp.add_argument("path", help="e.g. /api/issues/search")
    sp.add_argument("params", nargs="*", help="key=value pairs")

    return p


def main():
    a = build_parser().parse_args()
    cfg = load_config(find_config(a.config))
    a.fn(cfg, a)


if __name__ == "__main__":
    main()
