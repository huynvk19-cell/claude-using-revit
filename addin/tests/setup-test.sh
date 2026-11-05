#!/usr/bin/env bash
# The installer's logic on fake Windows folders (any OS). Run after addin/setup/make-setup.sh.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
dist="$here/../dist"
w="$(mktemp -d)"; trap 'rm -rf "$w"' EXIT
export APPDATA="$w/AppData/Roaming" USERPROFILE="$w/home" ProgramFiles="$w/ProgramFiles"
mkdir -p "$ProgramFiles/Autodesk/Revit 2023" "$ProgramFiles/Autodesk/Revit 2025" "$ProgramFiles/Autodesk/Revit 2024" \
         "$APPDATA/pyRevit/Extensions/ClaudeUsingRevit.extension" "$APPDATA/claude-using-revit/extension" \
         "$APPDATA/claude-using-revit/commands" "$USERPROFILE/.claude/skills/my-own"
touch "$ProgramFiles/Autodesk/Revit 2023/Revit.exe" "$ProgramFiles/Autodesk/Revit 2025/Revit.exe"   # 2024: folder only
touch "$APPDATA/claude-using-revit/extension/stale.py" "$APPDATA/claude-using-revit/commands/old_cmd.cs" "$USERPROFILE/.claude/skills/my-own/SKILL.md"
printf '{ "port": 8975, "autoStart": true }\n' > "$APPDATA/claude-using-revit/config.json"
cat > "$USERPROFILE/.claude.json" <<'J'
{"numStartups": 123, "big": 12345678901234567890, "name": "Huy \"x\" Việt\n", "tipsHistory": {"a": [1, 2.5e3, null]},
 "flag": true, "mcpServers": {"revit-mcp": {"command": "node", "args": ["x.js"]}, "other": {"type": "http", "url": "http://x"}}}
J
dotnet run --project "$here/SetupTest" -c Release -- "$dist/payload.zip" "$dist/ClaudeUsingRevit-Setup.exe"
