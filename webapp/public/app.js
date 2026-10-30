// claude using revit - web app page. Plain JS, no build step.
// Every Revit action is a job: POST /api/w/<pid>/jobs {kind: task|command|tool, name, args}, then
// GET /api/jobs/<id> until it is done. Tasks = lib/tasks.py (the ribbon buttons), commands = the
// Revit MCP dynamic commands, tools = the Revit MCP add-in's own commands.
"use strict";

const $ = (s, el = document) => el.querySelector(s);
const S = {
  windows: [], pid: null, info: null, busy: null,
  catalog: null, tasks: null, filter: "all", picked: null, view: "tools", board: null, onBoard: [],
};
const store = {
  get(k, d) { try { const v = localStorage.getItem("cur." + k); return v === null ? d : JSON.parse(v); } catch { return d; } },
  set(k, v) { try { localStorage.setItem("cur." + k, JSON.stringify(v)); } catch { /* private mode */ } },
};

// ---- small DOM helper -----------------------------------------------------------------
function h(tag, attrs, ...kids) {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs || {})) {
    if (v === null || v === undefined || v === false) continue;
    if (k === "class") el.className = v;
    else if (k.startsWith("on")) el.addEventListener(k.slice(2), v);
    else if (k === "html") el.innerHTML = v;
    else el.setAttribute(k, v === true ? "" : v);
  }
  for (const k of kids.flat()) if (k !== null && k !== undefined && k !== false)
    el.append(k instanceof Node ? k : document.createTextNode(String(k)));
  return el;
}

function toast(msg, kind = "") {
  const t = h("div", { class: "toast " + kind }, msg);
  $("#toasts").append(t);
  setTimeout(() => t.remove(), kind === "err" ? 9000 : 4500);
}

// ---- API ------------------------------------------------------------------------------
async function api(path, opts = {}) {
  const r = await fetch(path, { credentials: "same-origin", headers: { "content-type": "application/json" }, ...opts });
  if (r.status === 401) { showLogin(); throw new Error("Cần mã truy cập"); }
  const j = await r.json().catch(() => ({ error: "bad response" }));
  if (!r.ok && !j.busy) throw new Error(j.error || r.statusText);
  return j;
}

function showLogin() {
  $("#login").classList.remove("hidden");
  $("#login-code").focus();
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
    refreshInfo();
  }
}

// run + show the result in a box + toast (reads: no "done" toast); never throws
const READS = new Set(["print_sets_status", "schedule_sheets", "list_tasks"]);
async function act(box, kind, name, args, label, after) {
  box.replaceChildren(h("span", { class: "busy" }, "Đang chạy " + (label || name) + "…"));
  try {
    const res = await run(kind, name, args, label);
    box.replaceChildren(after ? after(res) || resultView(res) : resultView(res));
    if (!READS.has(name) && !(args && (args.dryRun || args.list))) toast((label || name) + ": xong", "ok");
    return res;
  } catch (e) {
    box.replaceChildren(resultView(e.result || { error: e.message }));
    toast((label || name) + ": " + e.message, "err");
    return null;
  }
}

function resultView(res) {
  const wrap = h("div", { class: "result" });
  if (res && typeof res === "object" && res.error) wrap.append(h("div", { class: "err" }, res.error));
  wrap.append(h("pre", {}, JSON.stringify(res, null, 2)));
  return wrap;
}

function setBusy(label) {
  S.busy = label;
  const b = $("#busy");
  b.className = label ? "busy" : "busy hidden";
  b.textContent = label ? "Revit: " + label : "";
}

function confirmRun(what) {
  const doc = S.info && S.info.doc ? S.info.doc.title : "?";
  return window.confirm(`${what}\n\nModel: ${doc}\nTiếp tục?`);
}

// ---- windows + title block ----------------------------------------------------------------
async function loadWindows() {
  const { windows } = await api("/api/windows");
  S.windows = windows;
  if (!windows.find(w => String(w.pid) === String(S.pid))) {
    const last = store.get("pid", null);
    S.pid = (windows.find(w => String(w.pid) === String(last)) || windows[0] || {}).pid || null;
  }
  renderWindows();
}

function renderWindows() {
  const el = $("#windows");
  el.replaceChildren();
  if (!S.windows.length) {
    el.append(h("span", { class: "tag todo" }, "Không có cửa sổ Revit nào có cầu nối"));
    return;
  }
  for (const w of S.windows) {
    const info = String(w.pid) === String(S.pid) && S.info ? S.info : w.info;
    const title = info && info.doc ? info.doc.title : "(chưa mở model)";
    const run = w.ping && w.ping.running ? " · đang chạy " + w.ping.running.name : "";
    el.append(h("button", { class: "win" + (String(w.pid) === String(S.pid) ? " on" : ""),
      onclick: () => { S.pid = w.pid; store.set("pid", w.pid); S.info = null; S.tasks = null; S.board = null; renderWindows(); refreshInfo(true); } },
      title, h("small", {}, `Revit ${w.revit} · PID ${w.pid}${run}`)));
  }
}

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
      $("#context").classList.add("hidden");
      renderTitleBlock();
      renderWindows();
      if (rebuild || docChanged) { S.board = null; renderTools(); loadBoard(); }
      else if (viewChanged) loadBoard();
      else updateSelection();
    } catch (e) {
      const n = $("#context");
      n.textContent = "Revit đang bận hoặc có hộp thoại đang mở: " + e.message;
      n.classList.remove("hidden");
    }
  }
  infoTimer = setTimeout(() => refreshInfo(false), 6000);
}

