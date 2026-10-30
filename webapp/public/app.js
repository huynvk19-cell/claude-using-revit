// claude using revit - web app page (visionOS-like). Plain JS, no build step.
// Every Revit action is a job: POST /api/w/<pid>/jobs {kind: task|command|tool, name, args}, then
// GET /api/jobs/<id> until it is done. Tasks = lib/tasks.py (the ribbon buttons), commands = the
// Revit MCP dynamic commands, tools = the Revit MCP add-in's own commands.
// The tool tiles take their state from the read-only task status_board: colour only where work is left.
"use strict";

const $ = (s, el = document) => el.querySelector(s);
const S = { windows: [], pid: null, info: null, busy: null, board: null,
  catalog: null, tasks: null, filter: "all", picked: null, view: "tools", open: null };
const store = {
  get(k, d) { try { const v = localStorage.getItem("cur." + k); return v === null ? d : JSON.parse(v); } catch { return d; } },
  set(k, v) { try { localStorage.setItem("cur." + k, JSON.stringify(v)); } catch { /* private mode */ } },
};

function h(tag, attrs, ...kids) {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs || {})) {
    if (v === null || v === undefined || v === false) continue;
    if (k === "class") el.className = v;
    else if (k.startsWith("on")) el.addEventListener(k.slice(2), v);
    else el.setAttribute(k, v === true ? "" : v);
  }
  for (const k of kids.flat()) if (k !== null && k !== undefined && k !== false)
    el.append(k instanceof Node ? k : document.createTextNode(String(k)));
  return el;
}

function toast(msg, kind = "") {
  const t = h("div", { class: "toast " + kind }, msg);
  $("#toasts").append(t);
  setTimeout(() => t.remove(), kind === "err" ? 8000 : 3500);
}

// ---- API ------------------------------------------------------------------------------
async function api(path, opts = {}) {
  const r = await fetch(path, { credentials: "same-origin", headers: { "content-type": "application/json" }, ...opts });
  if (r.status === 401) { $("#login").classList.remove("hidden"); $("#login-code").focus(); throw new Error("Cần mã truy cập"); }
  const j = await r.json().catch(() => ({ error: "bad response" }));
  if (!r.ok) throw new Error(j.error || r.statusText);
  return j;
}
$("#login-form").addEventListener("submit", async e => {
  e.preventDefault();
  const r = await fetch("/login", { method: "POST", credentials: "same-origin",
    headers: { "content-type": "application/json" }, body: JSON.stringify({ code: $("#login-code").value.trim() }) });
  if (r.ok) { $("#login").classList.add("hidden"); boot(); } else toast("Sai mã", "err");
});

const sleep = ms => new Promise(r => setTimeout(r, ms));

// Run one job in the selected Revit window; resolves with its result (throws on error).
async function run(kind, name, args = {}, label) {
  if (!S.pid) throw new Error("Chưa có cửa sổ Revit nào");
  const { id } = await api(`/api/w/${S.pid}/jobs`, { method: "POST", body: JSON.stringify({ kind, name, args, label }) });
  setBusy(label || name);
  try {
    for (let i = 0; ; i++) {
      await sleep(i < 10 ? 400 : 1200);
      const j = await api(`/api/jobs/${id}`);
      if (j.state === "running") continue;
      if (j.state === "error") throw Object.assign(new Error((j.result && j.result.error) || "lỗi"), { result: j.result });
      return j.result;
    }
  } finally {
    setBusy(null);
  }
}

// run, show the outcome in a box (a sentence by default, raw JSON on errors), never throws
async function act(box, kind, name, args, label, say) {
  box.replaceChildren(h("div", { class: "result" }, h("span", { class: "busy" }, label + "…")));
  try {
    const res = await run(kind, name, args, label);
    const text = say ? say(res) : null;
    box.replaceChildren(text instanceof Node ? text : h("div", { class: "result" }, h("div", { class: "msg" }, text || label + ": xong")));
    return res;
  } catch (e) {
    box.replaceChildren(resultView(e.result || { error: e.message }));
    toast(label + ": " + e.message, "err");
    return null;
  }
}
// a write: run it, then check the board again
async function change(...a) { const r = await act(...a); loadBoard(); return r; }

function resultView(res) {
  return h("div", { class: "result" }, res && res.error ? h("div", { class: "err" }, res.error) : null,
    h("pre", {}, JSON.stringify(res, null, 2)));
}

function setBusy(label) {
  S.busy = label;
  $("#busy").className = label ? "busy" : "busy hidden";
  $("#busy").textContent = label || "";
}


function confirmRun(what) {
  const doc = S.info && S.info.doc ? S.info.doc.title : "?";
  return window.confirm(`${what}\n\nModel: ${doc}`);
}

