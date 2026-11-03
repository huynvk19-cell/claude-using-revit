// Bảng: any Revit schedule as an Excel-like grid (tasks schedule_list / schedule_table / schedule_table_write).
// Edits stay in the page until "Ghi vào Revit" (one Revit transaction, one Ctrl+Z there). Keys as in Excel:
// arrows (+Shift extend, +Ctrl jump), Enter / Tab, F2 or typing edits, Delete clears, Ctrl+C / X / V (TSV, to and
// from Excel), Ctrl+D / Ctrl+R fill, Ctrl+Z / Y, Ctrl+H find & replace, Ctrl+S write. Fill handle = series.
// Sorting and filters only change what the page shows, never the schedule.
"use strict";

const ROW_H = 30;
const G = { list: null, name: null, data: null, rows: [], cols: [], view: [], shown: [], anchor: null, focus: null,
  edit: null, undo: [], redo: [], sort: null, filters: {}, q: "", onlyChanged: false, showHidden: store.get("grid.hidden", false),
  unlock: false, widths: {}, drag: null, fill: null, newId: 0, loading: false };
const el = {};

// ---- values -------------------------------------------------------------------------------
const YES = ["yes", "y", "1", "true", "có", "co", "x", "✓"], NO = ["no", "n", "0", "false", "không", "khong", ""];
function normalize(col, v) {
  v = v == null ? "" : String(v);
  if (col.kind === "yesno") { const s = v.trim().toLowerCase(); return YES.includes(s) ? "Yes" : NO.includes(s) ? "No" : null; }
  if (col.kind === "int") { const s = v.trim(); return /^-?\d*$/.test(s) ? s : null; }
  if (col.kind === "number") return v.trim();
  return v.replace(/\r?\n/g, " ");
}
function natural(a, b) {
  return String(a).localeCompare(String(b), undefined, { numeric: true, sensitivity: "base" });
}
const lockOf = (row, col) => row.isNew ? null : col.source === "type" ? row.typeLock : row.lock;
const lockBy = (row, col) => col.source === "type" ? row.typeBy : row.by;
function canEdit(row, ci) {
  const col = G.cols[ci];
  if (!col.editable && !(row.isNew && col.role)) return false;
  if (row.isNew && col.source === "type") return false;
  const lk = lockOf(row, col);
  return !(lk === "busy" || (lk === "others" && !G.unlock));
}
const changed = (row, ci) => row.v[ci] !== row.o[ci];
const cell = (vr, vc) => ({ row: G.rows[G.view[vr]], ci: G.shown[vc] });

// ---- load ------------------------------------------------------------------------------------
function dirtyCount() {
  let n = 0;
  for (const r of G.rows) if (r.isNew) n++; else for (let i = 0; i < r.v.length; i++) if (changed(r, i)) n++;
  return n;
}
function leaveOk() { return !dirtyCount() || window.confirm("Bảng còn ô đã sửa chưa ghi vào Revit. Bỏ các thay đổi đó?"); }

async function tableOpen() {
  if (!el.root) build();
  if (!S.info || !S.info.doc) { el.title.textContent = "Chưa mở model"; return; }
  if (!G.list) {
    try { G.list = (await run("task", "schedule_list", {}, "Đọc danh sách schedule")).schedules; }
    catch (e) { return toast(e.message, "err"); }
    fillPicker();
    const want = new URLSearchParams(location.search).get("schedule") || store.get("grid.schedule", null);
    const active = S.info.view && G.list.find(s => s.id === S.info.view.id);
    const pick = active || G.list.find(s => s.name === want) || G.list.find(s => s.sheetList) || G.list[0];
    if (pick) { el.pick.value = String(pick.id); load(pick.id); }
  }
}
function tableDocChanged() { if (dirtyCount()) toast("Model khác đang mở: các ô chưa ghi đã bị bỏ", "err"); G.list = null; G.data = null; G.rows = []; if (el.root) { render(); if (S.view === "table") tableOpen(); } }

function fillPicker() {
  const cats = {};
  for (const s of G.list) (cats[s.sheetList ? "Danh mục bản vẽ" : s.category] ||= []).push(s);
  const keys = Object.keys(cats).sort((a, b) => a === "Danh mục bản vẽ" ? -1 : b === "Danh mục bản vẽ" ? 1 : natural(a, b));
  el.pick.replaceChildren(...keys.map(k => h("optgroup", { label: k }, ...cats[k].map(s => h("option", { value: s.id }, s.name)))));
}

async function load(id, keep) {
  if (G.loading) return;
  G.loading = true;
  el.title.textContent = "Đang đọc…";
  try {
    const d = await run("task", "schedule_table", { schedule: String(id) }, "Đọc bảng");
    G.data = d; G.name = d.schedule; G.cols = d.columns;
    el.pick.value = String(d.id);
    G.rows = d.rows.map(r => ({ ...r, o: r.v.slice() }));
    G.undo = []; G.redo = []; G.edit = null;
    if (!keep) { G.sort = null; G.filters = {}; G.q = ""; el.q.value = ""; G.anchor = G.focus = { r: 0, c: 0 }; G.onlyChanged = false; }
    G.widths = store.get("grid.w." + d.schedule, {});
    store.set("grid.schedule", d.schedule);
    refilter();
    el.scroll.focus({ preventScroll: true });
  } catch (e) { toast(e.message, "err"); el.title.textContent = "Không đọc được"; }
  finally { G.loading = false; }
}

// ---- which rows / columns show ----------------------------------------------------------------
function refilter() {
  G.shown = G.cols.map((c, i) => i).filter(i => G.showHidden || !G.cols[i].hidden);
  const q = G.q.trim().toLowerCase();
  const fl = Object.entries(G.filters).filter(([, f]) => f !== "").map(([ci, f]) => [Number(ci), f]);
  const keep = r => {
    if (r.isNew) return true;
    if (G.onlyChanged && !r.v.some((v, i) => v !== r.o[i])) return false;
    if (q && !G.shown.some(i => r.v[i].toLowerCase().includes(q))) return false;
    for (const [ci, f] of fl) {
      const v = r.v[ci].toLowerCase(), t = f.toLowerCase();
      if (t.startsWith("=")) { if (v !== t.slice(1)) return false; }
      else if (t.startsWith("!")) { if (t.length > 1 && v.includes(t.slice(1))) return false; if (t.length === 1 && v === "") return false; }
      else if (!v.includes(t)) return false;
    }
    return true;
  };
  let idx = G.rows.map((r, i) => i).filter(i => keep(G.rows[i]));
  if (G.sort) {
    const { ci, dir } = G.sort, num = ["int", "number"].includes(G.cols[ci].kind);
    const key = r => r.isNew ? 1 : 0;
    idx.sort((a, b) => {
      const ra = G.rows[a], rb = G.rows[b];
      if (key(ra) !== key(rb)) return key(ra) - key(rb);
      const x = ra.v[ci], y = rb.v[ci];
      const c = num && x !== "" && y !== "" && !isNaN(parseFloat(x)) && !isNaN(parseFloat(y)) ? parseFloat(x) - parseFloat(y) : natural(x, y);
      return dir * c || a - b;
    });
  }
  G.view = idx;
  clampSel();
  render(true);
}
function clampSel() {
  const R = Math.max(0, G.view.length - 1), C = Math.max(0, G.shown.length - 1);
  for (const p of [G.anchor, G.focus]) if (p) { p.r = Math.min(Math.max(p.r, 0), R); p.c = Math.min(Math.max(p.c, 0), C); }
  if (!G.anchor) G.anchor = { r: 0, c: 0 };
  if (!G.focus) G.focus = { ...G.anchor };
}
const selBox = () => ({ r0: Math.min(G.anchor.r, G.focus.r), r1: Math.max(G.anchor.r, G.focus.r), c0: Math.min(G.anchor.c, G.focus.c), c1: Math.max(G.anchor.c, G.focus.c) });