function renderTitleBlock() {
  const i = S.info;
  $("#tb-model").textContent = i && i.doc ? i.doc.title + (i.doc.modified ? " *" : "") : "—";
  $("#tb-model").title = i && i.docs ? "Đang mở: " + i.docs.join(", ") : "";
  $("#tb-view").textContent = i && i.view ? i.view.name : "—";
  $("#tb-view").title = i && i.view ? i.view.type : "";
  $("#tb-sel").textContent = i ? String(i.selection.length) : "0";
  $("#tb-user").textContent = (i && i.user) || "—";
  $("#tb-date").textContent = new Date().toLocaleDateString("vi-VN");
  updateSelection();
}

function updateSelection() {
  const n = S.info ? S.info.selection.length : 0;
  $("#tb-sel").textContent = String(n);
  for (const el of document.querySelectorAll("[data-sel]")) el.textContent = String(n);
}

// ---- status board: what each tool has left to do (read only, one call) ---------------------
async function loadBoard() {
  if (!S.pid || !S.info || !S.info.doc) return;
  try {
    S.board = await run("task", "status_board", {}, "Kiểm tra trạng thái", true);
  } catch (e) {
    S.board = { error: e.message };
  }
  applyStates();
}
$("#btn-board").addEventListener("click", () => loadBoard());

// the drawing lists that count: the ones with a print set of their name (else all of them)
function officialLists(b) {
  const named = new Set(b.printSets.filter(r => r.setExists).map(r => r.schedule));
  return b.columns.filter(r => !named.size || named.has(r.schedule));
}

// state of one card: {level: "todo" | "done" | "idle", note, count}
const STATE = {
  printSet(b) {
    const sets = b.printSets.filter(r => r.setExists);
    const off = sets.filter(r => !r.inSync);
    if (off.length) return { level: "todo", count: off.length,
      note: "Print set lệch danh mục: " + off.map(r => `${r.schedule} (+${r.add} −${r.remove})`).join(", ") };
    if (sets.length) return { level: "done", note: `${sets.length} print set khớp danh mục` };
    return { level: "idle", note: "Chưa có print set nào mang tên danh mục" };
  },
  columns(b) {
    const rows = officialLists(b);
    const off = rows.filter(r => !r.inSync && !r.error);
    if (off.length) return { level: "todo", count: off.length,
      note: "Chưa đúng chuẩn: " + off.map(r => `${r.schedule} (${r.addColumns} cột, ${r.numbersToWrite} số No.)`).join(", ") };
    if (rows.length) return { level: "done", note: `${rows.length} danh mục đúng chuẩn cột` };
    return { level: "idle", note: "Model chưa có schedule danh mục bản vẽ" };
  },
  untagged(b) {
    const u = b.untagged;
    const left = u ? Object.entries(u.counts).filter(([, n]) => n) : [];
    if (left.length) return { level: "todo", count: left.reduce((s, [, n]) => s + n, 0),
      note: `${u.view}: ` + left.map(([k, n]) => `${n} ${KIND_LABEL[k]}`).join(", ") + " chưa tag" };
    if (u && b.untaggedMarks) return { level: "todo", count: b.untaggedMarks,
      note: `View đã tag đủ, còn ${b.untaggedMarks} vùng tô cam của bạn trong model` };
    if (u) return { level: "done", note: `${u.view}: đã tag đủ` };
    return { level: "idle", note: "View đang mở không phải mặt bằng / mặt cắt / mặt đứng" };
  },
  highlight(b) {
    if (b.highlights) return { level: "todo", count: b.highlights, note: `Còn ${b.highlights} vùng highlight đỏ của bạn trong model` };
    return { level: "idle", note: "Không còn highlight nào của bạn" };
  },
  idle() { return { level: "idle" }; },
};

function applyStates() {
  const b = S.board;
  let todo = 0;
  for (const c of document.querySelectorAll(".card[data-state]")) {
    const fn = STATE[c.dataset.state] || STATE.idle;
    const st = !b ? { level: "idle", note: "Đang kiểm tra…" } : b.error ? { level: "idle", note: "Không kiểm tra được: " + b.error } : fn(b);
    setCardState(c, st);
    if (st.level === "todo") todo++;
  }
  document.title = (todo ? `(${todo}) ` : "") + "claude using revit";
  $("#core-n").textContent = b && !b.error ? String(todo) : "–";
  $("#core").classList.toggle("todo", todo > 0);
  for (const f of S.onBoard) f(b);
}

