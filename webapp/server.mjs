// claude using revit - web app server (Node, no dependencies).
//
// The page (public/) is the user interface for every tool of the pyRevit tab "claude using revit"
// and every Revit MCP command, from any device on the LAN (PC browser, iPad...). Revit work goes
// through the thin bridge each Revit window runs (pyRevit lib/web_bridge.py, 127.0.0.1:48990+,
// discovered in %APPDATA%\claude-using-revit\web-bridge\<pid>.json); this server never touches the
// Revit MCP port 8964, so it does not compete with Claude sessions.
//
// Access: a token (webapp.json). Open http://<pc>:<port>/?k=<token> once per device; the page keeps
// it as a SameSite=Strict cookie. Every /api call needs that cookie and a Host header naming this PC.
// Settings: %APPDATA%\claude-using-revit\webapp.json (port, host, token, commandsDir, mcpBuildDir).
// host 127.0.0.1 (default) = this PC only; 0.0.0.0 = the LAN / Tailscale too. Restart the server after a change.
// Log: %APPDATA%\claude-using-revit\webapp.log. Started by pyRevit startup.py when not running.
import fs from "node:fs";
import http from "node:http";
import os from "node:os";
import path from "node:path";
import crypto from "node:crypto";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const DATA = path.join(process.env.APPDATA || os.homedir(), "claude-using-revit");
const BRIDGES = path.join(DATA, "web-bridge");
const CONFIG = path.join(DATA, "webapp.json");
const LOG = path.join(DATA, "webapp.log");
const PUBLIC = path.join(HERE, "public");
const EXT_TAB = path.join(process.env.APPDATA || "", "pyRevit", "Extensions", "ClaudeUsingRevit.extension",
    "claude using revit.tab");
const HEADER_RE = /\/\*\s*mcp-tool\s*([\s\S]*?)\*\//;
const NAME_RE = /^[a-z][a-z0-9_]{0,63}$/;
const MAX_JOBS = 200;
const MAX_RESULT = 4 * 1024 * 1024;

fs.mkdirSync(DATA, { recursive: true });

function log(...a) {
    const line = new Date().toISOString() + " " + a.join(" ") + "\n";
    try { fs.appendFileSync(LOG, line); } catch { /* ignore */ }
}

// ---- settings --------------------------------------------------------------------------

function mcpServerEntry() {
    // the Revit MCP server Claude Code uses (~/.claude.json mcpServers.revit-mcp args[0])
    try {
        const c = JSON.parse(fs.readFileSync(path.join(os.homedir(), ".claude.json"), "utf8"));
        for (const [k, v] of Object.entries(c.mcpServers || {}))
            if (/revit/i.test(k) && v.args && v.args[0]) return { entry: v.args[0], env: v.env || {} };
    } catch { /* none */ }
    return null;
}

function loadConfig() {
    let cfg = {};
    try { cfg = JSON.parse(fs.readFileSync(CONFIG, "utf8")); } catch { /* first run */ }
    let changed = false;
    const set = (k, v) => { if (cfg[k] === undefined || cfg[k] === null || cfg[k] === "") { cfg[k] = v; changed = true; } };
    set("port", 8780);
    set("host", "127.0.0.1");      // this PC only; "0.0.0.0" = the LAN too (iPad), token still needed
    set("token", crypto.randomBytes(6).toString("hex"));
    const mcp = mcpServerEntry();
    if (mcp) {
        const build = path.dirname(mcp.entry);
        set("mcpBuildDir", build);
        set("commandsDir", mcp.env.REVIT_MCP_DYNAMIC_DIR || process.env.REVIT_MCP_COMMANDS
            || path.resolve(build, "..", "..", "dynamic-commands"));
    }
    if (changed) fs.writeFileSync(CONFIG, JSON.stringify(cfg, null, 2));
    return cfg;
}

const cfg = loadConfig();

// ---- catalog: tasks, dynamic commands, base MCP tools ------------------------------------