// ---- Revit windows + where we are -------------------------------------------------------
async function loadWindows() {
  const { windows } = await api("/api/windows");
  S.windows = windows;
  if (!windows.find(w => String(w.pid) === String(S.pid))) {
    const last = store.get("pid", null);
    S.pid = (windows.find(w => String(w.pid) === String(last)) || windows[0] || {}).pid || null;
  }
  $("#where").classList.toggle("many", windows.length > 1);
  $("#where").classList.toggle("off", !windows.length);
  if (!windows.length) { $("#o-model").textContent = "Không thấy Revit"; $("#o-view").textContent = ""; }
}
// with several Revit windows, a tap on the model name lists them
$("#where").addEventListener("click", () => {
  if (S.windows.length < 2) return;
  const p = $("#windows");
  if (!p.classList.contains("hidden")) return p.classList.add("hidden");
  p.replaceChildren(...S.windows.map(w => {
    const info = String(w.pid) === String(S.pid) && S.info ? S.info : w.info;
    return h("button", { class: "pill" + (String(w.pid) === String(S.pid) ? " primary" : ""), onclick: () => {
      S.pid = w.pid; store.set("pid", w.pid); S.info = null; S.tasks = null; S.board = null; p.classList.add("hidden"); refreshInfo(true);
    } }, (info && info.doc ? info.doc.title : "(chưa mở model)") + ` · Revit ${w.revit}`);
  }));
  p.classList.remove("hidden");
});

let infoTimer = null;
async function refreshInfo(rebuild) {
  clearTimeout(infoTimer);
  if (S.pid && !S.busy && document.visibilityState === "visible") {
    try {
      const info = await api(`/api/w/${S.pid}/info`);
      if (info.error) throw new Error(info.error);
      const prev = S.info;
      const docChanged = !prev || !info.doc || !prev.doc || prev.doc.title !== info.doc.title
        || String(prev.schedules) !== String(info.schedules);
      const viewChanged = !prev || !prev.view || !info.view || prev.view.id !== info.view.id;
      S.info = info;
      $("#notice").classList.add("hidden");
      $("#o-model").textContent = info.doc ? info.doc.title : "Chưa mở model";
      $("#o-view").textContent = info.view ? info.view.name : "";
      for (const el of document.querySelectorAll("[data-sel]")) el.textContent = String(info.selection.length);
      if (rebuild || docChanged) { closeTool(); S.board = null; renderTiles(); loadBoard(); }
      else if (viewChanged) loadBoard();
    } catch (e) {
      $("#notice").textContent = "Revit đang bận hoặc có hộp thoại đang mở.";
      $("#notice").classList.remove("hidden");
    }
  }
  infoTimer = setTimeout(() => refreshInfo(false), 6000);
}

// ---- status board: what each tool has left to do -----------------------------------------
async function loadBoard() {
  if (!S.pid || !S.info || !S.info.doc) return;
  try { S.board = await run("task", "status_board", {}, "Kiểm tra"); }
  catch (e) { S.board = { error: e.message }; }
  renderTiles();
}
$("#btn-board").addEventListener("click", () => loadBoard());

const KINDS = [["Room", "room"], ["Door", "cửa đi"], ["Window", "cửa sổ"], ["Wall", "tường"], ["Floor", "sàn"]];
const KIND = Object.fromEntries(KINDS);

// the drawing lists that count: the ones with a print set of their name + the ones this user works with
function officialLists(b) {
  const named = new Set([...b.printSets.filter(r => r.setExists).map(r => r.schedule), ...(b.expected || [])]);
  return b.columns.filter(r => named.has(r.schedule));
}

// {level: work | done | idle, text, count}
const STATE = {
  printSet(b) {
    const missing = b.printSets.filter(r => !r.setExists && r.sheets && (b.expected || []).includes(r.schedule));
    if (missing.length) return { level: "work", count: missing.length, text: missing.length === 1 ? `Chưa có print set ${missing[0].schedule}` : `Thiếu ${missing.length} print set` };
    const off = b.printSets.filter(r => r.setExists && !r.inSync);
    if (off.length) return { level: "work", count: off.length, text: off.length === 1 ? `${off[0].schedule} lệch danh mục` : `${off.length} print set lệch danh mục` };
    const n = b.printSets.filter(r => r.setExists).length;
    return n ? { level: "done", text: "Khớp danh mục" } : { level: "idle", text: "Chưa có print set theo danh mục" };
  },
  columns(b) {
    const rows = officialLists(b), off = rows.filter(r => !r.inSync && !r.error);
    if (off.length) return { level: "work", count: off.length, text: off.length === 1 ? `${off[0].schedule} chưa đúng chuẩn` : `${off.length} danh mục chưa đúng chuẩn` };
    return rows.length ? { level: "done", text: "Đúng chuẩn" } : { level: "idle", text: "Chưa có danh mục có print set cùng tên" };
  },
  untagged(b) {
    const u = b.untagged, left = u ? Object.entries(u.counts).filter(([, n]) => n) : [];
    if (left.length) return { level: "work", count: left.reduce((s, [, n]) => s + n, 0), text: left.map(([k, n]) => `${n} ${KIND[k]}`).join(", ") };
    if (u && b.untaggedMarks) return { level: "work", count: b.untaggedMarks, text: `Còn ${b.untaggedMarks} vùng tô cam` };
    return u ? { level: "done", text: "View này đã tag đủ" } : { level: "idle", text: "Mở một mặt bằng / mặt cắt" };
  },
  highlight(b) {
    return b.highlights ? { level: "work", count: b.highlights, text: `Còn ${b.highlights} vùng highlight` } : { level: "idle", text: "Tô đỏ đối tượng đang chọn" };
  },
  export() { return { level: "idle", text: "PDF hoặc DWG, mỗi sheet một file" }; },
  view3d() { return { level: "idle", text: "Hộp cắt theo view đang mở" }; },
};
const stateOf = key => !S.board ? { level: "idle", text: "Đang kiểm tra…" }
  : S.board.error ? { level: "idle", text: "Chưa kiểm tra được" } : STATE[key](S.board);