function setCardState(c, st) {
  c.classList.remove("todo", "done", "idle");
  c.classList.add(st.level);
  const s = c.querySelector(".status");
  s.className = "status " + st.level;
  s.replaceChildren(...[st.level === "done" ? h("span", { class: "ok" }, "[ OK ]") : null,
    st.note ? h("span", {}, st.note) : null].filter(Boolean));
  c.querySelector(".count").textContent = st.level === "todo" ? (st.count > 99 ? "99+" : String(st.count || "")) : "";
}

// ---- tools: one solid per ribbon panel --------------------------------------------------------
const GROUP_NAME = { IN: "In ấn", TAG: "Kiểm tra tag", HL: "Đánh dấu", XU: "Xuất", "3D": "3D" };

// card(no, group code, icon, title, subtitle, stateKey, ...body)
function card(no, code, icon, title, sub, stateKey, ...body) {
  const c = h("section", { class: "card idle", "data-state": stateKey },
    h("i", { class: "scan", "aria-hidden": "true" }),
    h("div", { class: "head" },
      h("div", { class: "head-text" },
        h("span", { class: "idx" }, `0${no} · ${GROUP_NAME[code] || code}`),
        h("h3", {}, title), h("div", { class: "sub" }, sub)),
      h("img", { src: "/icon/" + icon + ".png", alt: "" }),
      h("span", { class: "count" }),
      h("button", { class: "close", title: "Đóng (Esc)", onclick: e => { e.stopPropagation(); unfocusCard(); } }, "✕")),
    h("div", { class: "status idle" }), h("div", { class: "body" }, ...body));
  // 3D board: a tap lifts the slab out of the board, flat and full size, to work in it
  c.addEventListener("click", () => { if (document.body.classList.contains("mode-3d") && !c.classList.contains("focus")) focusCard(c); });
  return c;
}

// ---- 3D board of acrylic slabs / flat grid --------------------------------------------------
let focused = null, holder = null;
function focusCard(c) {
  unfocusCard();
  holder = h("div", { class: "holder", style: `height:${c.offsetHeight}px` });
  c.replaceWith(holder);
  focused = c;
  c.classList.add("focus");
  $("#focus-layer").append(c);
  $("#focus-layer").classList.remove("hidden");
}
function unfocusCard() {
  if (!focused) return;
  focused.classList.remove("focus");
  if (holder && holder.isConnected) holder.replaceWith(focused);
  focused = holder = null;
  $("#focus-layer").classList.add("hidden");
}
function setMode(mode) {
  unfocusCard();
  document.body.classList.toggle("mode-3d", mode === "3d");
  $("#btn-mode").textContent = mode === "3d" ? "Phẳng" : "3D";
  store.set("mode", mode);
}
$("#btn-mode").addEventListener("click", () => setMode(document.body.classList.contains("mode-3d") ? "flat" : "3d"));
$("#focus-layer").addEventListener("click", e => { if (e.target.id === "focus-layer") unfocusCard(); });
document.addEventListener("keydown", e => { if (e.key === "Escape") unfocusCard(); });
setMode(store.get("mode", window.innerWidth > 900 ? "3d" : "flat"));

function renderTools() {
  unfocusCard();
  const g = $("#view-tools");
  S.onBoard = [];
  g.replaceChildren();
  if (!S.pid) return;
  if (S.info && !S.info.doc) { g.append(h("div", { class: "panel notice" }, "Cửa sổ Revit này chưa mở model nào.")); return; }
  g.append(printSetCard(), columnsCard(), untaggedCard(), highlightCard(), exportCard(), view3dCard());
  applyStates();
}

// after an action that may change the model: re-check the board
async function actAndCheck(...a) {
  const r = await act(...a);
  loadBoard();
  return r;
}

function printSetCard() {
  const list = h("div"), box = h("div");
  const draw = b => {
    if (!b || b.error) return list.replaceChildren();
    const t = h("table", { class: "table" }, h("tr", {}, h("th", {}, "Danh mục bản vẽ"), h("th", {}, "Sheet"), h("th", {}, "Print set"), h("th", {}, "")));
    for (const r of b.printSets) {
      const off = r.setExists && !r.inSync;
      const st = !r.setExists ? h("span", { class: "tag" }, "chưa có")
        : r.inSync ? h("span", { class: "tag ok" }, "khớp") : h("span", { class: "tag todo" }, `+${r.add} −${r.remove}`);
      t.append(h("tr", { class: off ? "todo" : "" }, h("td", {}, r.schedule), h("td", { class: "num" }, r.sheets), h("td", {}, st),
        h("td", {}, off ? h("button", { class: "btn primary small", onclick: () => sync(r.schedule, false) }, "Đồng bộ")
          : !r.setExists ? h("button", { class: "btn small", onclick: () => sync(r.schedule, false) }, "Tạo") : null)));
    }
    list.replaceChildren(t);
  };
  S.onBoard.push(draw);
  const sync = async (schedule, force) => {
    const res = await actAndCheck(box, "task", "print_set_sync", { schedule, force, detail: true }, "Print set theo DM: " + schedule);
    const row = res && res.schedules && res.schedules[0];
    if (row && /someone else/.test(row.status || "")) {
      box.append(h("div", { class: "row" }, h("span", { class: "tag todo" }, `Print set do ${row.lastChangedBy || "người khác"} sửa lần cuối`),
        h("button", { class: "btn danger", onclick: () => confirmRun("Thay print set của người khác?") && sync(schedule, true) }, "Vẫn thay")));
    }
  };
  return card("1", "IN", "Print Set Sync", "Print set theo danh mục", "Print set cùng tên = đúng các sheet của danh mục", "printSet",
    list, h("div", { class: "row" },
      h("button", { class: "btn primary", onclick: () => actAndCheck(box, "task", "print_set_sync", { all: true }, "Đồng bộ mọi print set") }, "Đồng bộ tất cả")),
    box);
}