const ORIGIN_FILE = path.join(HERE, "revit-commands.txt");    // written by install.ps1 -WebApp
let ownCommands = new Set();
try { ownCommands = new Set(fs.readFileSync(ORIGIN_FILE, "utf8").split(/\r?\n/).filter(Boolean)); }
catch {     // running from the repo checkout: its revit-commands folder
    const dir = path.join(HERE, "..", "revit-commands");
    if (fs.existsSync(dir)) ownCommands = new Set(fs.readdirSync(dir).filter(f => f.endsWith(".cs")).map(f => f.slice(0, -3)));
}

function readCommands() {
    const out = [];
    const dir = cfg.commandsDir;
    if (!dir || !fs.existsSync(dir)) return out;
    for (const f of fs.readdirSync(dir)) {
        if (!f.endsWith(".cs")) continue;
        const name = f.slice(0, -3);
        if (!NAME_RE.test(name)) continue;
        const src = fs.readFileSync(path.join(dir, f), "utf8");
        const m = HEADER_RE.exec(src);
        const c = { kind: "command", name, description: "", inputSchema: { type: "object", properties: {} },
            readOnly: false, timeoutSeconds: 300, own: ownCommands.has(name) };
        if (m) {
            try {
                const h = JSON.parse(m[1]);
                if (typeof h.description === "string") c.description = h.description;
                if (h.inputSchema) c.inputSchema = h.inputSchema;
                if (typeof h.readOnly === "boolean") c.readOnly = h.readOnly;
                if (typeof h.timeoutSeconds === "number") c.timeoutSeconds = h.timeoutSeconds;
            } catch (e) { c.headerError = String(e); }
        }
        out.push(c);
    }
    return out.sort((a, b) => a.name.localeCompare(b.name));
}

// tools of the add-in itself; the ones the MCP server handles in Node (not in Revit) are left out
const NODE_ONLY = new Set(["stop_mcp_service", "list_dynamic_commands", "run_dynamic_command"]);
let baseTools = null;

async function readBaseTools() {
    if (baseTools) return baseTools;
    baseTools = [];
    const file = cfg.mcpBuildDir && path.join(cfg.mcpBuildDir, "tools", "index.js");
    if (!file || !fs.existsSync(file)) return baseTools;
    const prev = process.env.MCP_PROFILE;
    process.env.MCP_PROFILE = "full";
    const err = console.error;
    console.error = () => {};
    try {
        const mod = await import(pathToFileURL(file).href);
        baseTools = mod.registerRevitTools()
            .filter(t => !NODE_ONLY.has(t.name))
            .map(t => ({ kind: "tool", name: t.name, description: t.description || "",
                inputSchema: t.inputSchema || { type: "object", properties: {} },
                readOnly: !!(t.annotations && t.annotations.readOnlyHint) }));
    } catch (e) {
        log("base tools not loaded:", e && e.message);
    } finally {
        console.error = err;
        if (prev === undefined) delete process.env.MCP_PROFILE; else process.env.MCP_PROFILE = prev;
    }
    return baseTools;
}

// ---- Revit windows (bridges) ------------------------------------------------------------

function alive(pid) {
    try { process.kill(pid, 0); return true; } catch (e) { return e.code === "EPERM"; }
}

function bridges() {
    const out = [];
    if (!fs.existsSync(BRIDGES)) return out;
    for (const f of fs.readdirSync(BRIDGES)) {
        if (!f.endsWith(".json")) continue;
        try {
            const b = JSON.parse(fs.readFileSync(path.join(BRIDGES, f), "utf8"));
            if (alive(b.pid)) out.push(b);
            else fs.rmSync(path.join(BRIDGES, f), { force: true });
        } catch { /* half written */ }
    }
    return out;
}