// ---- tiles + windows -------------------------------------------------------------------------
const TOOLS = [
  { key: "printSet", icon: "Print Set Sync", title: "Print set", open: printSetWindow },
  { key: "columns", icon: "Sheet List Columns", title: "Cột danh mục", open: columnsWindow },
  { key: "untagged", icon: "Highlight Untagged", title: "Chưa tag", open: untaggedWindow },
  { key: "highlight", icon: "Highlight", title: "Highlight", open: highlightWindow },
  { key: "export", icon: "Export PDF", title: "Xuất bản vẽ", open: exportWindow },
  { key: "view3d", icon: "3D From View", title: "3D theo view", open: view3dWindow },
];

function renderTiles() {
  const g = $("#view-tools");
  if (!S.pid || (S.info && !S.info.doc)) { g.replaceChildren(); return; }
  let work = 0;
  g.replaceChildren(...TOOLS.map(t => {
    const st = stateOf(t.key);
    if (st.level === "work") work++;
    return h("button", { class: `glass tile ${st.level}`, onclick: () => openTool(t) },
      h("div", { class: "ico" }, h("img", { src: "/icon/" + t.icon + ".png", alt: "" })),
      h("h3", {}, t.title), h("div", { class: "state" }, st.text),
      st.level === "work" ? h("span", { class: "count" }, st.count > 99 ? "99+" : String(st.count)) : null);
  }));
  const pill = $("#o-todo");
  pill.textContent = work === 1 ? "1 việc" : `${work} việc`;
  pill.classList.toggle("hidden", !work);
  document.title = (work ? `(${work}) ` : "") + "claude using revit";
  if (S.open) refreshWindowState();
  // /#export etc. (deep link /?k=..&open=export): open that tool once
  const want = location.hash.slice(1);
  const t = want && TOOLS.find(x => x.key === want);
  if (t && S.board) { history.replaceState(null, "", "/"); openTool(t); }
}

function openTool(t) {
  S.open = t;
  const win = $("#win");
  win.replaceChildren(
    h("div", { class: "w-head" }, h("div", { class: "ico" }, h("img", { src: "/icon/" + t.icon + ".png", alt: "" })),
      h("div", {}, h("h2", {}, t.title), h("div", { class: "w-state" }))),
    ...t.open());
  refreshWindowState();
  $("#stage").classList.remove("hidden");
}
function refreshWindowState() {
  const st = stateOf(S.open.key);
  $("#win").className = "glass window " + st.level;
  $(".w-state", $("#win")).textContent = st.text;
  for (const f of S.onOpenBoard || []) f(S.board);
}
function closeTool() { S.open = null; S.onOpenBoard = []; $("#stage").classList.add("hidden"); }
$("#win-close").addEventListener("click", closeTool);
$("#stage").addEventListener("click", e => { if (e.target.id === "stage") closeTool(); });
document.addEventListener("keydown", e => { if (e.key === "Escape") { closeTool(); $("#modal").classList.add("hidden"); } });
// a window part that redraws whenever the board changes
function live(draw) { S.onOpenBoard = S.onOpenBoard || []; S.onOpenBoard.push(draw); return draw; }

// ---- the six tools (only what is needed) ----------------------------------------------------
function printSetWindow() {
  S.onOpenBoard = [];
  const list = h("div", { class: "list" }), box = h("div");
  live(b => {
    if (!b || b.error) return list.replaceChildren();
    list.replaceChildren(...b.printSets.map(r => {
      const off = r.setExists && !r.inSync;
      return h("div", { class: "li" + (off ? " work" : "") }, h("span", { class: "t" }, r.schedule),
        h("span", { class: "n" }, `${r.sheets} sheet`),
        off ? h("span", { class: "n" }, `+${r.add} −${r.remove}`)
          : r.setExists ? h("span", { class: "ok" }, "✓")
          : h("button", { class: "pill small", onclick: () => sync({ schedule: r.schedule }, "Tạo print set") }, "Tạo"));
    }));
  });
  const sync = async (args, label) => {
    const res = await change(box, "task", "print_set_sync", args, label, r => {
      const rows = r.schedules || [];
      return rows.map(x => `${x.schedule}: ${x.status}`).join(" · ") || "Không có gì để đồng bộ";
    });
    const blocked = res && (res.schedules || []).filter(x => /someone else/.test(x.status || ""));
    if (blocked && blocked.length)
      box.append(h("div", { class: "row" }, h("span", { class: "dim" }, `Print set do ${blocked[0].lastChangedBy || "người khác"} sửa lần cuối.`),
        h("button", { class: "pill warn", onclick: () => confirmRun("Thay print set của người khác?") && sync({ schedule: blocked[0].schedule, force: true }, "Thay print set") }, "Vẫn thay")));
  };
  return [list, h("div", { class: "row" }, h("button", { class: "pill primary", onclick: () => sync({ all: true }, "Đồng bộ print set") }, "Đồng bộ")), box];
}

