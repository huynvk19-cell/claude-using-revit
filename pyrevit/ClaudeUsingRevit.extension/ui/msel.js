// Drop-down lists, macOS style: every <select> on the page gets a popup button (value + double chevron)
// and a glass menu (check mark on the chosen item, group headings, arrow keys, Enter, Esc, type to
// search when the list is long). The <select> stays in the page, hidden, and keeps the value: code
// reads / sets it and listens to its change event as before. Opt out: data-native on the select.
"use strict";
(() => {
  const buttons = new WeakMap();                 // select -> its popup button
  let menu = null;

  // a value set from code (select.value = ..., selectedIndex = ...) also updates the button
  for (const prop of ["value", "selectedIndex"]) {
    const d = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, prop);
    Object.defineProperty(HTMLSelectElement.prototype, prop, {
      configurable: true, enumerable: d.enumerable, get() { return d.get.call(this); },
      set(v) { d.set.call(this, v); const b = buttons.get(this); if (b) b.sync(); },
    });
  }

  function enhance(sel) {
    if (sel.multiple || "native" in sel.dataset || sel.classList.contains("g-edit")) return;
    let btn = buttons.get(sel);
    if (btn) { if (sel.nextSibling !== btn) sel.after(btn); return; }
    btn = document.createElement("button");
    btn.type = "button";
    btn.className = "msel " + sel.className;
    if (sel.title) btn.title = sel.title;
    btn.innerHTML = '<span class="v"></span><span class="chev"><svg viewBox="0 0 10 14"><path d="M2.5 5.5L5 3l2.5 2.5M2.5 8.5L5 11l2.5-2.5"/></svg></span>';
    btn.sync = () => {
      const o = sel.options[sel.selectedIndex];
      btn.querySelector(".v").textContent = o ? o.textContent : "";
      btn.disabled = sel.disabled;
    };
    sel.classList.add("msel-src");
    sel.after(btn);
    buttons.set(sel, btn);
    btn.sync();
    new MutationObserver(btn.sync).observe(sel, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ["disabled"] });
    sel.addEventListener("change", btn.sync);
    btn.addEventListener("click", () => menu && menu.sel === sel ? close() : open(sel, btn));
    btn.addEventListener("keydown", e => { if (["ArrowDown", "ArrowUp", "Enter", " "].includes(e.key)) { e.preventDefault(); open(sel, btn); } });
  }

  function close() {
    if (!menu) return;
    menu.el.remove();
    menu.btn.classList.remove("open");
    document.removeEventListener("mousedown", outside, true);
    window.removeEventListener("resize", close);
    document.removeEventListener("scroll", onScroll, true);
    menu = null;
  }
  const outside = e => { if (menu && !menu.el.contains(e.target) && !menu.btn.contains(e.target)) close(); };
  const onScroll = e => { if (menu && !menu.el.contains(e.target)) close(); };

  function open(sel, btn) {
    close();
    const el = document.createElement("div");
    el.className = "msel-menu";
    const list = document.createElement("div");
    list.className = "ml";
    const items = [];
    const add = (o, parent) => {
      const it = document.createElement("div");
      it.className = "mi" + (o.selected ? " on" : "") + (o.disabled ? " dis" : "");
      it.innerHTML = '<svg class="ck" viewBox="0 0 12 12"><path d="M2.5 6.3l2.3 2.3 4.7-5"/></svg><span></span>';
      it.lastChild.textContent = o.textContent;
      it.addEventListener("mousemove", () => hover(items.findIndex(x => x.it === it)));
      it.addEventListener("click", () => { if (!o.disabled) choose(o); });
      parent.append(it);
      items.push({ it, o, text: (o.textContent + " " + (o.parentNode.label || "")).toLowerCase() });
    };
    let first = true;
    for (const ch of sel.children) {
      if (ch.tagName === "OPTGROUP") {
        const g = document.createElement("div");
        g.className = "mg";
        g.innerHTML = (first ? "" : '<div class="sep"></div>') + '<div class="mh"></div>';
        g.querySelector(".mh").textContent = ch.label;
        for (const o of ch.children) add(o, g);
        list.append(g);
      } else add(ch, list);
      first = false;
    }
    let q = null;
    if (items.length > 12) {
      q = document.createElement("input");
      q.className = "mq";
      q.placeholder = "Tìm";
      q.addEventListener("input", () => {
        const t = q.value.trim().toLowerCase();
        for (const x of items) x.it.classList.toggle("hide", !!t && !x.text.includes(t));
        for (const g of list.querySelectorAll(".mg")) g.classList.toggle("hide", !g.querySelector(".mi:not(.hide)"));
        hover(items.findIndex(x => !x.it.classList.contains("hide") && !x.o.disabled));
      });
      el.append(q);
    }
    el.append(list);
    document.body.append(el);

    // under the button (above when there is more room there), at least as wide as the button
    const r = btn.getBoundingClientRect(), pad = 8;
    el.style.minWidth = Math.max(r.width, 180) + "px";
    el.style.left = Math.max(pad, Math.min(r.left, innerWidth - el.offsetWidth - pad)) + "px";
    const below = innerHeight - r.bottom - pad, above = r.top - pad;
    const down = below >= Math.min(el.scrollHeight, 320) || below >= above;
    el.style.maxHeight = Math.max(120, (down ? below : above) - 6) + "px";
    el.style.top = (down ? r.bottom + 5 : Math.max(pad, r.top - 5 - el.offsetHeight)) + "px";
    el.classList.add(down ? "down" : "up");

    menu = { el, btn, sel, items, idx: -1 };
    btn.classList.add("open");
    const on = items.findIndex(x => x.o.selected);
    hover(on >= 0 ? on : 0, true);
    document.addEventListener("mousedown", outside, true);
    window.addEventListener("resize", close);
    document.addEventListener("scroll", onScroll, true);
    el.addEventListener("keydown", key);
    el.tabIndex = -1;
    (q || el).focus({ preventScroll: true });
  }

  function hover(i, center) {
    if (!menu) return;
    menu.items.forEach((x, k) => x.it.classList.toggle("hi", k === i));
    menu.idx = i;
    const x = menu.items[i];
    if (!x) return;
    // scroll the list only (scrollIntoView would also scroll the page, and a page scroll closes the menu)
    const list = menu.el.querySelector(".ml"), top = x.it.offsetTop - list.offsetTop, h = x.it.offsetHeight;
    if (center) list.scrollTop = top - (list.clientHeight - h) / 2;
    else if (top < list.scrollTop) list.scrollTop = top;
    else if (top + h > list.scrollTop + list.clientHeight) list.scrollTop = top + h - list.clientHeight;
  }

  let typed = "", typedAt = 0;
  function key(e) {
    const vis = menu.items.map((x, k) => [x, k]).filter(([x]) => !x.it.classList.contains("hide") && !x.o.disabled).map(([, k]) => k);
    const pos = vis.indexOf(menu.idx);
    if (e.key === "ArrowDown") { e.preventDefault(); hover(vis[Math.min(vis.length - 1, pos + 1)] ?? vis[0]); }
    else if (e.key === "ArrowUp") { e.preventDefault(); hover(vis[Math.max(0, pos - 1)] ?? vis[0]); }
    else if (e.key === "Home") { e.preventDefault(); hover(vis[0]); }
    else if (e.key === "End") { e.preventDefault(); hover(vis[vis.length - 1]); }
    else if (e.key === "Enter") { e.preventDefault(); const x = menu.items[menu.idx]; if (x) choose(x.o); }
    else if (e.key === "Escape" || e.key === "Tab") { e.preventDefault(); e.stopPropagation(); const b = menu.btn; close(); b.focus(); }
    else if (e.key.length === 1 && e.target.tagName !== "INPUT") {       // type-ahead on short lists
      typed = Date.now() - typedAt > 700 ? e.key.toLowerCase() : typed + e.key.toLowerCase();
      typedAt = Date.now();
      const k = vis.find(k => menu.items[k].it.textContent.toLowerCase().startsWith(typed));
      if (k !== undefined) hover(k);
    }
  }

  function choose(o) {
    const { sel, btn } = menu;
    close();
    btn.focus();
    if (sel.value === o.value && o.selected) return;
    sel.value = o.value;
    sel.dispatchEvent(new Event("input", { bubbles: true }));
    sel.dispatchEvent(new Event("change", { bubbles: true }));
  }

  const scan = root => { if (root.tagName === "SELECT") enhance(root); else if (root.querySelectorAll) root.querySelectorAll("select").forEach(enhance); };
  scan(document.body);
  new MutationObserver(list => { for (const m of list) for (const n of m.addedNodes) if (n.nodeType === 1) scan(n); })
    .observe(document.body, { childList: true, subtree: true });
})();