function scheduleChecks(key) {
  const schedules = (S.info && S.info.schedules) || [];
  const want = new Set(store.get(key, []));
  const wrap = h("div", { class: "chips" });
  const chips = {};
  for (const s of schedules) {
    const b = chips[s] = h("button", { class: "chip" + (want.has(s) ? " on" : ""), onclick: () => {
      want.has(s) ? want.delete(s) : want.add(s); b.classList.toggle("on"); store.set(key, [...want]); } }, s);
    wrap.append(b);
  }
  wrap.values = () => [...want].filter(s => schedules.includes(s));
  wrap.mark = rows => {      // red = not standard yet; nothing picked yet = pick these lists
    for (const r of rows) if (chips[r.schedule]) chips[r.schedule].classList.toggle("todo", !r.inSync && !r.error);
    if (!want.size) for (const r of rows) if (chips[r.schedule]) { want.add(r.schedule); chips[r.schedule].classList.add("on"); }
  };
  return wrap;
}

function columnsCard() {
  const scheds = scheduleChecks("columns.schedules"), box = h("div");
  S.onBoard.push(b => b && !b.error && scheds.mark(officialLists(b)));
  const force = h("input", { type: "checkbox" });
  const pick = () => { const v = scheds.values(); if (!v.length) toast("Chọn ít nhất một danh mục", "err"); return v; };
  const param = h("select", { class: "grow" }, h("option", { value: "" }, "— tham số —"));
  const value = h("input", { class: "grow", placeholder: "giá trị (trống = xoá)" });
  return card("2", "IN", "Sheet List Columns", "Chuẩn cột danh mục", "Cột chuẩn + đánh số No. cho schedule danh mục", "columns",
    h("div", { class: "sect" }, "Danh mục"), scheds,
    h("div", { class: "row" },
      h("button", { class: "btn", onclick: () => { const s = pick(); s.length && act(box, "task", "sheet_list_columns", { schedules: s, dryRun: true }, "Xem trước cột chuẩn"); } }, "Xem trước"),
      h("button", { class: "btn primary", onclick: () => { const s = pick(); s.length && confirmRun("Thêm cột chuẩn + đánh số No.?") && actAndCheck(box, "task", "sheet_list_columns", { schedules: s, force: force.checked }, "Chuẩn cột danh mục"); } }, "Áp dụng"),
      h("label", { class: "check" }, force, "Cả danh mục người khác sửa")),
    h("div", { class: "sect" }, "Cột người ký"),
    h("div", { class: "row tight" },
      h("button", { class: "btn", onclick: () => { const s = pick(); s.length && act(box, "task", "sheet_list_signers", { schedules: s, show: true }, "Hiện cột ký"); } }, "Hiện"),
      h("button", { class: "btn", onclick: () => { const s = pick(); s.length && act(box, "task", "sheet_list_signers", { schedules: s, show: false }, "Ẩn cột ký"); } }, "Ẩn")),
    h("div", { class: "sect" }, "Điền một giá trị cho mọi sheet"),
    h("div", { class: "row tight" },
      h("button", { class: "btn", onclick: async () => {
        const s = pick(); if (!s.length) return;
        const r = await act(box, "task", "sheet_list_fill", { schedules: s }, "Đọc tham số");
        if (r && r.params) param.replaceChildren(h("option", { value: "" }, "— tham số —"), ...r.params.map(p => h("option", { value: p }, p)));
      } }, "Tải tham số"), param),
    h("div", { class: "row tight" }, value,
      h("button", { class: "btn", onclick: () => { const s = pick(); s.length && param.value && act(box, "task", "sheet_list_fill", { schedules: s, param: param.value, dryRun: true }, "Giá trị hiện tại"); } }, "Xem"),
      h("button", { class: "btn primary", onclick: () => { const s = pick(); s.length && param.value && confirmRun(`Ghi "${value.value}" vào ${param.value} của mọi sheet?`) && act(box, "task", "sheet_list_fill", { schedules: s, param: param.value, value: value.value }, "Điền tham số"); } }, "Ghi")),
    box);
}

