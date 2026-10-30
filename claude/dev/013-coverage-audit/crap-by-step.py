"""Joins a crap4csharp report to the step that created each member's file.

Usage, from the repository root, after a crap4csharp run that kept net/coverage/:

    for f in $(git ls-files 'net/CodeBase.Net/*.cs'); do
        echo "$f|$(git log --diff-filter=A --format=%s -- "$f" | tail -1)"
    done > file-step.txt
    python crap-by-step.py file-step.txt crap-output.txt net/coverage/*/coverage.cobertura.xml

Prints one line per step and writes crap-by-step.tsv beside the report. See AUDIT.md for what the
attribution can and cannot say.
"""
import collections
import os
import sys
import xml.etree.ElementTree as ET

file_steps, report, cobertura = sys.argv[1:4]

# Commits before the 2026-08-19 renumbering name 007-seek-by-value, which is 008 now.
RENUMBERED = {'007': '008'}

step_of_file = {}
for line in open(file_steps, encoding='utf-8'):
    path, subject = line.rstrip('\n').split('|', 1)
    number = subject[:3]
    step_of_file[path.replace('net/', '', 1)] = RENUMBERED.get(number, number)

file_of_class = {}
for node in ET.parse(cobertura).iter('class'):
    name = node.get('name').replace('/', '.')
    filename = node.get('filename')
    at = filename.find('CodeBase.Net/')
    file_of_class.setdefault(name, filename[at:] if at >= 0 else filename)

rows = []
in_report = False
for line in open(report, encoding='utf-8'):
    if line.startswith('-----'):
        in_report = True
        continue
    parts = line.split()
    if not in_report or 'threshold' in line or len(parts) < 5:
        continue
    member, cls, cc, coverage, crap = parts[:5]
    path = file_of_class.get(cls)
    if path is None:
        prefixes = [k for k in file_of_class if cls.startswith(k)]
        path = file_of_class[max(prefixes, key=len)] if prefixes else '?'
    rows.append((step_of_file.get(path, '?'), path, member, cls, int(cc), coverage, crap))

totals = collections.defaultdict(lambda: [0, 0, 0])
for step, _, _, _, _, coverage, crap in rows:
    t = totals[step]
    t[0] += 1
    t[1] += crap != 'N/A' and float(crap) > 8
    t[2] += coverage == '0.0%'

print(f"{'step':5}{'members':>8}{'>8':>5}{'0%':>5}")
for step in sorted(totals):
    print(f"{step:5}{totals[step][0]:8}{totals[step][1]:5}{totals[step][2]:5}")

out = os.path.join(os.path.dirname(os.path.abspath(report)), 'crap-by-step.tsv')
with open(out, 'w', encoding='utf-8', newline='\n') as f:
    f.write('step\tfile\tmember\tclass\tcc\tcoverage\tcrap\n')
    f.write('\n'.join('\t'.join(map(str, r)) for r in rows) + '\n')
