// 3D GPU viewer: draws the triangles of a Revit 3D view with WebGL2, entirely on the graphics card.
// The mesh comes from the pyRevit task view_mesh_export (served at /mesh/<name>.json|.bin); orbiting
// never asks Revit again. Click = pick (an id buffer read back from the GPU), "Chọn trong Revit" runs
// the task show_elements in the Revit window. No library: plain WebGL2.
"use strict";
const $ = s => document.querySelector(s);
const sleep = ms => new Promise(r => setTimeout(r, ms));
const TEX_W = 4096;                      // element -> category lookup texture width

async function api(url, opt = {}) {
  const r = await fetch(url, { credentials: "same-origin", headers: { "content-type": "application/json" }, ...opt });
  if (r.status === 401) { location.href = "/"; throw new Error("login"); }
  const j = await r.json().catch(() => ({}));
  if (!r.ok) throw new Error(j.error || r.statusText);
  return j;
}
function veil(text) { $("#veil").textContent = text || ""; $("#veil").classList.toggle("hidden", !text); }

// ---- Revit window + jobs (same API as the main page) -------------------------------------
let PID = new URLSearchParams(location.search).get("pid");
async function pid() {
  if (PID) return PID;
  const { windows } = await api("/api/windows");
  const w = windows.find(x => !x.error) || windows[0];
  if (!w) throw new Error("Chưa có cửa sổ Revit nào mở");
  return (PID = String(w.pid));
}
async function run(name, args, label) {
  const { id } = await api(`/api/w/${await pid()}/jobs`, { method: "POST", body: JSON.stringify({ kind: "task", name, args, label }) });
  for (let i = 0; ; i++) {
    await sleep(i < 10 ? 400 : 1000);
    const j = await api(`/api/jobs/${id}`);
    if (j.state === "running") continue;
    if (j.state === "error") throw new Error((j.result && j.result.error) || "lỗi");
    return j.result;
  }
}

