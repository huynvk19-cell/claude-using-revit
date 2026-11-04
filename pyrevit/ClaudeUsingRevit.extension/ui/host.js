// How the page reaches Revit. Inside the Revit panel (lib/pane.py, WebView2) every call is a message to this
// Revit window: PANE.api() answers the same paths the web server (webapp/server.mjs) answers in a browser,
// so app.js, grid.js and viewer.js are the same in both. PANE is null in a browser. ASSET = where files are.
"use strict";
window.PANE = (() => {
  try { if (window.parent !== window && window.parent.PANE) return window.parent.PANE; } catch { /* other origin */ }
  const wv = window.chrome && window.chrome.webview;
  if (!wv) return null;
  const waiting = new Map();
  let nextMsg = 1, nextJob = 1, infoNow = null;
  const jobs = [];
  wv.addEventListener("message", e => {
    let m;
    try { m = JSON.parse(e.data); } catch { return; }
    const w = waiting.get(m.id);
    if (w) { waiting.delete(m.id); w(m.result); }
  });
  const ask = (kind, name, args, label) => new Promise(done => {
    const id = nextMsg++;
    waiting.set(id, done);
    wv.postMessage(JSON.stringify({ id, kind, name, args, label }));
  });
  const fail = r => { if (r && r.error) throw new Error(r.error); return r; };
  // one info at a time: while Revit is busy the question waits, it must not pile up
  const info = () => infoNow || (infoNow = ask("info").then(r => { infoNow = null; return fail(r); }));
  // tasks answer plain JSON; MCP commands a RevitCommandResponse {Success, Data, Error}
  function unwrap(kind, r) {
    if (kind === "task" || !r || typeof r !== "object" || !("Success" in r)) return r;
    if (!r.Success) return { error: r.Error || "failed" };
    const d = r.Data;
    return kind === "command" && d && typeof d === "object" && "Result" in d ? d.Result : d;
  }
  function start({ kind, name, args, label }) {
    const job = { id: nextJob++, kind, name, label: label || name, args: args || {}, state: "running", started: Date.now(), finished: null, result: null };
    jobs.unshift(job);
    if (jobs.length > 200) jobs.length = 200;
    ask(kind, name, args || {}, label).then(r => {
      job.result = unwrap(kind, r);
      job.state = job.result && typeof job.result === "object" && job.result.error ? "error" : "done";
      job.finished = Date.now();
    });
    return job;
  }
  async function api(path, opts = {}) {
    const url = new URL(path, "https://x");
    const p = url.pathname.split("/").filter(Boolean);       // ["api", ...]
    if (p[1] === "windows") { const i = await info(); return { windows: [{ pid: i.pid, revit: i.revit, info: i }] }; }
    if (p[1] === "catalog") return fail(await ask("catalog"));
    if (p[1] === "meshes") return fail(await ask("meshes"));
    if (p[1] === "fs") return fail(await ask("fs", null, { path: url.searchParams.get("path") || "" }));
    if (p[1] === "jobs" && p[2]) { const j = jobs.find(x => String(x.id) === p[2]); if (!j) throw new Error("no such job"); return j; }
    if (p[1] === "jobs") return { jobs: jobs.slice(0, 60).map(({ result, ...rest }) => ({ ...rest, error: result && result.error || null })) };
    if (p[1] === "w" && p[3] === "info") return info();
    if (p[1] === "w" && p[3] === "tasks") return fail(await ask("task", "list_tasks", {}));
    if (p[1] === "w" && p[3] === "jobs") return { id: start(JSON.parse(opts.body)).id };
    throw new Error("not found: " + path);
  }
  return { api };
})();

window.ASSET = (() => {
  const PANELS = { "Print Set Sync": "PrintSet", "Sheet List Columns": "PrintSet", "Export PDF": "PrintSet", "Highlight": "Highlight",
    "Highlight Untagged": "Untagged", "3D From View": "View3D" };
  return window.PANE ? {
    icon: name => `https://tab.cur.example/${PANELS[name]}.panel/${name}.pushbutton/icon.png`,
    photo: exts => exts.map(e => `https://data.cur.example/background${e}`),
    mesh: file => `https://data.cur.example/meshes/${file}`,
  } : {
    icon: name => `/icon/${name}.png`,
    photo: () => ["/bg"],
    mesh: file => `/mesh/${file}`,
  };
})();