const KINDS = [["Room", "Room"], ["Door", "Cửa đi"], ["Window", "Cửa sổ"], ["Wall", "Tường"], ["Floor", "Sàn (cao độ)"]];
const KIND_LABEL = Object.fromEntries(KINDS.map(([k, l]) => [k, l.toLowerCase()]));

function untaggedCard() {
  const box = h("div");
  let kinds = store.get("untagged.kinds", KINDS.map(k => k[0]));
  const chips = h("div", { class: "chips" });
  const drawChips = () => {
    const counts = S.board && S.board.untagged ? S.board.untagged.counts : {};
    chips.replaceChildren(...KINDS.map(([k, l]) => h("button", { class: "chip" + (kinds.includes(k) ? " on" : "") + (counts[k] ? " todo" : ""),
      onclick: () => { kinds = kinds.includes(k) ? kinds.filter(x => x !== k) : [...kinds, k]; store.set("untagged.kinds", kinds); drawChips(); } },
      l + (counts[k] ? ` · ${counts[k]}` : ""))));
  };
  S.onBoard.push(drawChips);
  drawChips();
  const scope = h("select", {},
    h("option", { value: "active" }, "View đang mở"),
    h("option", { value: "sel" }, "Các view / sheet đang chọn"),
    h("option", { value: "sheets" }, "Theo số sheet"));
  const sheets = h("input", { class: "grow hidden", placeholder: "số sheet, cách nhau dấu phẩy" });
  scope.addEventListener("change", () => sheets.classList.toggle("hidden", scope.value !== "sheets"));
  const args = () => {
    const a = { kinds };
    if (scope.value === "active") a.views = [S.info.view.id];
    else if (scope.value === "sel") a.views = S.info.selection;
    else a.sheets = sheets.value.split(/[,;\s]+/).filter(Boolean);
    return a;
  };
  const table = res => {
    const t = h("table", { class: "table" }, h("tr", {}, h("th", {}, "View"), ...kinds.map(k => h("th", {}, KINDS.find(x => x[0] === k)[1])), h("th", {}, "")));
    for (const r of res.views || []) {
      const ids = kinds.flatMap(k => r[k + "Ids"] || []);
      t.append(h("tr", { class: ids.length ? "todo" : "" }, h("td", {}, r.view), ...kinds.map(k => h("td", { class: "num" }, r[k] || "")),
        h("td", {}, ids.length ? h("button", { class: "btn small", onclick: () => run("task", "show_elements", { view: r.id, ids }, "Mở view").catch(e => toast(e.message, "err")) }, "Mở") : null)));
    }
    return t;
  };
  return card("3", "TAG", "Highlight Untagged", "Chưa tag", "Room, cửa, tường, sàn chưa có tag / cao độ", "untagged",
    h("div", { class: "row" }, scope, sheets), h("div", { class: "row tight" }, chips),
    h("div", { class: "row" },
      h("button", { class: "btn", onclick: () => act(box, "task", "untagged_scan", { ...args(), detail: true }, "Đếm chưa tag", table) }, "Đếm"),
      h("button", { class: "btn primary", onclick: () => actAndCheck(box, "task", "untagged_highlight", args(), "Tô cam chỗ chưa tag") }, "Tô cam"),
      h("button", { class: "btn", onclick: () => {
        const whole = window.confirm("Xoá vùng tô cam của bạn trong CẢ MODEL?\n(Cancel = chỉ phạm vi đang chọn)");
        const a = whole ? {} : args();
        delete a.kinds;
        actAndCheck(box, "task", "untagged_remove", a, "Xoá vùng tô");
      } }, "Xoá vùng tô")),
    box);
}

function highlightCard() {
  const box = h("div");
  const sel = () => (S.info ? S.info.selection : []);
  return card("4", "HL", "Highlight", "Highlight", "Vùng đỏ 50% quanh đối tượng đang chọn trong Revit", "highlight",
    h("div", { class: "row" },
      h("button", { class: "btn", onclick: () => {
        if (!sel().length) return toast("Chọn đối tượng trong Revit trước", "err");
        actAndCheck(box, "task", "highlight", { ids: sel(), detail: true }, "Highlight");
      } }, "Highlight ", h("span", { "data-sel": "" }, String(sel().length)), " đối tượng"),
      h("button", { class: "btn primary", onclick: () => actAndCheck(box, "task", "highlight_clear", {}, "Xoá highlight (cả model)") }, "Xoá highlight cả model"),
      h("button", { class: "btn", onclick: () => actAndCheck(box, "task", "highlight_clear", { view: S.info.view.id }, "Xoá highlight (view này)") }, "Chỉ view đang mở")),
    box);
}