// ---- widths ------------------------------------------------------------------------------
function widthOf(ci) {
  const col = G.cols[ci];
  if (G.widths[col.key]) return G.widths[col.key];
  let n = col.heading.length;
  for (let i = 0; i < Math.min(G.rows.length, 300); i++) n = Math.max(n, G.rows[i].v[ci].length);
  return Math.round(Math.min(380, Math.max(70, n * 7.6 + 26)));
}

// ---- build the screen once ----------------------------------------------------------------------
function build() {
  const root = $("#view-table");
  el.root = root;
  el.pick = h("select", { class: "grow", title: "Schedule" });
  el.pick.addEventListener("change", () => { if (!leaveOk()) { el.pick.value = String(G.data ? G.data.id : ""); return; } load(el.pick.value); });
  el.q = h("input", { type: "search", class: "grow", placeholder: "Lọc mọi cột (Ctrl+F)" });
  el.q.addEventListener("input", () => { G.q = el.q.value; refilter(); });
  el.hidden = h("input", { type: "checkbox", checked: G.showHidden });
  el.hidden.addEventListener("change", () => { G.showHidden = el.hidden.checked; store.set("grid.hidden", G.showHidden); refilter(); });
  el.unlock = h("input", { type: "checkbox" });
  el.unlock.addEventListener("change", () => {
    if (el.unlock.checked && !window.confirm("Cho sửa cả phần tử do người khác tạo và sửa lần cuối?\nPhần tử đang bị người khác mượn vẫn bị khoá.")) { el.unlock.checked = false; return; }
    G.unlock = el.unlock.checked; render();
  });
  el.only = h("input", { type: "checkbox" });
  el.only.addEventListener("change", () => { G.onlyChanged = el.only.checked; refilter(); });
  el.title = h("div", { class: "g-title" });
  el.save = h("button", { class: "pill primary", onclick: save, title: "Ctrl+S" }, "Ghi vào Revit");
  el.undoB = h("button", { class: "pill small", onclick: () => undo(), title: "Ctrl+Z" }, "↶ Hoàn tác");
  el.redoB = h("button", { class: "pill small", onclick: () => redo(), title: "Ctrl+Y" }, "↷ Làm lại");
  el.addB = h("button", { class: "pill small", onclick: addRows, title: "Thêm sheet mới (danh mục bản vẽ)" }, "+ Sheet");
  el.file = h("input", { type: "file", accept: ".xlsx,.csv,.tsv,.txt", class: "hidden" });
  el.file.addEventListener("change", () => { const f = el.file.files[0]; el.file.value = ""; if (f) importFile(f); });
  el.table = h("table", { class: "grid" });
  el.scroll = h("div", { class: "g-scroll", tabindex: "0" }, el.table);
  el.status = h("div", { class: "g-status" });
  root.replaceChildren(
    h("div", { class: "glass g-bar" },
      h("div", { class: "row" }, el.pick,
        h("button", { class: "pill small", onclick: () => G.data && leaveOk() && load(G.data.id, true), title: "Đọc lại từ Revit" }, "Đọc lại"),
        el.q),
      h("div", { class: "row" },
        el.undoB, el.redoB,
        h("button", { class: "pill small", onclick: () => series(), title: "Đánh số tăng dần theo ô đầu (hoặc hai ô đầu) của vùng chọn" }, "Đánh số"),
        h("button", { class: "pill small", onclick: () => fillDown(), title: "Ctrl+D" }, "Fill ↓"),
        h("button", { class: "pill small", onclick: () => findReplace(), title: "Ctrl+H" }, "Tìm & thay"),
        h("button", { class: "pill small", onclick: () => formula(), title: "Ghi theo mẫu cho vùng chọn" }, "Công thức"),
        el.addB,
        h("button", { class: "pill small", onclick: () => exportFile("xlsx") }, "Xuất .xlsx"),
        h("button", { class: "pill small", onclick: () => exportFile("csv") }, "Xuất .csv"),
        h("button", { class: "pill small", onclick: () => el.file.click(), title: "Nhập .xlsx / .csv: khớp theo cột Id hoặc số sheet" }, "Nhập file"), el.file),
      h("div", { class: "row" },
        h("label", { class: "switch" }, el.hidden, h("i"), "Cột ẩn"),
        h("label", { class: "switch" }, el.only, h("i"), "Chỉ dòng đã sửa"),
        h("label", { class: "switch" }, el.unlock, h("i"), "Sửa phần tử người khác"),
        h("span", { class: "grow" }), el.title, el.save)),
    h("div", { class: "glass g-wrap" }, el.scroll), el.status);

  el.scroll.addEventListener("scroll", () => requestAnimationFrame(() => render()));
  el.scroll.addEventListener("keydown", onKey);
  el.scroll.addEventListener("mousedown", onDown);
  el.scroll.addEventListener("dblclick", e => { const td = e.target.closest("td[data-c]"); if (td) startEdit(null); });
  window.addEventListener("mousemove", onMove);
  window.addEventListener("mouseup", onUp);
  window.addEventListener("resize", () => render());
  document.addEventListener("copy", e => clip(e, false));
  document.addEventListener("cut", e => clip(e, true));
  document.addEventListener("paste", onPaste);
  window.addEventListener("beforeunload", e => { if (dirtyCount()) { e.preventDefault(); e.returnValue = ""; } });
}

