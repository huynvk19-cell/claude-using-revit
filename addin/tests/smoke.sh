#!/usr/bin/env bash
# The MCP server of claude using revit without Revit (Linux / macOS / Git Bash): build, self test, two "windows" handing the
# port over, then the official MCP TypeScript client (StreamableHTTPClientTransport, as Claude Code uses) against it.
#   bash addin/tests/smoke.sh
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"
trap 'kill $(jobs -p) 2>/dev/null || true; rm -rf "$work"' EXIT
export XDG_CONFIG_HOME="$work/config" APPDATA="$work/config"
data="$XDG_CONFIG_HOME/claude-using-revit"
mkdir -p "$data/commands"
port=18970
printf '{ "port": %s, "autoStart": true }\n' "$port" > "$data/config.json"

cat > "$data/commands/hello_test.cs" <<'EOF'
/* mcp-tool
{ "description": "Smoke test command.", "inputSchema": { "type": "object", "properties": { "n": { "type": "number" } } },
  "timeoutSeconds": 30, "readOnly": true }
*/
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
public static class HelloTest
{
    public static object Run(UIApplication app, JObject args)
    {
        System.Threading.Thread.Sleep(50);
        return new { n = (int?)args["n"], version = 1, onApiThread = System.Threading.Thread.CurrentThread.Name == "fake Revit API thread" };
    }
}
EOF
cat > "$data/commands/broken_test.cs" <<'EOF'
/* mcp-tool
{ "description": "Does not compile.", "inputSchema": { "type": "object", "properties": {} } }
*/
public static class Broken { public static object Run(Autodesk.Revit.UI.UIApplication app, Newtonsoft.Json.Linq.JObject a) { return nope; } }
EOF

dotnet build "$here/../src/ClaudeUsingRevit.Compiler" -c Release -f net8.0 -v q -nologo | tail -1
dotnet build "$here/McpSmoke" -c Release -v q -nologo | tail -1
smoke="dotnet $here/McpSmoke/bin/Release/net8.0/McpSmoke.dll"

echo "== self test"
$smoke selftest

echo "== two windows: the second takes the port over"
$smoke serve 6 > "$work/a.log" &
first=$!
sleep 2
$smoke takeover
wait $first || true
cat "$work/a.log"
grep -q "end state Standby" "$work/a.log" && echo "PASS first window yielded" || { echo "FAIL first window did not yield"; exit 1; }

echo "== official MCP client"
$smoke serve 40 > "$work/b.log" &
sleep 2
if command -v node >/dev/null; then
    client="$work/client"
    mkdir -p "$client"
    cp "$here/mcp-client.mjs" "$client/"
    (cd "$client" && npm init -y >/dev/null && npm install --silent @modelcontextprotocol/sdk@1 >/dev/null)
    node "$client/mcp-client.mjs" "http://127.0.0.1:$port/mcp"
else
    echo "node not found: MCP client test skipped"
fi
