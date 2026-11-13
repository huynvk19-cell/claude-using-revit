# -*- coding: utf-8 -*-
"""Token budget of what Claude is sent on every request, on plain Python 3:
    python addin/tests/budget_check.py
- skill / subagent descriptions (sent on every request, every session): <= 350 characters;
- command headers (/* mcp-tool */, the tool list): valid JSON, description <= 250, parameter descriptions <= 80,
  none cut short ("…", "e.g" at the end: the rest belongs in the // ---- Details comment);
- every **Tools** line names only existing commands or built-in tools (a wrong name = a ToolSearch for nothing);
- the standard a skill names exists.
Exit 1 on a problem; the last line prints the totals."""
import glob
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SKILL_DESC, CMD_DESC, PARAM_DESC = 350, 250, 80
BUILT_IN = {'cur_task', 'cur_status', 'run_dynamic_command'}
HEADER = re.compile(r'/\*\s*mcp-tool\s*([\s\S]*?)\*/')
problems = []


def read(path):
    with open(path, encoding='utf-8') as f:
        return f.read()


def rel(path):
    return os.path.relpath(path, ROOT).replace(os.sep, '/')


def cut_short(text):
    return text.endswith(u'…') or re.search(r'\be\.g$', text) is not None


# ---- command headers (the MCP tool list)
commands, tool_chars = set(), 0
for f in sorted(glob.glob(os.path.join(ROOT, 'revit-commands', '*.cs'))):
    name = os.path.basename(f)[:-3]
    commands.add(name)
    m = HEADER.search(read(f))
    if not m:
        problems.append(rel(f) + ': no /* mcp-tool */ header')
        continue
    try:
        h = json.loads(m.group(1))
    except ValueError as e:
        problems.append(rel(f) + ': header JSON: ' + str(e))
        continue
    desc = h.get('description', '')
    schema = h.get('inputSchema', {})
    tool_chars += len(json.dumps({'name': name, 'description': desc, 'inputSchema': schema},
                                 ensure_ascii=False, separators=(',', ':')))
    if len(desc) > CMD_DESC:
        problems.append('%s: description %d > %d characters' % (rel(f), len(desc), CMD_DESC))
    if cut_short(desc):
        problems.append(rel(f) + ': description cut short')
    for key, prop in (schema.get('properties') or {}).items():
        d = prop.get('description', '') if isinstance(prop, dict) else ''
        if len(d) > PARAM_DESC:
            problems.append('%s: %s description %d > %d characters' % (rel(f), key, len(d), PARAM_DESC))
        if cut_short(d):
            problems.append('%s: %s description cut short' % (rel(f), key))

# ---- skill / subagent descriptions
desc_chars = 0
for f in sorted(glob.glob(os.path.join(ROOT, 'skills', '*', 'SKILL.md')) + glob.glob(os.path.join(ROOT, 'agents', '*.md'))):
    m = re.search(r'^description:\s*"?(.*?)"?\s*$', read(f), re.M)
    if not m:
        problems.append(rel(f) + ': no description')
        continue
    desc_chars += len(m.group(1))
    if len(m.group(1)) > SKILL_DESC:
        problems.append('%s: description %d > %d characters' % (rel(f), len(m.group(1)), SKILL_DESC))

# ---- **Tools** lines and named standards
known = commands | BUILT_IN
md_files = glob.glob(os.path.join(ROOT, 'skills', '**', '*.md'), recursive=True) + glob.glob(os.path.join(ROOT, 'workflows', '*.md'))
for f in sorted(md_files):
    text = read(f)
    for line in text.splitlines():
        if '**Tools**' not in line:
            continue
        part = line.split('**Tools**', 1)[1].split('**Planned**', 1)[0]
        for n in re.findall(r'`([a-z][a-z0-9_]*)`', part):
            if n not in known:
                problems.append('%s: **Tools** names %s, not a command' % (rel(f), n))
    for std in set(re.findall(r'~/\.claude/drafting-domain/([A-Za-z0-9_-]+\.md)', text)):
        if not os.path.exists(os.path.join(ROOT, 'domain', std)):
            problems.append('%s: standard %s not in domain/' % (rel(f), std))

for p in problems:
    print('FAIL ' + p)
print('budget: %d commands, tool list %d characters; skill + subagent descriptions %d characters; %d problem(s)'
      % (len(commands), tool_chars, desc_chars, len(problems)))
sys.exit(1 if problems else 0)