// ---- small matrix helpers (column-major, like GL) ----------------------------------------
const sub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
const add = (a, b) => [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
const mul = (a, s) => [a[0] * s, a[1] * s, a[2] * s];
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const norm = a => mul(a, 1 / (Math.hypot(...a) || 1));
function perspective(fovy, aspect, n, f) {
  const t = 1 / Math.tan(fovy / 2), nf = 1 / (n - f);
  return [t / aspect, 0, 0, 0, 0, t, 0, 0, 0, 0, (f + n) * nf, -1, 0, 0, 2 * f * n * nf, 0];
}
function lookAt(eye, at, up) {
  const z = norm(sub(eye, at)), x = norm(cross(up, z)), y = cross(z, x);
  return [x[0], y[0], z[0], 0, x[1], y[1], z[1], 0, x[2], y[2], z[2], 0, -dot(x, eye), -dot(y, eye), -dot(z, eye), 1];
}
function mat4mul(a, b) {
  const o = new Array(16);
  for (let c = 0; c < 4; c++) for (let r = 0; r < 4; r++) {
    let s = 0; for (let k = 0; k < 4; k++) s += a[k * 4 + r] * b[c * 4 + k];
    o[c * 4 + r] = s;
  }
  return o;
}

// ---- GL setup --------------------------------------------------------------------------------
const canvas = $("#gl");
const gl = canvas.getContext("webgl2", { antialias: true, powerPreference: "high-performance" });
if (!gl) { veil("Trình duyệt này không có WebGL2"); throw new Error("no webgl2"); }

const VS = `#version 300 es
in vec3 aPos; in vec4 aCol; in uint aElem;
uniform mat4 uMVP; uniform highp usampler2D uElemCat; uniform sampler2D uCatVis;
out vec3 vPos; out vec4 vCol; flat out uint vElem;
void main() {
  uint c = texelFetch(uElemCat, ivec2(int(aElem % ${TEX_W}u), int(aElem / ${TEX_W}u)), 0).r;
  float vis = texelFetch(uCatVis, ivec2(int(c), 0), 0).r;
  vPos = aPos; vCol = aCol; vElem = aElem;
  gl_Position = vis < 0.5 ? vec4(2.0, 2.0, 2.0, 1.0) : uMVP * vec4(aPos, 1.0);
}`;
const FS = `#version 300 es
precision highp float; precision highp int;
in vec3 vPos; in vec4 vCol; flat in uint vElem;
uniform int uMode; uniform uint uSel; uniform vec3 uLight;
out vec4 o;
void main() {
  if (uMode == 2) {
    uint id = vElem + 1u;
    o = vec4(float(id & 255u), float((id >> 8) & 255u), float((id >> 16) & 255u), float((id >> 24) & 255u)) / 255.0;
    return;
  }
  bool tr = vCol.a < 0.99;
  if ((uMode == 0 && tr) || (uMode == 1 && !tr)) discard;
  vec3 n = normalize(cross(dFdx(vPos), dFdy(vPos)));
  float shade = 0.34 + 0.5 * abs(dot(n, uLight)) + 0.16 * abs(n.z);
  vec3 c = vCol.rgb * shade;
  if (vElem == uSel) c = mix(c, vec3(1.0, 0.62, 0.04), 0.6);
  o = vec4(c, vCol.a);
}`;
function shader(type, src) {
  const s = gl.createShader(type);
  gl.shaderSource(s, src); gl.compileShader(s);
  if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s));
  return s;
}
const prog = gl.createProgram();
gl.attachShader(prog, shader(gl.VERTEX_SHADER, VS));
gl.attachShader(prog, shader(gl.FRAGMENT_SHADER, FS));
gl.bindAttribLocation(prog, 0, "aPos"); gl.bindAttribLocation(prog, 1, "aCol"); gl.bindAttribLocation(prog, 2, "aElem");
gl.linkProgram(prog);
if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(prog));
const U = {};
for (const n of ["uMVP", "uElemCat", "uCatVis", "uMode", "uSel", "uLight"]) U[n] = gl.getUniformLocation(prog, n);

const M = { man: null, vao: null, count: 0, pos: null, elem: null, catVis: null, catTex: null, elemTex: null, bufs: [] };
const cam = { target: [0, 0, 0], yaw: -2.2, pitch: 0.55, dist: 50, fov: 45 * Math.PI / 180, radius: 50 };
let SEL = -1, dirty = true, pickFbo = null, pickSize = [0, 0];

function upload(man, bin) {
  for (const b of M.bufs) gl.deleteBuffer(b);
  if (M.vao) gl.deleteVertexArray(M.vao);
  for (const t of [M.catTex, M.elemTex]) if (t) gl.deleteTexture(t);
  const view = (k, T) => new T(bin, man.buffers[k][0], man.buffers[k][1] / T.BYTES_PER_ELEMENT);
  M.man = man; M.pos = view("position", Float32Array); M.elem = view("element", Uint32Array);
  const col = view("color", Uint8Array), idx = view("index", Uint32Array);
  M.vao = gl.createVertexArray(); gl.bindVertexArray(M.vao);
  const buf = (target, data) => { const b = gl.createBuffer(); gl.bindBuffer(target, b); gl.bufferData(target, data, gl.STATIC_DRAW); M.bufs.push(b); return b; };
  M.bufs = [];
  buf(gl.ARRAY_BUFFER, M.pos); gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 0, 0);
  buf(gl.ARRAY_BUFFER, col); gl.enableVertexAttribArray(1); gl.vertexAttribPointer(1, 4, gl.UNSIGNED_BYTE, true, 0, 0);
  buf(gl.ARRAY_BUFFER, M.elem); gl.enableVertexAttribArray(2); gl.vertexAttribIPointer(2, 1, gl.UNSIGNED_INT, 0, 0);
  buf(gl.ELEMENT_ARRAY_BUFFER, idx);
  gl.bindVertexArray(null);
  M.count = idx.length;
  // element -> category (R32UI) and category -> visible (R8)
  const n = man.elements.length, h = Math.max(1, Math.ceil(n / TEX_W));
  const ec = new Uint32Array(TEX_W * h);
  man.elements.forEach((e, i) => { ec[i] = e[1]; });
  M.elemTex = gl.createTexture(); gl.bindTexture(gl.TEXTURE_2D, M.elemTex);
  gl.texImage2D(gl.TEXTURE_2D, 0, gl.R32UI, TEX_W, h, 0, gl.RED_INTEGER, gl.UNSIGNED_INT, ec);
  setTexParams();
  M.catVis = new Uint8Array(Math.max(1, man.categories.length)).fill(255);
  M.catTex = gl.createTexture();
  uploadCatVis();
  SEL = -1;
}
function setTexParams() {
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
}
function uploadCatVis() {
  gl.bindTexture(gl.TEXTURE_2D, M.catTex);
  gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
  gl.texImage2D(gl.TEXTURE_2D, 0, gl.R8, M.catVis.length, 1, 0, gl.RED, gl.UNSIGNED_BYTE, M.catVis);
  setTexParams();
  dirty = true;
}