function bridgeCall(b, method, p, body, timeoutMs) {
    return new Promise((resolve, reject) => {
        const data = body ? Buffer.from(JSON.stringify(body), "utf8") : null;
        const req = http.request({ host: "127.0.0.1", port: b.port, path: p, method,
            headers: { "x-bridge-key": b.key, "content-type": "application/json",
                ...(data ? { "content-length": data.length } : {}) } }, res => {
            const chunks = [];
            res.on("data", c => chunks.push(c));
            res.on("end", () => {
                const text = Buffer.concat(chunks).toString("utf8");
                try { resolve({ status: res.statusCode, json: JSON.parse(text), size: text.length }); }
                catch { resolve({ status: res.statusCode, json: { error: text.slice(0, 2000) }, size: text.length }); }
            });
        });
        req.on("error", reject);
        if (timeoutMs) req.setTimeout(timeoutMs, () => req.destroy(new Error("timeout")));
        if (data) req.write(data);
        req.end();
    });
}

function bridgeOf(pid) {
    const b = bridges().find(x => String(x.pid) === String(pid));
    if (!b) throw httpError(404, "Revit window not found (closed, or pyRevit not reloaded since install)");
    return b;
}

async function windows() {
    return Promise.all(bridges().map(async b => {
        const w = { pid: b.pid, revit: b.revit, startedUtc: b.startedUtc };
        try { w.ping = (await bridgeCall(b, "GET", "/ping", null, 2000)).json; }
        catch (e) { w.error = String(e.message || e); }
        w.info = infoCache.get(b.pid) || null;
        return w;
    }));
}

const infoCache = new Map();

// ---- jobs -------------------------------------------------------------------------------

const jobs = [];
let nextJob = 1;

function unwrap(kind, r) {
    // tasks answer plain JSON; MCP commands a RevitCommandResponse {Success, Data, Error}
    if (kind === "task" || !r || typeof r !== "object" || !("Success" in r)) return r;
    if (!r.Success) return { error: r.Error || "failed" };
    const d = r.Data;
    if (kind === "command" && d && typeof d === "object" && "Result" in d) return d.Result;
    return d;
}

function startJob(pid, kind, name, args, label) {
    const b = bridgeOf(pid);
    let payload;
    if (kind === "task") payload = { kind: "task", name, args };
    else if (kind === "tool") payload = { kind: "mcp", name, args };
    else if (kind === "command") {
        if (!NAME_RE.test(name)) throw httpError(400, "bad command name");
        const file = path.join(cfg.commandsDir || "", name + ".cs");
        if (!fs.existsSync(file)) throw httpError(404, "command not found: " + name);
        payload = { kind: "mcp", name: "run_dynamic_command",
            args: { name, source: fs.readFileSync(file, "utf8"), args: args || {} } };
    } else throw httpError(400, "kind must be task, command or tool");
    const job = { id: nextJob++, pid: b.pid, kind, name, label: label || name, args: args || {},
        state: "running", started: Date.now(), finished: null, result: null };
    jobs.unshift(job);
    if (jobs.length > MAX_JOBS) jobs.length = MAX_JOBS;
    log("job", job.id, b.pid, kind, name, JSON.stringify(args || {}).slice(0, 300));
    bridgeCall(b, "POST", "/run", payload, 0).then(r => {
        let res = unwrap(kind, r.json);
        if (r.size > MAX_RESULT) res = { error: "result too large (" + r.size + " bytes)" };
        job.result = res;
        job.state = r.status !== 200 || (res && typeof res === "object" && res.error) ? "error" : "done";
    }).catch(e => {
        job.result = { error: "bridge: " + (e.message || e) };
        job.state = "error";
    }).finally(() => {
        job.finished = Date.now();
        log("job", job.id, job.state, ((job.finished - job.started) / 1000).toFixed(1) + "s");
    });
    return job;
}

function jobBrief(j) {
    const { result, ...rest } = j;
    return { ...rest, error: result && typeof result === "object" ? result.error || null : null };
}

// ---- HTTP -------------------------------------------------------------------------------

function httpError(status, message) { const e = new Error(message); e.status = status; return e; }

function send(res, status, obj, headers = {}) {
    const body = Buffer.from(JSON.stringify(obj), "utf8");
    res.writeHead(status, { "content-type": "application/json; charset=utf-8", "content-length": body.length,
        "cache-control": "no-store", ...headers });
    res.end(body);
}