function exportCard() {
  const box = h("div"), list = h("div", { class: "scroll" });
  const schedules = (S.info && S.info.schedules) || [];
  const sched = h("select", { class: "grow" }, ...schedules.map(s => h("option", { value: s }, s)));
  const last = store.get("export.schedule", null);
  if (S.info && S.info.view && schedules.includes(S.info.view.name)) sched.value = S.info.view.name;
  else if (last && schedules.includes(last)) sched.value = last;
  const filter = h("input", { class: "grow", type: "search", placeholder: "lọc số / tên sheet" });
  const folder = h("input", { class: "grow", placeholder: "thư mục xuất" });
  const groupBy = h("select", {}, h("option", { value: "" }, "Không chia thư mục con"));
  const setup = h("select", {}, h("option", { value: "" }, "Setup DWG mặc định"));
  const merged = h("input", { type: "checkbox", checked: true });
  const count = h("span", { class: "small" });
  let sheets = [], ticked = new Set();

  const counter = () => { count.textContent = `${ticked.size}/${sheets.length} sheet được chọn`; };
  const draw = () => {
    const q = filter.value.trim().toLowerCase();
    list.replaceChildren(...sheets.filter(s => !q || (s.number + " " + s.name).toLowerCase().includes(q)).map(s => {
      const cb = h("input", { type: "checkbox", checked: ticked.has(s.number) });
      cb.addEventListener("change", () => { cb.checked ? ticked.add(s.number) : ticked.delete(s.number); counter(); });
      return h("label", { class: "sheet-row" }, cb, h("span", { class: "no" }, s.number), h("span", { class: "nm" }, s.name));
    }));
    counter();
  };
  const load = async () => {
    if (!sched.value) return;
    store.set("export.schedule", sched.value);
    const r = await act(box, "task", "schedule_sheets", { schedule: sched.value }, "Đọc sheet", () => h("div"));
    if (!r) return;
    sheets = r.sheets;
    const sel = sheets.filter(s => s.selected);
    ticked = new Set((sel.length ? sel : sheets).map(s => s.number));
    if (!folder.value) folder.value = store.get("export.folder", r.pdfFolder || "");
    groupBy.replaceChildren(h("option", { value: "" }, "Không chia thư mục con"),
      ...r.groupBy.map(g => h("option", { value: g }, g === "browser" ? "Như Project Browser" : "Theo " + g)));
    groupBy.value = store.get("export.groupBy", "");
    draw();
  };
  sched.addEventListener("change", load);
  filter.addEventListener("input", draw);
  groupBy.addEventListener("change", () => store.set("export.groupBy", groupBy.value));
  folder.addEventListener("change", () => store.set("export.folder", folder.value));
  const chosen = () => sheets.filter(s => ticked.has(s.number)).map(s => s.number);
  const pdfArgs = extra => {
    const a = { schedule: sched.value, folder: folder.value || undefined, groupBy: groupBy.value || undefined, ...extra };
    if (chosen().length !== sheets.length) a.sheets = chosen();
    return a;
  };
  setTimeout(load, 0);
  return card("5", "XU", "Export PDF", "Xuất PDF / DWG", "Sheet của danh mục, mỗi sheet một file (không gộp)", "idle",
    h("div", { class: "row" }, sched, h("button", { class: "btn", onclick: load }, "Tải")),
    h("div", { class: "row tight" }, filter,
      h("button", { class: "btn small", onclick: () => { sheets.forEach(s => ticked.add(s.number)); draw(); } }, "Chọn hết"),
      h("button", { class: "btn small", onclick: () => { ticked.clear(); draw(); } }, "Bỏ hết")),
    list, h("div", { class: "row tight" }, count),
    h("div", { class: "row" }, folder, h("button", { class: "btn", onclick: () => pickFolder(folder.value, p => { folder.value = p; store.set("export.folder", p); }) }, "Duyệt…")),
    h("div", { class: "sect" }, "PDF"),
    h("div", { class: "row tight" }, groupBy,
      h("button", { class: "btn", onclick: () => chosen().length && act(box, "task", "pdf_export", pdfArgs({ dryRun: true }), "Kiểm tra trước khi xuất PDF") }, "Kiểm tra"),
      h("button", { class: "btn primary", onclick: () => chosen().length && confirmRun(`Xuất ${chosen().length} sheet ra PDF vào\n${folder.value}?\nRevit sẽ bận đến khi xong.`) && act(box, "task", "pdf_export", pdfArgs({}), "Xuất PDF") }, "Xuất PDF"),
      h("button", { class: "btn small", title: "Xoá bộ nhớ các view / sheet từng làm Revit crash (khi model đã sửa)", onclick: () => act(box, "task", "pdf_export", { schedule: sched.value, forget: true }, "Quên bộ nhớ crash") }, "Thử lại view từng crash")),
    h("div", { class: "sect" }, "DWG"),
    h("div", { class: "row tight" }, setup,
      h("button", { class: "btn small", onclick: async () => {
        const r = await act(box, "command", "export_sheets_dwg", { list: true }, "Đọc setup DWG");
        if (r && r.DwgSetups) setup.replaceChildren(h("option", { value: "" }, "Setup DWG mặc định"), ...r.DwgSetups.map(s => h("option", { value: s }, s)));
      } }, "Tải setup"),
      h("label", { class: "check" }, merged, "Gộp view, không xref"),
      h("button", { class: "btn primary", onclick: () => chosen().length && folder.value && confirmRun(`Xuất ${chosen().length} sheet ra DWG vào\n${folder.value}?`) && act(box, "command", "export_sheets_dwg",
        { sheetNumbers: chosen(), folder: folder.value, setupName: setup.value || undefined, mergedViews: merged.checked }, "Xuất DWG") }, "Xuất DWG")),
    box);
}