// ---- render (only the rows in sight) ---------------------------------------------------------------
let lastRange = "";
function render(force) {
  if (!el.root || el.root.classList.contains("hidden") && !force) return;
  const d = G.data;
  const dirty = dirtyCount();
  el.save.textContent = dirty ? `Ghi vào Revit (${dirty})` : "Ghi vào Revit";
  el.save.disabled = !dirty;
  el.undoB.disabled = !G.undo.length; el.redoB.disabled = !G.redo.length;
  el.addB.classList.toggle("hidden", !(d && d.sheetList && G.cols.some(c => c.role === "sheetNumber")));
  if (!d) { el.table.replaceChildren(); el.status.textContent = ""; return; }
  el.title.textContent = "";
  const top = el.scroll.scrollTop, hgt = el.scroll.clientHeight || 600;
  const first = Math.max(0, Math.floor(top / ROW_H) - 8), last = Math.min(G.view.length, Math.ceil((top + hgt) / ROW_H) + 8);
  const key = [first, last, G.view.length, G.shown.join()].join("|");
  if (!force && key === lastRange && !G.redraw) { paintSel(); return; }
  lastRange = key; G.redraw = false;
  const sb = selBox();
  const cg = h("colgroup", {}, h("col", { style: "width:56px" }), ...G.shown.map(ci => h("col", { style: `width:${widthOf(ci)}px` })));
  const head = h("tr", {}, h("th", { class: "rn corner", title: "Chọn hết (Ctrl+A)", onclick: () => { G.anchor = { r: 0, c: 0 }; G.focus = { r: G.view.length - 1, c: G.shown.length - 1 }; paintSel(); } }, ""),
    ...G.shown.map((ci, vc) => {
      const c = G.cols[ci], s = G.sort && G.sort.ci === ci ? (G.sort.dir > 0 ? " ▲" : " ▼") : "";
      const tip = [c.name !== c.heading ? c.name : "", c.source === "type" ? "Tham số loại: sửa một ô là đổi cho mọi phần tử cùng loại" : "",
        !c.editable ? (c.kind === "computed" ? "Giá trị Revit tính (chỉ đọc)" : "Chỉ đọc") : "", c.hidden ? "Cột ẩn trong schedule" : ""].filter(Boolean).join("\n");
      return h("th", { "data-hc": vc, class: (c.editable ? "" : "ro") + (c.hidden ? " hid" : ""), title: tip || null },
        h("span", { class: "hd", onclick: e => { e.stopPropagation(); sortBy(ci); } }, c.heading, c.source === "type" ? h("em", {}, "loại") : null, s),
        h("span", { class: "rz", "data-rz": ci }));
    }));
  const filt = h("tr", { class: "flt" }, h("th", { class: "rn" }, ""),
    ...G.shown.map(ci => {
      const i = h("input", { value: G.filters[ci] || "", placeholder: "lọc", title: "chứa · =khớp hết · !không chứa · = (trống) · ! (có giá trị)" });
      i.addEventListener("input", () => { G.filters[ci] = i.value; const pos = i.selectionStart; refilter(); const again = el.table.querySelector(`tr.flt th:nth-child(${G.shown.indexOf(ci) + 2}) input`); if (again) { again.focus(); again.setSelectionRange(pos, pos); } });
      i.addEventListener("keydown", e => e.stopPropagation());
      return h("th", {}, i);
    }));
  const body = [h("tr", { class: "pad", style: `height:${first * ROW_H}px` })];
  for (let vr = first; vr < last; vr++) {
    const row = G.rows[G.view[vr]];
    body.push(h("tr", { class: row.isNew ? "new" : row.placeholder ? "ph" : null },
      h("td", { class: "rn", "data-rn": vr, title: row.isNew ? "Sheet mới (chưa ghi)" : `Id ${row.id}` }, row.isNew ? "+" : String(vr + 1)),
      ...G.shown.map((ci, vc) => {
        const c = G.cols[ci], lk = lockOf(row, c), ed = canEdit(row, ci);
        let cls = ed ? "" : "ro";
        if (changed(row, ci)) cls += " chg";
        if (vr >= sb.r0 && vr <= sb.r1 && vc >= sb.c0 && vc <= sb.c1) cls += " sel";
        if (vr === G.focus.r && vc === G.focus.c) cls += " act";
        const tip = lk === "busy" ? `Đang bị ${lockBy(row, c) || "người khác"} mượn` : lk === "others" && !G.unlock ? `Do ${lockBy(row, c) || "người khác"} làm (bật "Sửa phần tử người khác")` : changed(row, ci) ? `Trước: ${row.o[ci] || "(trống)"}` : null;
        return h("td", { "data-r": vr, "data-c": vc, class: cls || null, title: tip }, row.v[ci]);
      })));
  }
  body.push(h("tr", { class: "pad", style: `height:${(G.view.length - last) * ROW_H}px` }));
  el.table.replaceChildren(cg, h("thead", {}, head, filt), h("tbody", {}, ...body));
  paintSel();
  const ro = G.rows.filter(r => r.lock === "busy").length, oth = G.rows.filter(r => r.lock === "others").length;
  el.status.textContent = [`${G.view.length}/${G.rows.length} dòng`, `${G.shown.length} cột`,
    d.order === "schedule" ? "thứ tự như schedule" : "thứ tự theo cột sắp xếp của schedule",
    d.itemized ? "" : "schedule không itemize: mỗi phần tử một dòng",
    ro ? `${ro} dòng đang bị người khác mượn` : "", oth ? `${oth} dòng của người khác${G.unlock ? " (đang cho sửa)" : " (khoá)"}` : "",
    d.truncated ? "chỉ đọc 20000 dòng đầu" : "", selText()].filter(Boolean).join(" · ");
}

function selText() {
  const b = selBox(), n = (b.r1 - b.r0 + 1) * (b.c1 - b.c0 + 1);
  if (n < 2) return "";
  let sum = 0, cnt = 0;
  for (let r = b.r0; r <= b.r1; r++) for (let c = b.c0; c <= b.c1; c++) { const { row, ci } = cell(r, c); const x = parseFloat(row.v[ci]); if (!isNaN(x) && /^-?[\d.]+$/.test(row.v[ci].trim())) { sum += x; cnt++; } }
  return `chọn ${n} ô` + (cnt > 1 ? ` · tổng ${Math.round(sum * 1000) / 1000}` : "");
}

// selection paint without rebuilding rows
function paintSel() {
  const b = selBox(), fb = G.fill;
  for (const td of el.table.querySelectorAll("td[data-c]")) {
    const r = +td.dataset.r, c = +td.dataset.c;
    td.classList.toggle("sel", r >= b.r0 && r <= b.r1 && c >= b.c0 && c <= b.c1);
    td.classList.toggle("act", r === G.focus.r && c === G.focus.c);
    td.classList.toggle("fpv", !!fb && c >= b.c0 && c <= b.c1 && (fb.to > b.r1 ? r > b.r1 && r <= fb.to : r < b.r0 && r >= fb.to));
    const handle = r === b.r1 && c === b.c1 && !G.edit;
    if (handle && !td.querySelector(".fh")) td.append(h("span", { class: "fh", title: "Kéo để fill / đánh số" }));
    else if (!handle) td.querySelector(".fh")?.remove();
  }
  for (const th of el.table.querySelectorAll("th[data-hc]")) { const c = +th.dataset.hc; th.classList.toggle("sel", c >= b.c0 && c <= b.c1); }
  for (const td of el.table.querySelectorAll("td[data-rn]")) { const r = +td.dataset.rn; td.classList.toggle("sel", r >= b.r0 && r <= b.r1); }
  const s = el.status.textContent.split(" · chọn ")[0];
  const t = selText();
  el.status.textContent = t ? s + " · " + t : s;
}

function scrollTo(r) {
  const top = r * ROW_H, head = el.table.tHead ? el.table.tHead.offsetHeight : 60;
  if (top < el.scroll.scrollTop) el.scroll.scrollTop = top;
  else if (top + ROW_H + head > el.scroll.scrollTop + el.scroll.clientHeight) el.scroll.scrollTop = top + ROW_H + head - el.scroll.clientHeight;
  const td = el.table.querySelector(`td[data-r="${r}"][data-c="${G.focus.c}"]`);
  if (td) {
    const rn = 56;
    if (td.offsetLeft - rn < el.scroll.scrollLeft) el.scroll.scrollLeft = td.offsetLeft - rn;
    else if (td.offsetLeft + td.offsetWidth > el.scroll.scrollLeft + el.scroll.clientWidth) el.scroll.scrollLeft = td.offsetLeft + td.offsetWidth - el.scroll.clientWidth;
  }
}
function moveTo(r, c, extend) {
  G.focus = { r, c };
  if (!extend) G.anchor = { r, c };
  clampSel();
  render();
  scrollTo(G.focus.r);
  render();
}

function sortBy(ci) {
  const s = G.sort;
  G.sort = !s || s.ci !== ci ? { ci, dir: 1 } : s.dir > 0 ? { ci, dir: -1 } : null;
  refilter();
}

// ---- edits + undo ------------------------------------------------------------------------------
function setCell(row, ci, value, batch, skipped) {
  const col = G.cols[ci];
  if (!canEdit(row, ci)) { if (skipped) skipped.n++; return; }
  const v = normalize(col, value);
  if (v === null) { if (skipped) skipped.bad++; return; }
  const targets = col.source === "type" && row.type ? G.rows.filter(r => r.type === row.type) : [row];
  for (const r of targets) if (r.v[ci] !== v) { batch.push({ row: r, ci, before: r.v[ci], after: v }); r.v[ci] = v; }
}
function commit(batch, skipped) {
  if (batch.length) { G.undo.push(batch); if (G.undo.length > 200) G.undo.shift(); G.redo = []; }
  if (skipped && (skipped.n || skipped.bad)) toast([skipped.n ? `Bỏ qua ${skipped.n} ô chỉ đọc / bị khoá` : "", skipped.bad ? `${skipped.bad} giá trị sai kiểu` : ""].filter(Boolean).join(" · "), "err");
  G.redraw = true; render();
}
function undo() { const b = G.undo.pop(); if (!b) return; for (let i = b.length - 1; i >= 0; i--) b[i].row.v[b[i].ci] = b[i].before; G.redo.push(b); G.redraw = true; render(); }
function redo() { const b = G.redo.pop(); if (!b) return; for (const x of b) x.row.v[x.ci] = x.after; G.undo.push(b); G.redraw = true; render(); }

