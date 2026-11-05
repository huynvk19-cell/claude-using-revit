// The official MCP TypeScript client against claude using revit (smoke.sh): connect, list tools, call one, read an error.
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StreamableHTTPClientTransport } from "@modelcontextprotocol/sdk/client/streamableHttp.js";

const url = new URL(process.argv[2] || "http://127.0.0.1:8970/mcp");
const client = new Client({ name: "smoke-sdk", version: "1.0.0" });
let failed = 0;
const check = (what, ok, detail) => {
    console.log(`${ok ? "PASS" : "FAIL"} sdk ${what}${ok ? "" : ": " + detail}`);
    if (!ok) failed++;
};

await client.connect(new StreamableHTTPClientTransport(url));
check("connect", client.getServerVersion()?.name === "claude-using-revit", JSON.stringify(client.getServerVersion()));
const { tools } = await client.listTools();
check("listTools", tools.some(t => t.name === "hello_test") && tools.some(t => t.name === "cur_task"), tools.map(t => t.name).join(","));
const r = await client.callTool({ name: "hello_test", arguments: { n: 42 } });
check("callTool", !r.isError && r.content[0].text.includes('"n":42'), JSON.stringify(r));
const e = await client.callTool({ name: "broken_test", arguments: {} });
check("tool error", e.isError === true, JSON.stringify(e));
await client.close();
process.exit(failed ? 1 : 0);