const MIME = { ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8",
    ".css": "text/css; charset=utf-8", ".png": "image/png", ".svg": "image/svg+xml", ".json": "application/json" };

function sendFile(res, file) {
    fs.readFile(file, (err, buf) => {
        if (err) return send(res, 404, { error: "not found" });
        res.writeHead(200, { "content-type": MIME[path.extname(file)] || "application/octet-stream",
            "cache-control": "no-cache" });
        res.end(buf);
    });
}

function cookies(req) {
    const out = {};
    for (const part of (req.headers.cookie || "").split(";")) {
        const i = part.indexOf("=");
        if (i > 0) out[part.slice(0, i).trim()] = decodeURIComponent(part.slice(i + 1).trim());
    }
    return out;
}

function safeEqual(a, b) {
    const x = Buffer.from(String(a || "")), y = Buffer.from(String(b || ""));
    return x.length === y.length && crypto.timingSafeEqual(x, y);
}

function hostOk(req) {
    // DNS rebinding guard: the Host must be this PC (name, IP, localhost) or its Tailscale name
    const host = String(req.headers.host || "").replace(/:\d+$/, "").replace(/^\[|\]$/g, "").toLowerCase();
    if (!host) return false;
    if (host === "localhost" || host === os.hostname().toLowerCase() || host.endsWith(".ts.net")) return true;
    return /^[0-9.]+$/.test(host) || host.includes(":");
}

function readBody(req) {
    return new Promise((resolve, reject) => {
        const chunks = [];
        let n = 0;
        req.on("data", c => { n += c.length; if (n > 1024 * 1024) req.destroy(); else chunks.push(c); });
        req.on("end", () => {
            try { resolve(chunks.length ? JSON.parse(Buffer.concat(chunks).toString("utf8")) : {}); }
            catch { reject(httpError(400, "bad JSON")); }
        });
        req.on("error", reject);
    });
}

function lanUrls() {
    const urls = [];
    for (const list of Object.values(os.networkInterfaces()))
        for (const a of list || [])
            if (a.family === "IPv4" && !a.internal) urls.push(`http://${a.address}:${cfg.port}/?k=${cfg.token}`);
    return urls;
}

function listDirs(p) {
    if (!p) {
        const drives = [];
        for (const c of "CDEFGHIJKLMNOPQRSTUVWXYZ") if (fs.existsSync(c + ":\\")) drives.push(c + ":\\");
        return { path: "", parent: null, dirs: drives };
    }
    const full = path.resolve(p);
    const dirs = fs.readdirSync(full, { withFileTypes: true })
        .filter(d => d.isDirectory() && !d.name.startsWith("$")).map(d => d.name)
        .sort((a, b) => a.localeCompare(b));
    const parent = path.dirname(full);
    return { path: full, parent: parent === full ? "" : parent, dirs };
}

function iconFile(name) {
    // /icon/<button name>.png -> the ribbon icon of that button
    if (!fs.existsSync(EXT_TAB)) return null;
    for (const panel of fs.readdirSync(EXT_TAB)) {
        const f = path.join(EXT_TAB, panel, name + ".pushbutton", "icon.png");
        if (fs.existsSync(f)) return f;
    }
    return null;
}