function bounds(filter) {
  const p = M.pos, e = M.elem, lo = [Infinity, Infinity, Infinity], hi = [-Infinity, -Infinity, -Infinity];
  for (let i = 0, v = 0; i < p.length; i += 3, v++) {
    if (filter !== undefined && e[v] !== filter) continue;
    for (let k = 0; k < 3; k++) { const x = p[i + k]; if (x < lo[k]) lo[k] = x; if (x > hi[k]) hi[k] = x; }
  }
  return lo[0] === Infinity ? null : [lo, hi];
}
function fit(b) {
  b = b || bounds();
  if (!b) return;
  cam.target = mul(add(b[0], b[1]), 0.5);
  const r = Math.max(0.5, Math.hypot(...sub(b[1], b[0])) / 2);
  cam.dist = r / Math.sin(cam.fov / 2) * 1.05;
  cam.radius = Math.max(cam.radius, r);
  dirty = true;
}

// ---- drawing ---------------------------------------------------------------------------------
function eye() {
  const c = Math.cos(cam.pitch);
  return add(cam.target, mul([c * Math.cos(cam.yaw), c * Math.sin(cam.yaw), Math.sin(cam.pitch)], cam.dist));
}
function mvp() {
  const w = canvas.width, h = canvas.height;
  const near = Math.max(0.02, cam.dist * 0.005), far = cam.dist + cam.radius * 4 + 10;
  return mat4mul(perspective(cam.fov, w / h, near, far), lookAt(eye(), cam.target, [0, 0, 1]));
}
function drawScene(mode) {
  gl.useProgram(prog);
  gl.uniformMatrix4fv(U.uMVP, false, mvp());
  gl.uniform1ui(U.uSel, SEL < 0 ? 0xffffffff : SEL);
  const l = norm(add(sub(eye(), cam.target), mul([0.3, -0.2, 1], cam.dist)));
  gl.uniform3fv(U.uLight, l);
  gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, M.elemTex); gl.uniform1i(U.uElemCat, 0);
  gl.activeTexture(gl.TEXTURE1); gl.bindTexture(gl.TEXTURE_2D, M.catTex); gl.uniform1i(U.uCatVis, 1);
  gl.bindVertexArray(M.vao);
  gl.enable(gl.DEPTH_TEST);
  if (mode === 2) {
    gl.disable(gl.BLEND); gl.depthMask(true); gl.uniform1i(U.uMode, 2);
    gl.drawElements(gl.TRIANGLES, M.count, gl.UNSIGNED_INT, 0);
  } else {
    gl.disable(gl.BLEND); gl.depthMask(true); gl.uniform1i(U.uMode, 0);
    gl.drawElements(gl.TRIANGLES, M.count, gl.UNSIGNED_INT, 0);
    gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA); gl.depthMask(false); gl.uniform1i(U.uMode, 1);
    gl.drawElements(gl.TRIANGLES, M.count, gl.UNSIGNED_INT, 0);
    gl.depthMask(true);
  }
  gl.bindVertexArray(null);
}
function frame() {
  const dpr = Math.min(window.devicePixelRatio || 1, 2);
  const w = Math.round(canvas.clientWidth * dpr), h = Math.round(canvas.clientHeight * dpr);
  if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; dirty = true; }
  if (dirty && M.vao) {
    dirty = false;
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    gl.viewport(0, 0, w, h);
    gl.clearColor(0, 0, 0, 0); gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    drawScene(0);
  }
  requestAnimationFrame(frame);
}
requestAnimationFrame(frame);