function columnsWindow() {
  S.onOpenBoard = [];
  const seg = h("div", { class: "seg" }), box = h("div");
  const want = new Set(store.get("columns.schedules", []));
  live(b => {
    const schedules = (S.info && S.info.schedules) || [];
    const off = new Set(b && !b.error ? officialLists(b).filter(r => !r.inSync).map(r => r.schedule) : []);
    if (!want.size) off.forEach(s => want.add(s));
    seg.replaceChildren(...schedules.map(s => h("button", { class: (want.has(s) ? "on" : "") + (off.has(s) ? " work" : ""),
      onclick: e => { want.has(s) ? want.delete(s) : want.add(s); store.set("columns.schedules", [...want]); e.target.classList.toggle("on"); } }, s)));
  });
  const pick = () => { const v = [...want]; if (!v.length) toast("Chọn danh mục", "err"); return v; };
  const signers = h("input", { type: "checkbox" });
  signers.addEventListener("change", () => { const s = pick(); if (s.length) change(box, "task", "sheet_list_signers", { schedules: s, show: signers.checked }, signers.checked ? "Hiện cột ký" : "Ẩn cột ký", () => signers.checked ? "Đã hiện 4 cột ký" : "Đã ẩn 4 cột ký"); });
  const param = h("select", { class: "grow" }), value = h("input", { class: "grow", placeholder: "Giá trị (trống = xoá)" });
  const more = h("details", { ontoggle: async e => {
    if (!e.target.open || param.options.length) return;
    const s = pick(); if (!s.length) return;
    const r = await act(box, "task", "sheet_list_fill", { schedules: s }, "Đọc tham số", r => `${r.sheets} sheet`);
    if (r && r.params) param.replaceChildren(...r.params.map(p => h("option", { value: p }, p)));
  } }, h("summary", {}, "Điền một giá trị cho mọi sheet"),
    h("div", { class: "row" }, param, value,
      h("button", { class: "pill", onclick: () => { const s = pick(); s.length && param.value && confirmRun(`Ghi "${value.value}" vào ${param.value} của mọi sheet?`) && change(box, "task", "sheet_list_fill", { schedules: s, param: param.value, value: value.value }, "Điền tham số", r => `Đã ghi ${r.written} sheet`); } }, "Ghi")));
  return [h("div", { class: "label" }, "Danh mục"), h("div", { class: "row" }, seg),
    h("div", { class: "row" },
      h("button", { class: "pill primary", onclick: async () => {
        const s = pick(); if (!s.length || !confirmRun("Thêm cột chuẩn + đánh số No.?")) return;
        const r = await change(box, "task", "sheet_list_columns", { schedules: s }, "Chuẩn cột", r => r.blocked ? "Danh mục người khác đang giữ / sửa cuối: " + r.blocked.join(", ") : `Thêm ${r.fieldsAdded} cột, ghi ${r.numbersWritten} số No.`);
        if (r && r.blocked) box.append(h("div", { class: "row" }, h("button", { class: "pill warn", onclick: () => confirmRun("Áp dụng cả danh mục người khác sửa?") && change(box, "task", "sheet_list_columns", { schedules: s, force: true }, "Chuẩn cột", r => `Thêm ${r.fieldsAdded} cột, ghi ${r.numbersWritten} số No.`) }, "Vẫn áp dụng")));
      } }, "Áp dụng chuẩn"),
      h("label", { class: "switch" }, signers, h("i"), "Cột người ký")),
    more, box];
}