function eachSel(f) { const b = selBox(); for (let r = b.r0; r <= b.r1; r++) for (let c = b.c0; c <= b.c1; c++) f(r, c, b); }

function clearSel() { const batch = [], sk = { n: 0, bad: 0 }; eachSel((r, c) => { const { row, ci } = cell(r, c); if (row.v[ci] !== "") setCell(row, ci, "", batch, sk); }); commit(batch, sk); }

function fillDown() {
  const b = selBox(), batch = [], sk = { n: 0, bad: 0 };
  const src = b.r0 === b.r1 ? b.r0 - 1 : b.r0;
  if (src < 0) return;
  for (let c = b.c0; c <= b.c1; c++) { const v = cell(src, c).row.v[cell(src, c).ci]; for (let r = src + 1; r <= b.r1; r++) { const { row, ci } = cell(r, c); setCell(row, ci, v, batch, sk); } }
  commit(batch, sk);
}
function fillRight() {
  const b = selBox(), batch = [], sk = { n: 0, bad: 0 };
  const src = b.c0 === b.c1 ? b.c0 - 1 : b.c0;
  if (src < 0) return;
  for (let r = b.r0; r <= b.r1; r++) { const s = cell(r, src); for (let c = src + 1; c <= b.c1; c++) { const { row, ci } = cell(r, c); setCell(row, ci, s.row.v[s.ci], batch, sk); } }
  commit(batch, sk);
}

// next values after seeds: trailing number +step keeping its zero padding, else the seeds repeated
function seriesValues(seeds, count) {
  const m = seeds.map(s => /^(.*?)(\d+)(\D*)$/.exec(s));
  const same = m.every(x => x && x[1] === m[0][1] && x[3] === m[0][3]);
  if (!same) return Array.from({ length: count }, (_, i) => seeds[i % seeds.length]);
  const last = m[m.length - 1], width = last[2].length, pad = last[2].startsWith("0");
  const step = m.length > 1 ? Number(last[2]) - Number(m[m.length - 2][2]) : 1;
  return Array.from({ length: count }, (_, i) => {
    const n = Number(last[2]) + step * (i + 1);
    let t = String(Math.abs(n)); if (pad) t = t.padStart(width, "0");
    return last[1] + (n < 0 ? "-" : "") + t + last[3];
  });
}
// "Đánh số": the first cell (or the first two when both end in a number) of each selected column seeds the rest
function series() {
  const b = selBox(), batch = [], sk = { n: 0, bad: 0 };
  if (b.r1 === b.r0) return toast("Chọn từ ô đầu xuống tới ô cuối cần đánh số", "err");
  for (let c = b.c0; c <= b.c1; c++) {
    const v0 = cell(b.r0, c).row.v[cell(b.r0, c).ci], v1 = cell(b.r0 + 1, c).row.v[cell(b.r0 + 1, c).ci];
    const two = b.r1 > b.r0 + 1 && /\d+\D*$/.test(v0) && /\d+\D*$/.test(v1) && v0.replace(/\d+(\D*)$/, "$1") === v1.replace(/\d+(\D*)$/, "$1") && v0 !== v1;
    const seeds = two ? [v0, v1] : [v0];
    const vals = seriesValues(seeds, b.r1 - b.r0 + 1 - seeds.length);
    vals.forEach((v, i) => { const { row, ci } = cell(b.r0 + seeds.length + i, c); setCell(row, ci, v, batch, sk); });
  }
  commit(batch, sk);
}
function fillTo(to) {
  const b = selBox(), batch = [], sk = { n: 0, bad: 0 };
  for (let c = b.c0; c <= b.c1; c++) {
    const seeds = []; for (let r = b.r0; r <= b.r1; r++) seeds.push(cell(r, c).row.v[cell(r, c).ci]);
    if (to > b.r1) seriesValues(seeds, to - b.r1).forEach((v, i) => { const { row, ci } = cell(b.r1 + 1 + i, c); setCell(row, ci, v, batch, sk); });
    else seriesValues(seeds.slice().reverse(), b.r0 - to).forEach((v, i) => { const { row, ci } = cell(b.r0 - 1 - i, c); setCell(row, ci, v, batch, sk); });
  }
  if (to > b.r1) G.focus = { r: to, c: b.c1 }, G.anchor = { r: b.r0, c: b.c0 }; else G.focus = { r: to, c: b.c1 }, G.anchor = { r: b.r1, c: b.c0 };
  commit(batch, sk);
}

// ---- in-cell editor ------------------------------------------------------------------------------
function startEdit(text) {
  const { row, ci } = cell(G.focus.r, G.focus.c);
  if (!row) return;
  if (!canEdit(row, ci)) { const lk = lockOf(row, G.cols[ci]); return toast(lk === "busy" ? `Đang bị ${lockBy(row, G.cols[ci])} mượn` : lk === "others" ? `Phần tử của ${lockBy(row, G.cols[ci])}: bật "Sửa phần tử người khác"` : "Cột chỉ đọc", "err"); }
  scrollTo(G.focus.r); render();
  const td = el.table.querySelector(`td[data-r="${G.focus.r}"][data-c="${G.focus.c}"]`);
  if (!td) return;
  const col = G.cols[ci];
  let inp;
  if (col.kind === "yesno") {
    inp = h("select", { class: "g-edit" }, h("option", { value: "Yes" }, "Yes"), h("option", { value: "No" }, "No"));
    inp.value = text !== null ? (normalize(col, text) || row.v[ci] || "No") : row.v[ci] || "No";
  } else inp = h("input", { class: "g-edit", value: text !== null ? text : row.v[ci] });
  Object.assign(inp.style, { left: td.offsetLeft + "px", top: td.offsetTop + "px", width: Math.max(td.offsetWidth, 120) + "px", height: td.offsetHeight + "px" });
  G.edit = { inp, r: G.focus.r, c: G.focus.c, typed: text !== null };
  el.scroll.append(inp);
  inp.focus();
  if (inp.setSelectionRange) { const n = inp.value.length; inp.setSelectionRange(n, n); }
  inp.addEventListener("keydown", e => {
    e.stopPropagation();
    const k = e.key;
    if (k === "Escape") { endEdit(false); return; }
    if (k === "Enter") { e.preventDefault(); endEdit(true); moveTo(G.focus.r + (e.shiftKey ? -1 : 1), G.focus.c); return; }
    if (k === "Tab") { e.preventDefault(); endEdit(true); moveTo(G.focus.r, G.focus.c + (e.shiftKey ? -1 : 1)); return; }
    if (G.edit.typed && ["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight"].includes(k)) {
      e.preventDefault(); endEdit(true);
      moveTo(G.focus.r + (k === "ArrowDown") - (k === "ArrowUp"), G.focus.c + (k === "ArrowRight") - (k === "ArrowLeft"));
    }
  });
  inp.addEventListener("blur", () => { if (G.edit && G.edit.inp === inp) endEdit(true); });
}
function endEdit(keep) {
  const e = G.edit;
  if (!e) return;
  G.edit = null;
  if (keep) {
    const { row, ci } = cell(e.r, e.c);
    const batch = [], sk = { n: 0, bad: 0 };
    setCell(row, ci, e.inp.value, batch, sk);
    commit(batch, sk);
  }
  e.inp.remove();
  el.scroll.focus({ preventScroll: true });
  G.redraw = true; render();
}