function pick(x, y) {
  // element index under a CSS pixel, read back from an id render; -1 for none
  const w = canvas.width, h = canvas.height, dpr = w / canvas.clientWidth;
  if (!pickFbo || pickSize[0] !== w || pickSize[1] !== h) {
    if (pickFbo) gl.deleteFramebuffer(pickFbo.fb), gl.deleteTexture(pickFbo.tex), gl.deleteRenderbuffer(pickFbo.depth);
    const fb = gl.createFramebuffer(), tex = gl.createTexture(), depth = gl.createRenderbuffer();
    gl.bindTexture(gl.TEXTURE_2D, tex); gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, w, h, 0, gl.RGBA, gl.UNSIGNED_BYTE, null); setTexParams();
    gl.bindRenderbuffer(gl.RENDERBUFFER, depth); gl.renderbufferStorage(gl.RENDERBUFFER, gl.DEPTH_COMPONENT24, w, h);
    gl.bindFramebuffer(gl.FRAMEBUFFER, fb);
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, tex, 0);
    gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.RENDERBUFFER, depth);
    pickFbo = { fb, tex, depth }; pickSize = [w, h];
  }
  gl.bindFramebuffer(gl.FRAMEBUFFER, pickFbo.fb);
  gl.viewport(0, 0, w, h);
  gl.clearColor(0, 0, 0, 0); gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
  drawScene(2);
  const px = new Uint8Array(4);
  gl.readPixels(Math.round(x * dpr), Math.round(h - y * dpr), 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, px);
  gl.bindFramebuffer(gl.FRAMEBUFFER, null);
  dirty = true;
  return (px[0] | (px[1] << 8) | (px[2] << 16) | (px[3] << 24)) - 1;
}