function untaggedWindow() {
  S.onOpenBoard = [];
  const box = h("div"), list = h("div", { class: "list" });
  let scope = "active";
  let kinds = store.get("untagged.kinds", KINDS.map(k => k[0]));
  const chips = h("div", { class: "seg" });
  const drawChips = () => {
    const c = S.board && S.board.untagged ? S.board.untagged.counts : {};
    chips.replaceChildren(...KINDS.map(([k, l]) => h("button", { class: (kinds.includes(k) ? "on" : "") + (c[k] ? " work" : ""),
      onclick: () => { kinds = kinds.includes(k) ? kinds.filter(x => x !== k) : [...kinds, k]; store.set("untagged.kinds", kinds); drawChips(); } },
      l + (c[k] ? ` ${c[k]}` : ""))));
  };
  live(drawChips);
  const scopeSeg = h("div", { class: "seg" },
    ...[["active", "View đang mở"], ["sel", "View / sheet đang chọn"]].map(([k, l]) =>
      h("button", { class: k === scope ? "on" : "", onclick: e => { scope = k; [...scopeSeg.children].forEach(b => b.classList.toggle("on", b === e.target)); scan(); } }, l)));
  const args = () => ({ kinds, views: scope === "active" ? [S.info.view.id] : S.info.selection });
  const scan = async () => {
    list.replaceChildren();
    const r = await act(box, "task", "untagged_scan", { ...args(), detail: true }, "Đếm", () => "");
    if (!r) return;
    box.replaceChildren();
    list.replaceChildren(...(r.views || []).map(v => {
      const ids = kinds.flatMap(k => v[k + "Ids"] || []), n = kinds.map(k => v[k] ? `${v[k]} ${KIND[k]}` : "").filter(Boolean).join(", ");
      return h("div", { class: "li" + (ids.length ? " work" : "") }, h("span", { class: "t" }, v.view), h("span", { class: "n" }, n || "✓"),
        ids.length ? h("button", { class: "pill small", onclick: () => run("task", "show_elements", { view: v.id, ids }, "Mở view").catch(e => toast(e.message, "err")) }, "Mở") : null);
    }));
  };
  setTimeout(scan, 0);
  return [h("div", { class: "row" }, scopeSeg), h("div", { class: "row" }, chips), list,
    h("div", { class: "row" },
      h("button", { class: "pill primary", onclick: () => change(box, "task", "untagged_highlight", args(), "Tô chỗ thiếu tag",
        r => `Đã tô: ${(r.views || []).map(v => v.view).join(", ") || "không có gì"}`) }, "Tô chỗ thiếu tag"),
      h("button", { class: "pill", onclick: () => confirmRun("Xoá mọi vùng tô cam của bạn trong model?") && change(box, "task", "untagged_remove", {}, "Xoá vùng tô", r => `Đã xoá ${r.removed} vùng tô`) }, "Xoá vùng tô")),
    box];
}

function highlightWindow() {
  S.onOpenBoard = [];
  const box = h("div");
  return [h("div", { class: "row" },
    h("button", { class: "pill primary", onclick: () => {
      const ids = S.info ? S.info.selection : [];
      if (!ids.length) return toast("Chọn đối tượng trong Revit trước", "err");
      change(box, "task", "highlight", { ids }, "Highlight", r => `Đã tô ${r.created} đối tượng`);
    } }, "Tô ", h("span", { "data-sel": "" }, String(S.info ? S.info.selection.length : 0)), " đối tượng đang chọn"),
    h("button", { class: "pill", onclick: () => change(box, "task", "highlight_clear", {}, "Xoá highlight", r => `Đã xoá ${r.removed} vùng`) }, "Xoá hết highlight")), box];
}