async function api(req, res, url) {
    const parts = url.pathname.split("/").filter(Boolean);   // ["api", ...]
    const m = req.method;
    if (m === "GET" && parts[1] === "windows") return send(res, 200, { windows: await windows() });
    if (m === "GET" && parts[1] === "catalog") {
        return send(res, 200, { commands: readCommands(), tools: await readBaseTools(),
            commandsDir: cfg.commandsDir || null });
    }
    if (m === "GET" && parts[1] === "access") {
        const lan = !["127.0.0.1", "localhost", "::1"].includes(listenHost);
        return send(res, 200, { lan, urls: lan ? lanUrls() : [], config: CONFIG, port: cfg.port });
    }
    if (m === "GET" && parts[1] === "fs") return send(res, 200, listDirs(url.searchParams.get("path") || ""));
    if (m === "GET" && parts[1] === "jobs" && parts[2]) {
        const j = jobs.find(x => String(x.id) === parts[2]);
        return j ? send(res, 200, j) : send(res, 404, { error: "no such job" });
    }
    if (m === "GET" && parts[1] === "jobs") return send(res, 200, { jobs: jobs.slice(0, 60).map(jobBrief) });
    if (parts[1] === "w" && parts[2]) {
        const pid = parts[2];
        if (m === "GET" && parts[3] === "info") {
            const b = bridgeOf(pid);
            const r = await bridgeCall(b, "POST", "/run", { kind: "info", waitSeconds: 4 }, 15000);
            if (r.status === 200 && !r.json.error) infoCache.set(b.pid, r.json);
            return send(res, r.status === 200 ? 200 : 503, r.json);
        }
        if (m === "GET" && parts[3] === "tasks") {
            const r = await bridgeCall(bridgeOf(pid), "POST", "/run",
                { kind: "task", name: "list_tasks", args: {}, waitSeconds: 4 }, 15000);
            return send(res, r.status === 200 ? 200 : 503, r.json);
        }
        if (m === "POST" && parts[3] === "jobs") {
            const body = await readBody(req);
            const job = startJob(pid, body.kind, body.name, body.args, body.label);
            return send(res, 200, { id: job.id });
        }
    }
    return send(res, 404, { error: "not found" });
}

const server = http.createServer(async (req, res) => {
    try {
        const url = new URL(req.url, "http://x");
        if (!hostOk(req)) return send(res, 403, { error: "host not allowed" });
        const ok = safeEqual(cookies(req).k, cfg.token);
        if (url.searchParams.has("k")) {
            if (!safeEqual(url.searchParams.get("k"), cfg.token)) return send(res, 403, { error: "wrong access code" });
            res.writeHead(302, { location: "/", "set-cookie":
                `k=${encodeURIComponent(cfg.token)}; Path=/; Max-Age=31536000; SameSite=Strict; HttpOnly` });
            return res.end();
        }
        if (url.pathname === "/login" && req.method === "POST") {
            const body = await readBody(req);
            if (!safeEqual(body.code, cfg.token)) return send(res, 403, { error: "wrong access code" });
            return send(res, 200, { ok: true }, { "set-cookie":
                `k=${encodeURIComponent(cfg.token)}; Path=/; Max-Age=31536000; SameSite=Strict; HttpOnly` });
        }
        if (url.pathname.startsWith("/api/")) {
            if (!ok) return send(res, 401, { error: "login" });
            const origin = req.headers.origin;
            if (req.method !== "GET" && origin && new URL(origin).host !== req.headers.host)
                return send(res, 403, { error: "cross-site request" });
            return await api(req, res, url);
        }
        if (url.pathname.startsWith("/icon/")) {
            const f = iconFile(decodeURIComponent(url.pathname.slice(6)).replace(/\.png$/, ""));
            return f ? sendFile(res, f) : send(res, 404, { error: "no icon" });
        }
        const rel = url.pathname === "/" ? "index.html" : decodeURIComponent(url.pathname.slice(1));
        const file = path.resolve(PUBLIC, rel);
        if (!file.startsWith(PUBLIC + path.sep)) return send(res, 403, { error: "forbidden" });
        return sendFile(res, file);
    } catch (e) {
        if (!e.status) log("error", e && e.stack);
        return send(res, e.status || 500, { error: String(e.message || e) });
    }
});

server.on("error", e => { log("listen failed:", e.message); process.exit(1); });
const listenHost = process.env.CUR_WEBAPP_HOST || cfg.host;    // 127.0.0.1 = this PC only
server.listen(cfg.port, listenHost, () => {
    log(`listening on ${listenHost}:${cfg.port}`);
    console.log(`claude using revit web app: http://localhost:${cfg.port}/?k=${cfg.token}`);
    for (const u of lanUrls()) console.log("  " + u);
});