// ---- input: mouse + touch (pointer events) ----------------------------------------------------
const ptrs = new Map();
let down = null, lastPinch = null;
function orbit(dx, dy) {
  cam.yaw -= dx * 0.006;
  cam.pitch = Math.max(-1.55, Math.min(1.55, cam.pitch + dy * 0.006));
  dirty = true;
}
function pan(dx, dy) {
  const f = norm(sub(cam.target, eye())), r = norm(cross(f, [0, 0, 1])), u = cross(r, f);
  const k = 2 * cam.dist * Math.tan(cam.fov / 2) / canvas.clientHeight;
  cam.target = add(cam.target, add(mul(r, -dx * k), mul(u, dy * k)));
  dirty = true;
}
function zoomAt(x, y, factor) {
  // move the camera along the ray under the cursor; the orbit centre comes along
  const e = eye(), f = norm(sub(cam.target, e)), r = norm(cross(f, [0, 0, 1])), u = cross(r, f);
  const t = Math.tan(cam.fov / 2), w = canvas.clientWidth, h = canvas.clientHeight;
  const nx = (2 * x / w - 1) * t * w / h, ny = (1 - 2 * y / h) * t;
  const ray = norm(add(f, add(mul(r, nx), mul(u, ny))));
  const step = cam.dist * (1 - factor);
  const e2 = add(e, mul(ray, step));
  cam.dist = Math.max(0.05, cam.dist * factor);
  cam.target = add(e2, mul(f, cam.dist));
  dirty = true;
}
canvas.addEventListener("contextmenu", e => e.preventDefault());
canvas.addEventListener("pointerdown", e => {
  canvas.setPointerCapture(e.pointerId);
  ptrs.set(e.pointerId, [e.clientX, e.clientY]);
  down = { x: e.clientX, y: e.clientY, moved: 0, button: e.button, shift: e.shiftKey };
  lastPinch = null;
});
canvas.addEventListener("pointermove", e => {
  if (!ptrs.has(e.pointerId)) return;
  const p = ptrs.get(e.pointerId), dx = e.clientX - p[0], dy = e.clientY - p[1];
  ptrs.set(e.pointerId, [e.clientX, e.clientY]);
  if (down) down.moved += Math.abs(dx) + Math.abs(dy);
  if (ptrs.size === 2) {
    const [a, b] = [...ptrs.values()], mid = [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2], d = Math.hypot(a[0] - b[0], a[1] - b[1]);
    if (lastPinch) {
      pan(mid[0] - lastPinch.mid[0], mid[1] - lastPinch.mid[1]);
      if (d > 0 && lastPinch.d > 0) zoomAt(mid[0], mid[1], lastPinch.d / d);
    }
    lastPinch = { mid, d };
    return;
  }
  if (down && (down.button === 2 || down.button === 1 || down.shift)) pan(dx, dy); else orbit(dx, dy);
});
function up(e) {
  ptrs.delete(e.pointerId);
  if (down && down.moved < 5 && ptrs.size === 0 && M.vao && down.button === 0) select(pick(e.clientX, e.clientY));
  if (ptrs.size === 0) down = null;
  lastPinch = null;
}
canvas.addEventListener("pointerup", up);
canvas.addEventListener("pointercancel", up);
canvas.addEventListener("wheel", e => {
  e.preventDefault();
  zoomAt(e.clientX, e.clientY, Math.exp(Math.max(-0.5, Math.min(0.5, e.deltaY * 0.0015))));
}, { passive: false });
canvas.addEventListener("dblclick", e => {
  const i = pick(e.clientX, e.clientY);
  if (i < 0) return;
  const b = bounds(i);
  if (b) { cam.target = mul(add(b[0], b[1]), 0.5); dirty = true; }
});
addEventListener("keydown", e => {
  if (e.key === "f" || e.key === "F") fit();
  if (e.key === "Escape") select(-1);
});