function exportWindow() {
  S.onOpenBoard = [];
  const box = h("div"), list = h("div", { class: "list scroll" });
  const schedules = (S.info && S.info.schedules) || [];
  const sched = h("select", { class: "grow" }, ...schedules.map(s => h("option", { value: s }, s)));
  const last = store.get("export.schedule", null);
  if (S.info && S.info.view && schedules.includes(S.info.view.name)) sched.value = S.info.view.name;
  else if (last && schedules.includes(last)) sched.value = last;
  const filter = h("input", { class: "grow", type: "search", placeholder: "Lọc số / tên sheet" });
  const all = h("input", { type: "checkbox", checked: true, title: "Chọn hết / bỏ hết" });
  const folder = h("input", { class: "grow", placeholder: "Thư mục" });
  const groupBy = h("select", { class: "grow" });
  const setup = h("select", { class: "grow" }, h("option", { value: "" }, "Setup DWG mặc định"));
  const merged = h("input", { type: "checkbox", checked: true });
  let fmt = store.get("export.format", "pdf"), sheets = [], ticked = new Set();
  const go = h("button", { class: "pill primary" });

  const label = () => { go.textContent = `Xuất ${ticked.size} sheet ${fmt.toUpperCase()}`; go.disabled = !ticked.size; all.checked = ticked.size === sheets.length; };
  const draw = () => {
    const q = filter.value.trim().toLowerCase();
    list.replaceChildren(...sheets.filter(s => !q || (s.number + " " + s.name).toLowerCase().includes(q)).map(s => {
      const cb = h("input", { type: "checkbox", checked: ticked.has(s.number) });
      cb.addEventListener("change", () => { cb.checked ? ticked.add(s.number) : ticked.delete(s.number); label(); });
      return h("label", { class: "li" }, cb, h("span", { class: "no" }, s.number), h("span", { class: "nm" }, s.name));
    }));
    label();
  };
  const load = async () => {
    if (!sched.value) return;
    store.set("export.schedule", sched.value);
    const r = await act(box, "task", "schedule_sheets", { schedule: sched.value }, "Đọc sheet", () => "");
    if (!r) return;
    box.replaceChildren();
    sheets = r.sheets;
    const sel = sheets.filter(s => s.selected);
    ticked = new Set((sel.length ? sel : sheets).map(s => s.number));
    if (!folder.value) folder.value = store.get("export.folder", r.pdfFolder || "");
    groupBy.replaceChildren(h("option", { value: "" }, "Không chia thư mục con"),
      ...r.groupBy.map(g => h("option", { value: g }, g === "browser" ? "Như Project Browser" : "Chia theo " + g)));
    groupBy.value = store.get("export.groupBy", "");
    draw();
  };
  sched.addEventListener("change", load);
  filter.addEventListener("input", draw);
  all.addEventListener("change", () => { all.checked ? sheets.forEach(s => ticked.add(s.number)) : ticked.clear(); draw(); });
  groupBy.addEventListener("change", () => store.set("export.groupBy", groupBy.value));
  folder.addEventListener("change", () => store.set("export.folder", folder.value));
  const fmtSeg = h("div", { class: "seg" }, ...["pdf", "dwg"].map(f => h("button", { class: f === fmt ? "on" : "", onclick: e => {
    fmt = f; store.set("export.format", f); [...fmtSeg.children].forEach(b => b.classList.toggle("on", b === e.target));
    pdfOpts.classList.toggle("hidden", f !== "pdf"); dwgOpts.classList.toggle("hidden", f !== "dwg"); label();
    if (f === "dwg" && setup.options.length < 2) loadSetups();
  } }, f.toUpperCase())));
  const loadSetups = async () => {
    try { const r = await run("command", "export_sheets_dwg", { list: true }, "Đọc setup DWG");
      if (r && r.DwgSetups) setup.replaceChildren(h("option", { value: "" }, "Setup DWG mặc định"), ...r.DwgSetups.map(s => h("option", { value: s }, s))); }
    catch (e) { toast(e.message, "err"); }
  };
  const pdfOpts = h("div", { class: "row" + (fmt === "pdf" ? "" : " hidden") }, groupBy,
    h("button", { class: "pill small", title: "Sau khi sửa model: thử lại view / sheet từng làm Revit văng", onclick: () => act(box, "task", "pdf_export", { schedule: sched.value, forget: true }, "Quên lượt văng", () => "Lần xuất sau sẽ thử lại các view từng văng") }, "Thử lại view từng văng"));
  const dwgOpts = h("div", { class: "row" + (fmt === "dwg" ? "" : " hidden") }, setup, h("label", { class: "switch" }, merged, h("i"), "Gộp view"));
  go.addEventListener("click", () => {
    const chosen = sheets.filter(s => ticked.has(s.number)).map(s => s.number);
    if (!chosen.length || !folder.value) return toast("Chọn sheet và thư mục", "err");
    if (!confirmRun(`Xuất ${chosen.length} sheet ${fmt.toUpperCase()} vào\n${folder.value}?\nRevit sẽ bận đến khi xong.`)) return;
    if (fmt === "pdf") {
      const a = { schedule: sched.value, folder: folder.value, groupBy: groupBy.value || undefined };
      if (chosen.length !== sheets.length) a.sheets = chosen;
      act(box, "task", "pdf_export", a, "Xuất PDF", r => `Đã xuất ${r.exported}/${r.total} sheet vào ${r.folder}` + (r.failed && r.failed.length ? ` · lỗi: ${r.failed.join("; ")}` : ""));
    } else {
      act(box, "command", "export_sheets_dwg", { sheetNumbers: chosen, folder: folder.value, setupName: setup.value || undefined, mergedViews: merged.checked }, "Xuất DWG",
        r => r.Error ? r.Error : `Đã xuất ${(r.Results || []).filter(x => x.Ok).length}/${chosen.length} file DWG`);
    }
  });
  if (fmt === "dwg") setTimeout(loadSetups, 0);
  setTimeout(load, 0);
  return [h("div", { class: "row" }, sched, fmtSeg),
    h("div", { class: "row" }, all, filter), list,
    h("div", { class: "row" }, folder, h("button", { class: "pill", onclick: () => pickFolder(folder.value, p => { folder.value = p; store.set("export.folder", p); }) }, "Chọn…")),
    pdfOpts, dwgOpts, h("div", { class: "row" }, go), box];
}

function view3dWindow() {
  S.onOpenBoard = [];
  const box = h("div"), cut = h("input", { type: "checkbox" });
  return [h("div", { class: "row" },
    h("button", { class: "pill primary", onclick: () => act(box, "task", "view_3d_from_view", { cut: cut.checked }, "Mở 3D", r => `Đã mở ${r.view3d || "view 3D"}`) }, "Mở 3D theo view đang mở"),
    h("label", { class: "switch" }, cut, h("i"), "Mặt bằng: dừng ở mặt cắt")), box];
}