// ---- keys + mouse ------------------------------------------------------------------------------
function onKey(e) {
  if (!G.data || G.edit) return;
  const k = e.key, ctrl = e.ctrlKey || e.metaKey, R = G.view.length - 1, C = G.shown.length - 1;
  const f = G.focus;
  const go = (r, c) => { e.preventDefault(); moveTo(r, c, e.shiftKey); };
  if (ctrl) {
    const lk = k.toLowerCase();
    if (lk === "z") { e.preventDefault(); return e.shiftKey ? redo() : undo(); }
    if (lk === "y") { e.preventDefault(); return redo(); }
    if (lk === "d") { e.preventDefault(); return fillDown(); }
    if (lk === "r") { e.preventDefault(); return fillRight(); }
    if (lk === "a") { e.preventDefault(); G.anchor = { r: 0, c: 0 }; G.focus = { r: R, c: C }; return paintSel(); }
    if (lk === "s") { e.preventDefault(); return save(); }
    if (lk === "h") { e.preventDefault(); return findReplace(); }
    if (lk === "f") { e.preventDefault(); return el.q.focus(); }
    if (k === "ArrowDown") return go(R, f.c);
    if (k === "ArrowUp") return go(0, f.c);
    if (k === "ArrowRight") return go(f.r, C);
    if (k === "ArrowLeft") return go(f.r, 0);
    if (k === "Home") return go(0, 0);
    if (k === "End") return go(R, C);
    return;                       // Ctrl+C / X / V: the copy / cut / paste events
  }
  const page = Math.max(1, Math.floor(el.scroll.clientHeight / ROW_H) - 2);
  switch (k) {
    case "ArrowDown": return go(f.r + 1, f.c);
    case "ArrowUp": return go(f.r - 1, f.c);
    case "ArrowRight": return go(f.r, f.c + 1);
    case "ArrowLeft": return go(f.r, f.c - 1);
    case "PageDown": return go(f.r + page, f.c);
    case "PageUp": return go(f.r - page, f.c);
    case "Home": return go(f.r, 0);
    case "End": return go(f.r, C);
    case "Tab": e.preventDefault(); return moveTo(f.r, f.c + (e.shiftKey ? -1 : 1));
    case "Enter": e.preventDefault(); return moveTo(f.r + (e.shiftKey ? -1 : 1), f.c);
    case "F2": e.preventDefault(); return startEdit(null);
    case "Delete": case "Backspace": e.preventDefault(); return clearSel();
  }
  if (k.length === 1 && !e.altKey) { e.preventDefault(); startEdit(k); }
}

function onDown(e) {
  if (e.button !== 0 || !G.data) return;
  const rz = e.target.closest("[data-rz]");
  if (rz) { e.preventDefault(); const ci = +rz.dataset.rz; G.drag = { rz: ci, x: e.clientX, w: widthOf(ci) }; return; }
  if (e.target.classList.contains("fh")) { e.preventDefault(); G.fill = { to: selBox().r1 }; return; }
  const td = e.target.closest("td[data-c]"), rn = e.target.closest("td[data-rn]"), th = e.target.closest("th[data-hc]");
  if (G.edit && !e.target.classList.contains("g-edit")) endEdit(true);
  if (td) {
    e.preventDefault(); el.scroll.focus({ preventScroll: true });
    const p = { r: +td.dataset.r, c: +td.dataset.c };
    G.focus = p; if (!e.shiftKey) G.anchor = { ...p };
    G.drag = { cells: true }; paintSel();
  } else if (rn) {
    e.preventDefault(); el.scroll.focus({ preventScroll: true });
    const r = +rn.dataset.rn;
    if (!e.shiftKey) G.anchor = { r, c: 0 };
    G.focus = { r, c: G.shown.length - 1 }; G.drag = { rows: true }; paintSel();
  } else if (th && !e.target.closest(".hd")) {
    el.scroll.focus({ preventScroll: true });
    const c = +th.dataset.hc;
    if (!e.shiftKey) G.anchor = { r: 0, c };
    G.focus = { r: G.view.length - 1, c }; paintSel();
  }
}
function onMove(e) {
  const d = G.drag;
  if (d && d.rz !== undefined) {
    G.widths[G.cols[d.rz].key] = Math.max(40, d.w + e.clientX - d.x);
    const col = el.table.querySelectorAll("col")[G.shown.indexOf(d.rz) + 1];
    if (col) col.style.width = G.widths[G.cols[d.rz].key] + "px";
    return;
  }
  if (!d && !G.fill) return;
  const t = document.elementFromPoint(e.clientX, e.clientY);
  const td = t && t.closest && t.closest("td[data-r]");
  if (!td) return;
  const r = +td.dataset.r;
  if (G.fill) { G.fill.to = r; paintSel(); return; }
  if (d.cells && td.dataset.c !== undefined) { G.focus = { r, c: +td.dataset.c }; paintSel(); }
  else if (d.rows) { G.focus = { r, c: G.shown.length - 1 }; paintSel(); }
}
function onUp() {
  const d = G.drag;
  G.drag = null;
  if (d && d.rz !== undefined) { store.set("grid.w." + G.name, G.widths); G.redraw = true; return render(); }
  if (G.fill) {
    const b = selBox(), to = G.fill.to;
    G.fill = null;
    if (to > b.r1 || to < b.r0) fillTo(to); else paintSel();
  }
}