function view3dCard() {
  const box = h("div"), cut = h("input", { type: "checkbox" });
  return card("6", "3D", "3D From View", "3D theo view", "View 3D riêng, hộp cắt theo view đang mở", "idle",
    h("div", { class: "row" },
      h("button", { class: "btn primary", onclick: () => act(box, "task", "view_3d_from_view", { cut: cut.checked }, "3D theo view") }, "Mở 3D theo view đang mở"),
      h("label", { class: "check" }, cut, "Plan: dừng ở mặt cắt")),
    box);
}


// ---- folder picker ---------------------------------------------------------------------
async function pickFolder(start, done) {
  const body = $("#modal-body");
  const go = async p => {
    try {
      const r = await api("/api/fs?path=" + encodeURIComponent(p || ""));
      body.replaceChildren(h("h2", {}, "Chọn thư mục"), h("div", { class: "small" }, r.path || "Ổ đĩa"),
        h("div", { class: "dirs" },
          r.parent !== null ? h("div", { class: "dir", onclick: () => go(r.parent) }, "↑ ..") : null,
          ...r.dirs.map(d => h("div", { class: "dir", onclick: () => go(r.path ? r.path.replace(/\\$/, "") + "\\" + d : d) }, "📁 " + d))),
        h("div", { class: "row" },
          h("button", { class: "btn primary", disabled: !r.path, onclick: () => { close(); done(r.path); } }, "Chọn thư mục này"),
          h("button", { class: "btn", onclick: close }, "Huỷ")));
    } catch (e) { if (p) go(""); else toast(e.message, "err"); }
  };
  const close = () => $("#modal").classList.add("hidden");
  $("#modal").classList.remove("hidden");
  go(start);
}

// ---- all commands ------------------------------------------------------------------------
const GROUPS = [
  ["all", "Tất cả"], ["task", "Nút ribbon (task)"], ["own", "Lệnh claude-using-revit"],
  ["command", "Lệnh dynamic khác"], ["tool", "Lệnh gốc MCP"],
];

async function loadCatalog() {
  if (!S.catalog) S.catalog = await api("/api/catalog");
  if (!S.tasks && S.pid) {
    try {
      const r = await api(`/api/w/${S.pid}/tasks`);
      S.tasks = (r.tasks || []).map(t => ({ kind: "task", name: t.task, description: t.help,
        readOnly: /^\(RO\)/.test(t.help), inputSchema: null }));
    } catch { S.tasks = null; }
  }
  renderAll();
}

function items() {
  const c = S.catalog || { commands: [], tools: [] };
  return [...(S.tasks || []), ...c.commands, ...c.tools];
}

function group(it) { return it.kind === "command" && it.own ? "own" : it.kind; }

function renderAll() {
  const f = $("#filters");
  f.replaceChildren(...GROUPS.map(([k, l]) => h("button", { class: "chip" + (S.filter === k ? " on" : ""),
    onclick: () => { S.filter = k; renderAll(); } }, l)));
  const q = $("#search").value.trim().toLowerCase();
  const list = items().filter(it => (S.filter === "all" || group(it) === S.filter)
    && (!q || (it.name + " " + it.description).toLowerCase().includes(q)));
  $("#cmd-list").replaceChildren(...list.slice(0, 400).map(it => h("div", {
    class: "cmd" + (S.picked === it ? " on" : ""), onclick: () => { S.picked = it; renderAll(); renderDetail(); } },
    h("b", {}, it.name), " ", h("span", { class: "tag" + (it.readOnly ? " ok" : "") }, it.readOnly ? "chỉ đọc" : GROUPS.find(g => g[0] === group(it))[1]),
    h("small", { class: "muted" }, it.description))));
  if (!list.length) $("#cmd-list").append(h("p", { class: "muted" }, S.catalog ? "Không có lệnh nào khớp." : "Đang tải…"));
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
    input = h("textarea", { placeholder: "mỗi dòng một giá trị (hoặc cách nhau dấu phẩy)", rows: 3 });
    get = () => {
      const v = input.value.split(/[\n,]+/).map(s => s.trim()).filter(Boolean);
      return v.length ? (sch.items.type === "string" ? v : v.map(Number)) : undefined;
    };
  } else if (t === "string") {
    input = h("input", { placeholder: sch.default !== undefined ? String(sch.default) : "" });
    get = () => input.value === "" ? undefined : input.value;
  } else {
    input = h("textarea", { placeholder: "JSON", rows: 3 });
    get = () => { if (!input.value.trim()) return undefined; return JSON.parse(input.value); };
  }
  const el = h("div", { class: "field" },
    h("label", {}, name, required ? " *" : "", " ", h("code", {}, t || "any")),
    sch.description ? h("div", { class: "desc" }, sch.description) : null, input);
  return { el, get };
}