// ---- folder picker ---------------------------------------------------------------------
async function pickFolder(start, done) {
  const body = $("#modal-body");
  const close = () => $("#modal").classList.add("hidden");
  const go = async p => {
    try {
      const r = await api("/api/fs?path=" + encodeURIComponent(p || ""));
      body.replaceChildren(h("h2", {}, "Chọn thư mục"), h("div", { class: "dim" }, r.path || "Ổ đĩa"),
        h("div", { class: "dirs" },
          r.parent !== null ? h("div", { class: "dir", onclick: () => go(r.parent) }, "‹  Lên trên") : null,
          ...r.dirs.map(d => h("div", { class: "dir", onclick: () => go(r.path ? r.path.replace(/\\$/, "") + "\\" + d : d) }, d))),
        h("div", { class: "row" },
          h("button", { class: "pill primary", disabled: !r.path, onclick: () => { close(); done(r.path); } }, "Chọn thư mục này"),
          h("button", { class: "pill", onclick: close }, "Huỷ")));
    } catch (e) { if (p) go(""); else toast(e.message, "err"); }
  };
  $("#modal").classList.remove("hidden");
  go(start);
}
$("#modal").addEventListener("click", e => { if (e.target.id === "modal") $("#modal").classList.add("hidden"); });

// ---- every command ------------------------------------------------------------------------
const GROUPS = [["all", "Tất cả"], ["task", "Nút ribbon"], ["own", "claude-using-revit"], ["command", "Dynamic khác"], ["tool", "Gốc MCP"]];

async function loadCatalog() {
  if (!S.catalog) S.catalog = await api("/api/catalog");
  if (!S.tasks && S.pid) {
    try {
      const r = await api(`/api/w/${S.pid}/tasks`);
      S.tasks = (r.tasks || []).map(t => ({ kind: "task", name: t.task, description: t.help, readOnly: /^\(RO\)/.test(t.help), inputSchema: null }));
    } catch { S.tasks = null; }
  }
  renderAll();
}
const items = () => { const c = S.catalog || { commands: [], tools: [] }; return [...(S.tasks || []), ...c.commands, ...c.tools]; };
const group = it => it.kind === "command" && it.own ? "own" : it.kind;

function renderAll() {
  $("#filters").replaceChildren(...GROUPS.map(([k, l]) => h("button", { class: S.filter === k ? "on" : "", onclick: () => { S.filter = k; renderAll(); } }, l)));
  const q = $("#search").value.trim().toLowerCase();
  const list = items().filter(it => (S.filter === "all" || group(it) === S.filter) && (!q || (it.name + " " + it.description).toLowerCase().includes(q)));
  $("#cmd-list").replaceChildren(...list.slice(0, 400).map(it => h("div", {
    class: "cmd" + (S.picked === it ? " on" : ""), onclick: () => { S.picked = it; renderAll(); renderDetail(); } },
    h("b", {}, it.name), h("small", {}, it.description))));
  if (!list.length) $("#cmd-list").append(h("p", { class: "dim" }, S.catalog ? "Không có lệnh nào khớp." : "Đang tải…"));
}
$("#search").addEventListener("input", renderAll);

function fieldFor(name, sch, required) {
  const t = Array.isArray(sch.type) ? sch.type[0] : sch.type;
  let input, get;
  if (t === "boolean") {
    input = h("select", {}, h("option", { value: "" }, "—"), h("option", { value: "true" }, "true"), h("option", { value: "false" }, "false"));
    if (sch.default !== undefined) input.value = String(sch.default);
    get = () => input.value === "" ? undefined : input.value === "true";
  } else if (sch.enum) {
    input = h("select", {}, h("option", { value: "" }, "—"), ...sch.enum.map(v => h("option", { value: v }, v)));
    get = () => input.value || undefined;
  } else if (t === "number" || t === "integer") {
    input = h("input", { type: "number", step: t === "integer" ? "1" : "any", placeholder: sch.default !== undefined ? String(sch.default) : "" });
    get = () => input.value === "" ? undefined : Number(input.value);
  } else if (t === "array" && sch.items && ["string", "number", "integer"].includes(sch.items.type)) {
    input = h("textarea", { placeholder: "Mỗi dòng một giá trị", rows: 3 });
    get = () => { const v = input.value.split(/[\n,]+/).map(s => s.trim()).filter(Boolean); return v.length ? (sch.items.type === "string" ? v : v.map(Number)) : undefined; };
  } else if (t === "string") {
    input = h("input", { placeholder: sch.default !== undefined ? String(sch.default) : "" });
    get = () => input.value === "" ? undefined : input.value;
  } else {
    input = h("textarea", { placeholder: "JSON", rows: 3 });
    get = () => input.value.trim() ? JSON.parse(input.value) : undefined;
  }
  return { el: h("div", { class: "field" }, h("label", {}, name + (required ? " *" : ""), h("code", {}, t || "any")),
    sch.description ? h("div", { class: "desc" }, sch.description) : null, input), get };
}