// ---- clipboard (TSV, the way Excel copies) -------------------------------------------------------
const gridActive = () => S.view === "table" && G.data && !G.edit && document.activeElement === el.scroll;
function tsvOut(rows) { return rows.map(r => r.map(v => /[\t\n"]/.test(v) ? '"' + v.replace(/"/g, '""') + '"' : v).join("\t")).join("\r\n"); }
function clip(e, cut) {
  if (!gridActive()) return;
  const b = selBox(), rows = [];
  for (let r = b.r0; r <= b.r1; r++) { const line = []; for (let c = b.c0; c <= b.c1; c++) { const { row, ci } = cell(r, c); line.push(row.v[ci]); } rows.push(line); }
  e.clipboardData.setData("text/plain", tsvOut(rows));
  e.preventDefault();
  if (cut) clearSel();
}
function parseDelimited(text, sep) {
  const out = []; let row = [], v = "", q = false;
  text = text.replace(/^﻿/, "");
  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (q) { if (ch === '"') { if (text[i + 1] === '"') { v += '"'; i++; } else q = false; } else v += ch; }
    else if (ch === '"' && v === "") q = true;
    else if (ch === sep) { row.push(v); v = ""; }
    else if (ch === "\n" || ch === "\r") { if (ch === "\r" && text[i + 1] === "\n") i++; row.push(v); out.push(row); row = []; v = ""; }
    else v += ch;
  }
  if (v !== "" || row.length) { row.push(v); out.push(row); }
  return out;
}
function onPaste(e) {
  if (!gridActive()) return;
  const text = e.clipboardData.getData("text/plain");
  if (!text) return;
  e.preventDefault();
  const m = parseDelimited(text.replace(/\r?\n$/, ""), "\t");
  if (!m.length) return;
  const b = selBox(), mh = m.length, mw = Math.max(...m.map(r => r.length));
  const selH = b.r1 - b.r0 + 1, selW = b.c1 - b.c0 + 1;
  const tile = (mh === 1 && mw === 1) || (selH % mh === 0 && selW % mw === 0 && (selH > mh || selW > mw));
  const H = tile ? selH : mh, W = tile ? selW : mw;
  const batch = [], sk = { n: 0, bad: 0 };
  let outside = 0;
  for (let i = 0; i < H; i++) for (let j = 0; j < W; j++) {
    const r = b.r0 + i, c = b.c0 + j;
    if (r >= G.view.length || c >= G.shown.length) { outside++; continue; }
    const v = (m[i % mh] || [])[j % mw];
    if (v === undefined) continue;
    const { row, ci } = cell(r, c);
    setCell(row, ci, v, batch, sk);
  }
  G.focus = { r: Math.min(b.r0 + H - 1, G.view.length - 1), c: Math.min(b.c0 + W - 1, G.shown.length - 1) }; G.anchor = { r: b.r0, c: b.c0 };
  commit(batch, sk);
  if (outside) toast(`${outside} ô dán ra ngoài bảng bị bỏ` + (G.data.sheetList ? " (thêm dòng bằng + Sheet)" : ""), "err");
}

// ---- dialogs -------------------------------------------------------------------------------------
function modal(...kids) { $("#modal-body").replaceChildren(...kids); $("#modal").classList.remove("hidden"); }
function closeModal() { $("#modal").classList.add("hidden"); el.scroll.focus({ preventScroll: true }); }

function findReplace() {
  if (!G.data) return;
  const b = selBox(), many = b.r1 > b.r0 || b.c1 > b.c0;
  const find = h("input", { class: "grow", placeholder: "Tìm" }), repl = h("input", { class: "grow", placeholder: "Thay bằng" });
  const inSel = h("input", { type: "checkbox", checked: many }), whole = h("input", { type: "checkbox" }), cs = h("input", { type: "checkbox" }), rx = h("input", { type: "checkbox" });
  const info = h("div", { class: "dim" });
  const pattern = () => {
    const src = rx.checked ? find.value : find.value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    return new RegExp(whole.checked ? `^(?:${src})$` : src, cs.checked ? "g" : "gi");
  };
  const cells = () => {
    const out = [];
    const rr = inSel.checked ? [b.r0, b.r1] : [0, G.view.length - 1], cc = inSel.checked ? [b.c0, b.c1] : [0, G.shown.length - 1];
    for (let r = rr[0]; r <= rr[1]; r++) for (let c = cc[0]; c <= cc[1]; c++) out.push([r, c]);
    return out;
  };
  const next = () => {
    let re; try { re = pattern(); } catch (e) { return info.textContent = "Regex sai: " + e.message; }
    const all = cells(), start = all.findIndex(([r, c]) => r === G.focus.r && c === G.focus.c);
    for (let k = 1; k <= all.length; k++) {
      const [r, c] = all[(start + k) % all.length], { row, ci } = cell(r, c);
      re.lastIndex = 0;
      if (find.value && re.test(row.v[ci])) { G.focus = { r, c }; if (!inSel.checked) G.anchor = { r, c }; scrollTo(r); G.redraw = true; render(); info.textContent = `Ô ${r + 1} · ${G.cols[ci].heading}`; return; }
    }
    info.textContent = "Không thấy";
  };
  const all = () => {
    let re; try { re = pattern(); } catch (e) { return info.textContent = "Regex sai: " + e.message; }
    if (!find.value) return;
    const batch = [], sk = { n: 0, bad: 0 };
    for (const [r, c] of cells()) { const { row, ci } = cell(r, c); re.lastIndex = 0; if (re.test(row.v[ci])) { re.lastIndex = 0; setCell(row, ci, row.v[ci].replace(re, repl.value), batch, sk); } }
    commit(batch, sk);
    info.textContent = `Đã thay ${batch.length} ô` + (sk.n ? ` · bỏ qua ${sk.n} ô chỉ đọc / bị khoá` : "");
  };
  modal(h("h2", {}, "Tìm & thay"),
    h("div", { class: "row" }, find), h("div", { class: "row" }, repl),
    h("div", { class: "row" }, h("label", { class: "switch" }, inSel, h("i"), "Chỉ vùng chọn"), h("label", { class: "switch" }, whole, h("i"), "Khớp cả ô"),
      h("label", { class: "switch" }, cs, h("i"), "Hoa / thường"), h("label", { class: "switch" }, rx, h("i"), "Regex ($1 khi thay)")),
    info,
    h("div", { class: "row" }, h("button", { class: "pill", onclick: next }, "Tìm tiếp"), h("button", { class: "pill primary", onclick: all }, "Thay hết"),
      h("button", { class: "pill", onclick: closeModal }, "Đóng")));
  find.addEventListener("keydown", e => { if (e.key === "Enter") next(); });
  find.focus();
}

// template per selected cell: {v} value, {V} upper case, {n} / {n:000} position in the selection, {Heading} another column
function formula() {
  if (!G.data) return;
  const tpl = h("input", { class: "grow", placeholder: "vd: {v} - REV A · {n:000} · {SHEET NUMBER}-{n}", value: store.get("grid.formula", "") });
  const pv = h("div", { class: "dim" });
  const byHead = {}; G.cols.forEach((c, i) => { byHead[c.heading.toLowerCase()] = i; byHead[c.name.toLowerCase()] = i; });
  const apply = (t, row, ci, n) => t.replace(/\{([^{}]+)\}/g, (m, k) => {
    if (k === "v") return row.v[ci];
    if (k === "V") return row.v[ci].toUpperCase();
    if (k === "n") return String(n);
    if (/^n:0+$/.test(k)) return String(n).padStart(k.length - 2, "0");
    const i = byHead[k.toLowerCase()];
    return i === undefined ? m : row.v[i];
  });
  const draw = () => {
    const b = selBox(), { row, ci } = cell(b.r0, b.c0);
    pv.textContent = row ? `Ô đầu: "${row.v[ci]}" → "${apply(tpl.value, row, ci, 1)}"` : "";
  };
  tpl.addEventListener("input", draw);
  const go = () => {
    store.set("grid.formula", tpl.value);
    const b = selBox(), batch = [], sk = { n: 0, bad: 0 };
    let n = 0;
    for (let c = b.c0; c <= b.c1; c++) { n = 0; for (let r = b.r0; r <= b.r1; r++) { const { row, ci } = cell(r, c); setCell(row, ci, apply(tpl.value, row, ci, ++n), batch, sk); } }
    commit(batch, sk); closeModal();
  };
  tpl.addEventListener("keydown", e => { if (e.key === "Enter") go(); });
  modal(h("h2", {}, "Công thức cho vùng chọn"),
    h("p", { class: "dim" }, "{v} giá trị đang có · {V} chữ hoa · {n} hoặc {n:000} số thứ tự trong vùng chọn · {Tên cột} giá trị cột khác cùng dòng"),
    h("div", { class: "row" }, tpl), pv,
    h("div", { class: "row" }, h("button", { class: "pill primary", onclick: go }, "Áp dụng"), h("button", { class: "pill", onclick: closeModal }, "Huỷ")));
  draw(); tpl.focus();
}

function addRows() {
  if (!G.data || !G.data.sheetList) return;
  const n = parseInt(window.prompt("Thêm bao nhiêu sheet mới? (điền số sheet, tên rồi Ghi vào Revit)", "1"), 10);
  if (!(n > 0)) return;
  for (let i = 0; i < Math.min(n, 500); i++) {
    const v = G.cols.map(() => "");
    G.rows.push({ id: "new" + (++G.newId), isNew: true, v, o: v.slice() });
  }
  refilter();
  const sn = G.shown.findIndex(ci => G.cols[ci].role === "sheetNumber");
  moveTo(G.view.length - n, Math.max(0, sn));
}

// ---- write -------------------------------------------------------------------------------------
function pending() {
  const changes = [], add = [], seen = new Set(), lines = [];
  const label = row => { const sn = G.cols.findIndex(c => c.role === "sheetNumber"); return sn >= 0 ? row.v[sn] || row.o[sn] : (G.shown.length ? row.o[G.shown[0]] : row.id); };
  for (const row of G.rows) {
    if (row.isNew) {
      const sn = G.cols.findIndex(c => c.role === "sheetNumber"), nm = G.cols.findIndex(c => c.role === "sheetName");
      if (!row.v.some(v => v)) continue;
      const values = {};
      row.v.forEach((v, i) => { if (v && i !== sn && i !== nm && G.cols[i].editable) values[G.cols[i].key] = v; });
      add.push({ number: row.v[sn], name: nm >= 0 ? row.v[nm] : "", values });
      lines.push(`+ sheet ${row.v[sn] || "(chưa có số)"} ${nm >= 0 ? row.v[nm] : ""}`);
      continue;
    }
    row.v.forEach((v, ci) => {
      if (v === row.o[ci]) return;
      const col = G.cols[ci];
      const k = col.source === "type" && row.type ? `t${row.type}:${ci}` : `${row.id}:${ci}`;
      if (seen.has(k)) return;
      seen.add(k);
      changes.push({ id: row.id, col: col.key, value: v });
      lines.push(`${label(row)} · ${col.heading}${col.source === "type" ? " (loại)" : ""}: ${row.o[ci] || "(trống)"} → ${v || "(trống)"}`);
    });
  }
  return { changes, add, lines };
}

async function save() {
  if (!G.data || G.edit) return;
  const { changes, add, lines } = pending();
  if (!changes.length && !add.length) return;
  if (add.some(a => !a.number)) return toast("Sheet mới cần số sheet", "err");
  const types = changes.filter(c => G.cols.find(x => x.key === c.col).source === "type").length;
  const doc = S.info && S.info.doc ? S.info.doc.title : "?";
  modal(h("h2", {}, "Ghi vào Revit?"),
    h("p", { class: "dim" }, `Model ${doc} · schedule ${G.name}: ${changes.length} ô` + (add.length ? `, ${add.length} sheet mới` : "") +
      (types ? ` · ${types} ô là tham số loại (đổi cho mọi phần tử cùng loại)` : "") +
      (G.data.sheetList ? " · print set cùng tên sẽ đồng bộ theo danh mục" : "") + ". Thường hoàn tác bằng một Ctrl+Z trong Revit. Model cloud: lần đầu sửa một phần tử Revit phải mượn nó (có khi Reload Latest), có thể mất vài phút."),
    h("div", { class: "list scroll g-pv" }, ...lines.slice(0, 300).map(t => h("div", { class: "li" }, h("span", { class: "t" }, t))),
      lines.length > 300 ? h("div", { class: "li dim" }, `… ${lines.length - 300} dòng nữa`) : null),
    h("div", { class: "row" }, h("button", { class: "pill primary", onclick: async e => {
      e.target.disabled = true;
      try {
        const r = await run("task", "schedule_table_write", { schedule: String(G.data.id), changes, add, force: G.unlock, detail: true }, "Ghi bảng");
        const bad = [...(r.errors || []), ...(r.locked || []), ...(r.others || [])];
        const ps = r.printSet ? ` · print set: ${r.printSet.status}` : "";
        const made = r.add ? ` · tạo ${r.add.created.length} sheet` : "";
        modal(h("h2", {}, "Đã ghi"), h("p", {}, `${r.written || 0} ô${made}${ps}` + (r.same ? ` · ${r.same} ô vốn đã đúng` : "")),
          bad.length ? h("div", { class: "list scroll" }, ...bad.slice(0, 100).map(x => h("div", { class: "li work" }, h("span", { class: "t" },
            `Id ${x.id} · ${(G.cols.find(c => c.key === x.col) || {}).heading || x.col}: ${x.error || (x.by ? "khoá, của " + x.by : "khoá")}`)))) : null,
          h("div", { class: "row" }, h("button", { class: "pill primary", onclick: closeModal }, "Xong")));
        if (typeof loadBoard === "function" && G.data.sheetList) loadBoard();
        await load(G.data.id, true);
      } catch (err) {
        modal(h("h2", {}, "Chưa ghi được"), resultView(err.result || { error: err.message }), h("div", { class: "row" }, h("button", { class: "pill", onclick: closeModal }, "Đóng")));
      }
    } }, "Ghi"), h("button", { class: "pill", onclick: closeModal }, "Huỷ")));
}

// ---- export / import ----------------------------------------------------------------------------
function download(name, blob) {
  const a = h("a", { href: URL.createObjectURL(blob), download: name });
  document.body.append(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(a.href), 5000);
}
function exportRows() {
  const head = ["Id", ...G.shown.map(ci => G.cols[ci].heading)];
  return [head, ...G.view.map(i => G.rows[i]).filter(r => !r.isNew).map(r => [String(r.id), ...G.shown.map(ci => r.v[ci])])];
}
const safeName = s => s.replace(/[\\/:*?"<>|]+/g, "_");
function exportFile(kind) {
  if (!G.data) return;
  const rows = exportRows();
  if (kind === "csv") return download(safeName(G.name) + ".csv", new Blob(["﻿" + rows.map(r => r.map(v => /[",\n;]/.test(v) ? '"' + v.replace(/"/g, '""') + '"' : v).join(",")).join("\r\n")], { type: "text/csv" }));
  const numeric = [false, ...G.shown.map(ci => ["int", "number"].includes(G.cols[ci].kind))];
  download(safeName(G.name) + ".xlsx", xlsxWrite(G.name, rows, numeric));
}

async function importFile(f) {
  if (!G.data) return;
  let m;
  try {
    if (/\.xlsx$/i.test(f.name)) m = await xlsxRead(await f.arrayBuffer());
    else { const t = await f.text(); const first = t.split(/\r?\n/)[0]; const sep = first.includes("\t") ? "\t" : (first.split(";").length > first.split(",").length ? ";" : ","); m = parseDelimited(t, sep); }
  } catch (e) { return toast("Không đọc được file: " + e.message, "err"); }
  if (!m || m.length < 2) return toast("File không có dữ liệu", "err");
  const head = m[0].map(x => String(x || "").trim().toLowerCase());
  const map = head.map(hd => { const i = G.cols.findIndex(c => c.heading.toLowerCase() === hd); return i >= 0 ? i : G.cols.findIndex(c => c.name.toLowerCase() === hd); });
  const idCol = head.indexOf("id");
  // match rows by Id, else by the sheet number, else by the first mapped column unique in the table
  let keyCol = -1, keyFile = -1;
  if (idCol < 0) {
    const cand = [G.cols.findIndex(c => c.role === "sheetNumber"), ...map.filter(i => i >= 0)];
    for (const ci of cand) {
      if (ci < 0 || !map.includes(ci)) continue;
      const vals = G.rows.filter(r => !r.isNew).map(r => r.o[ci]);
      if (vals.every(v => v) && new Set(vals).size === vals.length) { keyCol = ci; keyFile = map.indexOf(ci); break; }
    }
    if (keyCol < 0) return toast("File cần cột Id (xuất từ bảng này) hoặc một cột có giá trị không trùng (vd số sheet)", "err");
  }
  const byKey = new Map(G.rows.filter(r => !r.isNew).map(r => [idCol >= 0 ? String(r.id) : r.o[keyCol], r]));
  const batch = [], sk = { n: 0, bad: 0 };
  const unmatched = [];
  for (const line of m.slice(1)) {
    if (!line.some(v => String(v || "").trim())) continue;
    const k = String(line[idCol >= 0 ? idCol : keyFile] || "").trim();
    const row = byKey.get(k);
    if (!row) { unmatched.push(line); continue; }
    map.forEach((ci, j) => { if (ci >= 0 && j !== idCol && line[j] !== undefined && String(line[j]) !== row.v[ci] && G.cols[ci].editable) setCell(row, ci, String(line[j]), batch, sk); });
  }
  let added = 0;
  const sn = G.cols.findIndex(c => c.role === "sheetNumber");
  if (unmatched.length && G.data.sheetList && sn >= 0 && map.includes(sn) && (idCol < 0 || unmatched.every(l => !String(l[idCol] || "").trim()))
      && window.confirm(`${unmatched.length} dòng trong file không có trong danh mục. Thêm thành sheet mới?`)) {
    for (const line of unmatched) {
      const v = G.cols.map(() => "");
      map.forEach((ci, j) => { if (ci >= 0 && j !== idCol && line[j] !== undefined) v[ci] = String(line[j]); });
      const row = { id: "new" + (++G.newId), isNew: true, v: G.cols.map(() => ""), o: G.cols.map(() => "") };
      G.rows.push(row);
      v.forEach((x, ci) => { if (x) { batch.push({ row, ci, before: "", after: x }); row.v[ci] = x; } });
      added++;
    }
  }
  refilter();
  commit(batch, sk);
  toast(`Nhập ${f.name}: ${batch.length} ô thay đổi` + (added ? `, ${added} sheet mới` : "") + (unmatched.length && !added ? `, ${unmatched.length} dòng không khớp` : "") + " · bấm Ghi vào Revit để ghi");
}

// ---- xlsx (zip by hand, no library) ----------------------------------------------------------------
const CRC = (() => { const t = new Uint32Array(256); for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1; t[n] = c >>> 0; } return t; })();
function crc32(b) { let c = 0xFFFFFFFF; for (let i = 0; i < b.length; i++) c = CRC[(c ^ b[i]) & 0xFF] ^ (c >>> 8); return (c ^ 0xFFFFFFFF) >>> 0; }
function zipStore(files) {
  const enc = new TextEncoder(), parts = [], central = [];
  let off = 0;
  for (const [name, text] of files) {
    const data = enc.encode(text), nm = enc.encode(name), crc = crc32(data);
    const lh = new DataView(new ArrayBuffer(30));
    lh.setUint32(0, 0x04034b50, true); lh.setUint16(4, 20, true); lh.setUint16(6, 0x0800, true); lh.setUint32(14, crc, true);
    lh.setUint32(18, data.length, true); lh.setUint32(22, data.length, true); lh.setUint16(26, nm.length, true);
    parts.push(new Uint8Array(lh.buffer), nm, data);
    const ch = new DataView(new ArrayBuffer(46));
    ch.setUint32(0, 0x02014b50, true); ch.setUint16(4, 20, true); ch.setUint16(6, 20, true); ch.setUint16(8, 0x0800, true); ch.setUint32(16, crc, true);
    ch.setUint32(20, data.length, true); ch.setUint32(24, data.length, true); ch.setUint16(28, nm.length, true); ch.setUint32(42, off, true);
    central.push(new Uint8Array(ch.buffer), nm);
    off += 30 + nm.length + data.length;
  }
  const size = central.reduce((s, p) => s + p.length, 0);
  const end = new DataView(new ArrayBuffer(22));
  end.setUint32(0, 0x06054b50, true); end.setUint16(8, files.length, true); end.setUint16(10, files.length, true); end.setUint32(12, size, true); end.setUint32(16, off, true);
  return new Blob([...parts, ...central, new Uint8Array(end.buffer)], { type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" });
}
const xesc = s => String(s).replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]).replace(/[\x00-\x08\x0b\x0c\x0e-\x1f]/g, "");
const colName = i => { let s = ""; for (i++; i; i = Math.floor((i - 1) / 26)) s = String.fromCharCode(65 + (i - 1) % 26) + s; return s; };
function xlsxWrite(title, rows, numeric) {
  const widths = rows[0].map((_, j) => Math.min(60, Math.max(8, ...rows.slice(0, 300).map(r => String(r[j] || "").length + 2))));
  const sheet = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">` +
    `<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>` +
    `<cols>${widths.map((w, j) => `<col min="${j + 1}" max="${j + 1}" width="${w}" customWidth="1"${j === 0 ? ' hidden="1"' : ""}/>`).join("")}</cols><sheetData>` +
    rows.map((r, i) => `<row r="${i + 1}">` + r.map((v, j) => {
      const ref = colName(j) + (i + 1), s = i === 0 ? ' s="1"' : "";
      if (i > 0 && numeric[j] && /^-?\d+(\.\d+)?$/.test(v)) return `<c r="${ref}"><v>${v}</v></c>`;
      return v === "" ? "" : `<c r="${ref}" t="inlineStr"${s}><is><t xml:space="preserve">${xesc(v)}</t></is></c>`;
    }).join("") + `</row>`).join("") + `</sheetData><autoFilter ref="A1:${colName(rows[0].length - 1)}${rows.length}"/></worksheet>`;
  const name = xesc(title.replace(/[\\/?*[\]:]/g, " ").slice(0, 31) || "Sheet1");
  return zipStore([
    ["[Content_Types].xml", `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>`],
    ["_rels/.rels", `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>`],
    ["xl/workbook.xml", `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="${name}" sheetId="1" r:id="rId1"/></sheets><definedNames><definedName name="_xlnm._FilterDatabase" localSheetId="0" hidden="1">'${name.replace(/'/g, "''")}'!$A$1:$${colName(rows[0].length - 1)}$${rows.length}</definedName></definedNames></workbook>`],
    ["xl/_rels/workbook.xml.rels", `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>`],
    ["xl/styles.xml", `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf/></cellStyleXfs><cellXfs count="2"><xf/><xf fontId="1" applyFont="1"/></cellXfs></styleSheet>`],
    ["xl/worksheets/sheet1.xml", sheet],
  ]);
}

async function unzip(buf) {
  const dv = new DataView(buf), u8 = new Uint8Array(buf), dec = new TextDecoder();
  let e = buf.byteLength - 22;
  while (e >= 0 && dv.getUint32(e, true) !== 0x06054b50) e--;
  if (e < 0) throw new Error("không phải file xlsx");
  const n = dv.getUint16(e + 10, true);
  let p = dv.getUint32(e + 16, true);
  const files = {};
  for (let i = 0; i < n; i++) {
    const method = dv.getUint16(p + 10, true), csize = dv.getUint32(p + 20, true), nl = dv.getUint16(p + 28, true), xl = dv.getUint16(p + 30, true), cl = dv.getUint16(p + 32, true), lo = dv.getUint32(p + 42, true);
    const name = dec.decode(u8.subarray(p + 46, p + 46 + nl));
    const start = lo + 30 + dv.getUint16(lo + 26, true) + dv.getUint16(lo + 28, true);
    files[name] = { method, data: u8.subarray(start, start + csize) };
    p += 46 + nl + xl + cl;
  }
  return async name => {
    const f = files[name];
    if (!f) return null;
    if (f.method === 0) return dec.decode(f.data);
    const s = new Blob([f.data]).stream().pipeThrough(new DecompressionStream("deflate-raw"));
    return await new Response(s).text();
  };
}
async function xlsxRead(buf) {
  const get = await unzip(buf), xml = s => new DOMParser().parseFromString(s, "application/xml");
  let path = "xl/worksheets/sheet1.xml";
  const wb = await get("xl/workbook.xml"), rels = await get("xl/_rels/workbook.xml.rels");
  if (wb && rels) {
    const first = xml(wb).getElementsByTagName("sheet")[0];
    const rid = first && (first.getAttribute("r:id") || first.getAttributeNS("http://schemas.openxmlformats.org/officeDocument/2006/relationships", "id"));
    const rel = [...xml(rels).getElementsByTagName("Relationship")].find(r => r.getAttribute("Id") === rid);
    if (rel) { const t = rel.getAttribute("Target"); path = t.startsWith("/") ? t.slice(1) : "xl/" + t; }
  }
  const sst = await get("xl/sharedStrings.xml");
  const strings = sst ? [...xml(sst).getElementsByTagName("si")].map(si => [...si.getElementsByTagName("t")].filter(t => t.parentNode.nodeName !== "rPh").map(t => t.textContent).join("")) : [];
  const sh = await get(path);
  if (!sh) throw new Error("không thấy sheet đầu tiên");
  const out = [];
  for (const row of xml(sh).getElementsByTagName("row")) {
    const r = Number(row.getAttribute("r")) - 1, line = [];
    for (const c of row.getElementsByTagName("c")) {
      const ref = c.getAttribute("r") || "", t = c.getAttribute("t");
      let j = 0; for (const ch of ref.replace(/\d+/g, "")) j = j * 26 + ch.charCodeAt(0) - 64;
      const vEl = c.getElementsByTagName("v")[0];
      let v = t === "s" ? strings[Number(vEl && vEl.textContent)] : t === "inlineStr" ? [...c.getElementsByTagName("t")].map(x => x.textContent).join("") : t === "b" ? (vEl && vEl.textContent === "1" ? "Yes" : "No") : vEl ? vEl.textContent : "";
      if (t !== "s" && t !== "inlineStr" && t !== "str" && /^-?\d+\.\d{9,}$/.test(v)) v = String(Math.round(Number(v) * 1e6) / 1e6);
      line[j - 1] = v == null ? "" : v;
    }
    out[r] = Array.from(line, x => x === undefined ? "" : x);
  }
  return Array.from(out, x => x || []);
}