function renderDetail() {
  const it = S.picked, d = $("#cmd-detail");
  if (!it) return;
  const box = h("div");
  const fields = [];
  let raw = null;
  const props = it.inputSchema && it.inputSchema.properties;
  if (props && Object.keys(props).length) {
    const req = new Set(it.inputSchema.required || []);
    for (const [n, sch] of Object.entries(props)) fields.push([n, fieldFor(n, sch || {}, req.has(n))]);
  } else {
    raw = h("textarea", { rows: 6, placeholder: it.kind === "task" ? '{"dryRun": true}' : "{}" });
  }
  const args = () => {
    if (raw) return raw.value.trim() ? JSON.parse(raw.value) : {};
    const a = {};
    for (const [n, f] of fields) { const v = f.get(); if (v !== undefined) a[n] = v; }
    return a;
  };
  d.replaceChildren(
    h("h2", {}, it.name),
    h("div", { class: "row tight" }, h("span", { class: "tag" }, GROUPS.find(g => g[0] === group(it))[1]),
      it.readOnly ? h("span", { class: "tag ok" }, "chỉ đọc") : h("span", { class: "tag todo" }, "có thể sửa model / ghi file")),
    h("p", {}, it.description || h("span", { class: "muted" }, "(không có mô tả)")),
    ...fields.map(([, f]) => f.el), raw ? h("div", { class: "field" }, h("label", {}, "args (JSON)"), raw) : null,
    h("div", { class: "row" }, h("button", { class: "btn primary", onclick: () => {
      let a;
      try { a = args(); } catch (e) { return toast("JSON sai: " + e.message, "err"); }
      const safe = (it.readOnly && !/export/.test(it.name)) || a.dryRun === true || a.list === true;
      if (!safe && !confirmRun(`Chạy ${it.name}? Lệnh này có thể sửa model hoặc ghi file.`)) return;
      act(box, it.kind, it.name, a, it.name);
    } }, "Chạy")),
    box);
}

// ---- jobs ------------------------------------------------------------------------------
async function renderJobs() {
  const el = $("#view-jobs");
  const { jobs } = await api("/api/jobs");
  el.replaceChildren(...(jobs.length ? jobs : [null]).map(j => {
    if (!j) return h("p", { class: "muted" }, "Chưa chạy lệnh nào từ web app.");
    const secs = (((j.finished || Date.now()) - j.started) / 1000).toFixed(1);
    const st = j.state === "running" ? h("span", { class: "busy" }, "đang chạy")
      : j.state === "error" ? h("span", { class: "tag err" }, "lỗi") : h("span", { class: "tag ok" }, "xong");
    const more = h("div");
    return h("div", { class: "job", onclick: async () => {
      if (more.childNodes.length) return more.replaceChildren();
      const full = await api("/api/jobs/" + j.id);
      more.replaceChildren(h("div", { class: "small" }, "args: " + JSON.stringify(full.args)), resultView(full.result));
    } }, h("div", { class: "top" }, st, h("b", {}, j.label), h("small", { class: "muted" },
      `${new Date(j.started).toLocaleTimeString()} · ${secs}s · PID ${j.pid}`)), j.error ? h("div", { class: "small", style: "color:var(--red)" }, j.error) : null, more);
  }));
}

// ---- access from other devices ---------------------------------------------------------
$("#btn-access").addEventListener("click", async () => {
  const r = await api("/api/access");
  const body = $("#modal-body");
  body.replaceChildren(h("h2", {}, "Mở trên iPad / máy khác"),
    r.lan ? h("p", { class: "muted" }, "Cùng mạng LAN (hoặc Tailscale). Mở một link dưới đây một lần trên thiết bị đó; link chứa mã truy cập, đừng gửi ra ngoài.")
      : h("p", { class: "muted" }, `Web app đang chỉ mở trong máy này. Để dùng từ iPad: trong ${r.config} đổi "host" thành "0.0.0.0", rồi chạy lại install.ps1 -WebApp để khởi động lại máy chủ web.`),
    ...r.urls.map(u => h("div", { class: "row tight" }, h("code", { class: "grow" }, u),
      h("button", { class: "btn", onclick: () => navigator.clipboard && navigator.clipboard.writeText(u).then(() => toast("Đã chép")) }, "Chép"))),
    h("div", { class: "row" }, h("button", { class: "btn primary", onclick: () => $("#modal").classList.add("hidden") }, "Đóng")));
  $("#modal").classList.remove("hidden");
});
$("#modal").addEventListener("click", e => { if (e.target.id === "modal") $("#modal").classList.add("hidden"); });

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
