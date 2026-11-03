// The desktop app's title bar, macOS style. Only inside the app (desktop/app.ps1: WebView2, no Windows
// title bar): traffic lights top left, the top strip drags the window (double click = zoom), the top edge
// resizes. The window answers with its state, so the lights grey out when it is not the active window.
// Messages to the window: close, min, zoom, drag, size:<hit test> (12 top, 13 top left, 14 top right).
"use strict";
(() => {
  const wv = window.chrome && window.chrome.webview;
  if (!wv) return;
  const post = m => wv.postMessage(m);
  const body = document.body;
  body.classList.add("in-app");
  const glyph = { close: "M4 4l4 4M8 4l-4 4", min: "M3.5 6h5", zoom: "M4 3.5h4.5V8M8 8.5H3.5V4" };
  const bar = document.createElement("div");
  bar.id = "titlebar";
  bar.innerHTML = `<div class="lights">${["close", "min", "zoom"].map(k =>
    `<button class="light ${k}" data-m="${k}" title="${{ close: "Đóng", min: "Thu nhỏ", zoom: "Phóng to / thu lại" }[k]}" tabindex="-1">` +
    `<svg viewBox="0 0 12 12"><path d="${glyph[k]}"/></svg></button>`).join("")}</div>`;
  body.prepend(bar);
  for (const b of bar.querySelectorAll("button")) b.addEventListener("click", e => { e.stopPropagation(); post(b.dataset.m); });

  // the strip that drags: the top of the window wherever nothing clickable is under the mouse
  const STRIP = 64;
  const bare = t => t === document.documentElement || t === body || t.id === "space" || t.id === "titlebar" || t.classList.contains("lights")
    || t.matches("main, .ornament, .tiles, .table-view, .all, #view-tools, .g-bar, .g-bar .row");
  const edge = e => !body.classList.contains("max") && e.clientY <= 5;
  document.addEventListener("mousedown", e => {
    if (e.button !== 0) return;
    if (edge(e)) { e.preventDefault(); post("size:" + (e.clientX <= 10 ? 13 : e.clientX >= innerWidth - 10 ? 14 : 12)); return; }
    if (e.clientY <= STRIP && bare(e.target) && e.detail === 1) { e.preventDefault(); post("drag"); }
  }, true);
  document.addEventListener("dblclick", e => { if (e.clientY <= STRIP && bare(e.target)) post("zoom"); }, true);
  document.addEventListener("mousemove", e => body.classList.toggle("edge-n", edge(e)), { passive: true });
  wv.addEventListener("message", e => {
    const m = String(e.data);
    if (!m.startsWith("window:")) return;
    const [state, act] = m.slice(7).split(",");
    body.classList.toggle("max", state === "max");
    body.classList.toggle("inactive", act === "inactive");
  });
  post("hello");
})();