function renderDetail() {
  const it = S.picked, d = $("#cmd-detail");
  if (!it) return;
  const box = h("div"), fields = [];
  let raw = null;
  const props = it.inputSchema && it.inputSchema.properties;
  if (props && Object.keys(props).length) {
    const req = new Set(it.inputSchema.required || []);
    for (const [n, sch] of Object.entries(props)) fields.push([n, fieldFor(n, sch || {}, req.has(n))]);
  } else raw = h("textarea", { rows: 6, placeholder: it.kind === "task" ? '{"dryRun": true}' : "{}" });
  const args = () => {
    if (raw) return raw.value.trim() ? JSON.parse(raw.value) : {};
    const a = {};
    for (const [n, f] of fields) { const v = f.get(); if (v !== undefined) a[n] = v; }
    return a;
  };
  d.replaceChildren(h("h2", {}, it.name),
    h("div", {}, h("span", { class: "badge" }, GROUPS.find(g => g[0] === group(it))[1]),
      it.readOnly ? h("span", { class: "badge" }, "chỉ đọc") : h("span", { class: "badge work" }, "có thể sửa model / ghi file")),
    h("p", { class: "dim" }, it.description || "(không có mô tả)"),
    ...fields.map(([, f]) => f.el), raw ? h("div", { class: "field" }, h("label", {}, "args (JSON)"), raw) : null,
    h("div", { class: "row" }, h("button", { class: "pill primary", onclick: async () => {
      let a;
      try { a = args(); } catch (e) { return toast("JSON sai: " + e.message, "err"); }
      const safe = (it.readOnly && !/export/.test(it.name)) || a.dryRun === true || a.list === true;
      if (!safe && !confirmRun(`Chạy ${it.name}? Lệnh này có thể sửa model hoặc ghi file.`)) return;
      box.replaceChildren(h("div", { class: "result" }, h("span", { class: "busy" }, it.name + "…")));
      try { box.replaceChildren(resultView(await run(it.kind, it.name, a, it.name))); if (!safe) loadBoard(); }
      catch (e) { box.replaceChildren(resultView(e.result || { error: e.message })); }
    } }, "Chạy")), box);
}

// ---- jobs ------------------------------------------------------------------------------
async function renderJobs() {
  const { jobs } = await api("/api/jobs");
  $("#view-jobs").replaceChildren(...(jobs.length ? jobs : [null]).map(j => {
    if (!j) return h("p", { class: "dim" }, "Chưa chạy lệnh nào từ web app.");
    const secs = (((j.finished || Date.now()) - j.started) / 1000).toFixed(1);
    const more = h("div");
    return h("div", { class: "job", onclick: async () => {
      if (more.childNodes.length) return more.replaceChildren();
      const full = await api("/api/jobs/" + j.id);
      more.replaceChildren(h("div", { class: "dim" }, "args: " + JSON.stringify(full.args)), resultView(full.result));
    } }, h("div", { class: "top" },
      j.state === "running" ? h("span", { class: "busy" }, "") : h("span", { class: j.state === "error" ? "badge work" : "badge" }, j.state === "error" ? "lỗi" : "xong"),
      h("b", {}, j.label), h("small", {}, `${new Date(j.started).toLocaleTimeString()} · ${secs}s`)),
      j.error ? h("div", { class: "dim" }, j.error) : null, more);
  }));
}

// ---- access from other devices ---------------------------------------------------------
$("#btn-access").addEventListener("click", async () => {
  const r = await api("/api/access");
  $("#modal-body").replaceChildren(h("h2", {}, "Mở trên iPad"),
    r.lan ? h("p", { class: "dim" }, "Cùng mạng LAN hoặc Tailscale. Mở link một lần trên thiết bị đó; link chứa mã truy cập.")
      : h("p", { class: "dim" }, `Web app đang chỉ mở trong máy này. Để dùng từ iPad: trong ${r.config} đổi "host" thành "0.0.0.0", rồi chạy lại install.ps1 -WebApp.`),
    ...r.urls.map(u => h("div", { class: "row" }, h("code", { class: "grow" }, u),
      h("button", { class: "pill small", onclick: () => navigator.clipboard && navigator.clipboard.writeText(u).then(() => toast("Đã chép")) }, "Chép"))),
    h("div", { class: "row" }, h("button", { class: "pill primary", onclick: () => $("#modal").classList.add("hidden") }, "Xong")));
  $("#modal").classList.remove("hidden");
});

// ---- navigation + boot -------------------------------------------------------------------
let jobsTimer = null;
function show(view) {
  S.view = view;
  for (const b of document.querySelectorAll("#nav button")) b.classList.toggle("on", b.dataset.view === view);
  for (const v of ["tools", "all", "jobs"]) $("#view-" + v).classList.toggle("hidden", v !== view);
  clearInterval(jobsTimer);
  if (view === "all") loadCatalog().catch(e => toast(e.message, "err"));
  if (view === "jobs") { renderJobs(); jobsTimer = setInterval(renderJobs, 2500); }
}
for (const b of document.querySelectorAll("#nav button")) b.addEventListener("click", () => show(b.dataset.view));
document.addEventListener("visibilitychange", () => { if (document.visibilityState === "visible") refreshInfo(false); });

async function boot() {
  try {
    await loadWindows();
    await refreshInfo(true);
    setInterval(() => loadWindows().catch(() => {}), 10000);
  } catch (e) { if (!/mã truy cập/.test(e.message)) toast(e.message, "err"); }
}
boot();