// ---- panels ------------------------------------------------------------------------------------
function el(tag, attrs = {}, ...kids) {
  const n = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (k.startsWith("on")) n.addEventListener(k.slice(2), v); else if (v !== null && v !== undefined && v !== false) n.setAttribute(k, v === true ? "" : v);
  }
  for (const k of kids.flat()) if (k !== null && k !== undefined) n.append(k.nodeType ? k : String(k));
  return n;
}
function select(i) {
  SEL = i; dirty = true;
  const box = $("#pick");
  if (i < 0 || !M.man || !M.man.elements[i]) { box.classList.add("hidden"); return; }
  const [id, ci, link] = M.man.elements[i];
  const cat = M.man.categories[ci];
  const msg = el("div", { class: "dim" });
  const go = el("button", { class: "vbtn primary", disabled: link ? true : null, onclick: async () => {
    msg.textContent = "Đang chọn…";
    try { await run("show_elements", { view: M.man.viewId, ids: [id] }, "Chọn trong Revit"); msg.textContent = "Đã chọn trong Revit"; }
    catch (err) { msg.textContent = err.message; }
  } }, "Chọn trong Revit");
  const hide = el("button", { class: "vbtn", onclick: () => { M.catVis[ci] = 0; uploadCatVis(); renderCats(); select(-1); } }, "Ẩn category");
  box.replaceChildren(el("b", {}, cat), el("div", { class: "dim" }, `Id ${id}${link ? " · trong link" : ""}`),
    el("div", { style: "display:flex;gap:8px;margin-top:10px" }, go, hide), msg);
  box.classList.remove("hidden");
}
function renderCats() {
  const man = M.man, box = $("#cats");
  if (!man) return;
  const counts = new Array(man.categories.length).fill(0);
  for (const e of man.elements) counts[e[1]]++;
  const order = man.categories.map((c, i) => i).sort((a, b) => man.categories[a].localeCompare(man.categories[b]));
  const all = el("input", { type: "checkbox" });
  all.checked = M.catVis.every(v => v);
  all.addEventListener("change", () => { M.catVis.fill(all.checked ? 255 : 0); uploadCatVis(); renderCats(); });
  box.replaceChildren(el("label", {}, all, el("span", {}, el("b", {}, "Tất cả")), el("span", {}, man.elements.length)),
    ...order.map(i => {
      const cb = el("input", { type: "checkbox" });
      cb.checked = !!M.catVis[i];
      cb.addEventListener("change", () => { M.catVis[i] = cb.checked ? 255 : 0; uploadCatVis(); all.checked = M.catVis.every(v => v); });
      return el("label", {}, cb, el("span", {}, man.categories[i]), el("span", {}, counts[i]));
    }));
}
$("#cats-btn").addEventListener("click", () => $("#cats").classList.toggle("hidden"));
$("#fit").addEventListener("click", () => fit());

async function load(name) {
  veil("Đang tải hình…");
  try {
    const man = await api(`/mesh/${encodeURIComponent(name)}.json`);
    const r = await fetch(`/mesh/${encodeURIComponent(name)}.bin`, { credentials: "same-origin" });
    if (!r.ok) throw new Error("không tải được " + name + ".bin");
    upload(man, await r.arrayBuffer());
    $("#title").textContent = `${man.view} · ${man.model}`;
    const f = n => n.toLocaleString("vi-VN");
    $("#sub").textContent = `${f(man.triangles)} tam giác · ${f(man.elements.length)} phần tử · lấy lúc ${man.exported}` +
      (man.truncated ? ` · đã cắt ở ${f(man.limit)} tam giác` : "");
    cam.radius = 1;
    fit();
    renderCats();
    select(-1);
    history.replaceState(null, "", `?mesh=${encodeURIComponent(name)}${PID ? "&pid=" + PID : ""}`);
    $("#meshes").value = name;
    veil("");
  } catch (e) { veil(e.message); }
}
async function listMeshes(selectName) {
  const { meshes } = await api("/api/meshes");
  $("#meshes").replaceChildren(...meshes.map(m => el("option", { value: m.name }, `${m.view} · ${m.exported}`)));
  $("#meshes").classList.toggle("hidden", !meshes.length);
  if (selectName) $("#meshes").value = selectName;
  return meshes;
}
$("#meshes").addEventListener("change", e => load(e.target.value));
$("#grab").addEventListener("click", async () => {
  const b = $("#grab");
  b.disabled = true;
  veil("Revit đang xuất hình của view 3D đang mở…");
  try {
    const r = await run("view_mesh_export", {}, "Lấy hình 3D cho viewer GPU");
    await listMeshes(r.name);
    await load(r.name);
  } catch (e) { veil(""); $("#sub").textContent = e.message; }
  b.disabled = false;
});

(async () => {
  try {
    const want = new URLSearchParams(location.search).get("mesh");
    const meshes = await listMeshes(want);
    const name = want || (meshes[0] && meshes[0].name);
    if (name) await load(name);
    else { veil(""); $("#sub").textContent = "Chưa có hình: mở một view 3D trong Revit rồi bấm Lấy từ Revit"; }
  } catch (e) { veil(e.message); }
})();
