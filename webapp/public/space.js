// Background: inside the cabin of an exploration ship, looking out through a wide observation window
// (three panes, two struts) at the Milky Way: star field, the bright band with its dust lanes and warm
// core. Around the window: dark hull panels with seams, a ceiling light strip, the console edge below,
// a bluish rim light on the window bevel and a faint reflection on the glass. Drawn once per size.
"use strict";
(function () {
  const cv = document.getElementById("space");
  const ctx = cv.getContext("2d");
  let seed = 11;
  const rnd = () => ((seed = (seed * 16807) % 2147483647) / 2147483647);
  const gauss = () => { let u = 0, v = 0; while (!u) u = rnd(); while (!v) v = rnd(); return Math.sqrt(-2 * Math.log(u)) * Math.cos(2 * Math.PI * v); };

  function glow(x, y, r, stops) {
    const g = ctx.createRadialGradient(x, y, 0, x, y, r);
    for (const [o, c] of stops) g.addColorStop(o, c);
    ctx.fillStyle = g;
    ctx.beginPath(); ctx.arc(x, y, r, 0, Math.PI * 2); ctx.fill();
  }

  function roundRect(x, y, w, h, r) {
    ctx.moveTo(x + r, y);
    ctx.arcTo(x + w, y, x + w, y + h, r); ctx.arcTo(x + w, y + h, x, y + h, r);
    ctx.arcTo(x, y + h, x, y, r); ctx.arcTo(x, y, x + w, y, r); ctx.closePath();
  }

  // ---- outside: the Milky Way -----------------------------------------------------------
  function sky(w, h) {
    const m = Math.max(w, h);
    ctx.fillStyle = "#010208";
    ctx.fillRect(0, 0, w, h);
    ctx.globalCompositeOperation = "lighter";
    for (let i = 0; i < Math.round(w * h / 900); i++) {                 // background stars
      const x = rnd() * w, y = rnd() * h, r = Math.pow(rnd(), 7) * 1.7 + .25, t = rnd();
      const col = t < .72 ? "255,255,255" : t < .88 ? "170,200,255" : "255,214,170";
      ctx.fillStyle = `rgba(${col},${.25 + rnd() * .7})`;
      ctx.beginPath(); ctx.arc(x, y, r, 0, Math.PI * 2); ctx.fill();
    }
    // the band: rises from lower left to upper right, core a little right of centre
    const cx = w * .56, cy = h * .46, ang = -0.5, len = m * 1.3, wid = m * .2;
    ctx.save();
    ctx.translate(cx, cy); ctx.rotate(ang);
    const band = ctx.createLinearGradient(0, -wid, 0, wid);
    band.addColorStop(0, "rgba(0,0,0,0)"); band.addColorStop(.35, "rgba(70,80,130,.10)");
    band.addColorStop(.5, "rgba(150,150,190,.18)"); band.addColorStop(.65, "rgba(70,80,130,.10)"); band.addColorStop(1, "rgba(0,0,0,0)");
    ctx.fillStyle = band; ctx.fillRect(-len, -wid, len * 2, wid * 2);
    for (let i = 0; i < 30000; i++) {                                   // dense band stars
      const x = (rnd() - .5) * len * 2, spread = wid * (.28 + .5 * Math.exp(-(x * x) / (len * len * .08)));
      const y = gauss() * spread * .45;
      const warm = Math.abs(x) < len * .25 && rnd() < .45;
      ctx.fillStyle = warm ? `rgba(255,220,180,${.1 + rnd() * .45})` : `rgba(215,222,255,${.08 + rnd() * .38})`;
      ctx.fillRect(x, y, rnd() < .1 ? 1.4 : .9, rnd() < .1 ? 1.4 : .9);
    }
    ctx.globalCompositeOperation = "source-over";
    glow(0, 0, wid * 1.5, [[0, "rgba(255,226,190,.32)"], [.35, "rgba(230,170,130,.14)"], [1, "rgba(0,0,0,0)"]]);  // core
    ctx.globalCompositeOperation = "lighter";
    glow(0, 0, wid * .55, [[0, "rgba(255,240,215,.35)"], [1, "rgba(0,0,0,0)"]]);
    ctx.globalCompositeOperation = "source-over";
    for (let i = 0; i < 160; i++) {                                     // dust lanes along the middle
      const x = (rnd() - .5) * len * 1.3, y = gauss() * wid * .12, rx = wid * (.06 + rnd() * .3), ry = wid * (.015 + rnd() * .05);
      ctx.save(); ctx.translate(x, y); ctx.rotate((rnd() - .5) * .3); ctx.scale(1, ry / rx);
      const g = ctx.createRadialGradient(0, 0, 0, 0, 0, rx);
      g.addColorStop(0, "rgba(2,3,8,.38)"); g.addColorStop(1, "rgba(2,3,8,0)");
      ctx.fillStyle = g; ctx.beginPath(); ctx.arc(0, 0, rx, 0, Math.PI * 2); ctx.fill(); ctx.restore();
    }
    ctx.globalCompositeOperation = "lighter";
    for (let i = 0; i < 8; i++)                                         // a few pink / blue nebulae in the band
      glow((rnd() - .5) * len * .9, gauss() * wid * .25, wid * (.12 + rnd() * .2), [[0, rnd() < .5 ? "rgba(200,90,130,.10)" : "rgba(80,130,220,.10)"], [1, "rgba(0,0,0,0)"]]);
    ctx.restore();
    ctx.globalCompositeOperation = "source-over";
  }

  // ---- inside: the cabin around the window --------------------------------------------------
  function cabin(w, h) {
    const narrow = w < 760;
    const wx = w * (narrow ? .03 : .07), wy = h * (narrow ? .09 : .1);
    const ww = w - 2 * wx, wh = h * (narrow ? .7 : .66), r = Math.min(w, h) * .06;
    const strut = Math.max(10, w * .009);
    const panes = narrow ? 1 : 3;

    // hull: everything but the window panes
    ctx.save();
    ctx.beginPath();
    ctx.rect(0, 0, w, h);
    const pw = (ww - strut * (panes - 1)) / panes;
    for (let i = 0; i < panes; i++) {
      const x = wx + i * (pw + strut);
      const rl = i === 0 ? r : r * .25, rr = i === panes - 1 ? r : r * .25;
      ctx.moveTo(x + rl, wy);
      ctx.arcTo(x + pw, wy, x + pw, wy + wh, rr); ctx.arcTo(x + pw, wy + wh, x, wy + wh, rr);
      ctx.arcTo(x, wy + wh, x, wy, rl); ctx.arcTo(x, wy, x + pw, wy, rl); ctx.closePath();
    }
    const hull = ctx.createLinearGradient(0, 0, 0, h);
    hull.addColorStop(0, "#0d1017"); hull.addColorStop(.12, "#141922"); hull.addColorStop(.75, "#10141b"); hull.addColorStop(1, "#07090d");
    ctx.fillStyle = hull;
    ctx.fill("evenodd");
    ctx.clip("evenodd");

    // panel seams and rivet rows on the hull
    ctx.strokeStyle = "rgba(255,255,255,.035)"; ctx.lineWidth = 1;
    for (let x = w * .02; x < w; x += w * .16) { ctx.beginPath(); ctx.moveTo(x, 0); ctx.lineTo(x, h); ctx.stroke(); }
    for (const y of [wy * .45, wy + wh + (h - wy - wh) * .35, wy + wh + (h - wy - wh) * .7]) { ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(w, y); ctx.stroke(); }
    ctx.fillStyle = "rgba(255,255,255,.05)";
    for (let x = w * .03; x < w; x += 26) { ctx.fillRect(x, wy * .45 - 4, 2, 2); }

    // ceiling light strip
    const ly = wy * .32;
    const strip = ctx.createLinearGradient(w * .2, 0, w * .8, 0);
    strip.addColorStop(0, "rgba(220,235,255,0)"); strip.addColorStop(.15, "rgba(220,235,255,.55)"); strip.addColorStop(.85, "rgba(220,235,255,.55)"); strip.addColorStop(1, "rgba(220,235,255,0)");
    ctx.fillStyle = strip; ctx.fillRect(w * .2, ly - 1.5, w * .6, 3);
    ctx.save();                                                         // its light on the ceiling: a soft ellipse
    ctx.translate(w / 2, ly); ctx.scale(1, Math.max(.05, (wy - ly) / (w * .35)));
    const spill = ctx.createRadialGradient(0, 0, 0, 0, 0, w * .35);
    spill.addColorStop(0, "rgba(200,220,255,.12)"); spill.addColorStop(1, "rgba(200,220,255,0)");
    ctx.fillStyle = spill; ctx.beginPath(); ctx.arc(0, 0, w * .35, 0, Math.PI * 2); ctx.fill();
    ctx.restore();

    // console edge below the window: a curved desk with a soft top light and a few dim indicators
    const cy0 = wy + wh + (h - wy - wh) * .42;
    ctx.beginPath();
    ctx.moveTo(0, h); ctx.lineTo(0, cy0 + 30); ctx.quadraticCurveTo(w / 2, cy0 - 26, w, cy0 + 30); ctx.lineTo(w, h); ctx.closePath();
    const desk = ctx.createLinearGradient(0, cy0 - 30, 0, h);
    desk.addColorStop(0, "#1a2029"); desk.addColorStop(.08, "#0f131a"); desk.addColorStop(1, "#05070a");
    ctx.fillStyle = desk; ctx.fill();
    ctx.beginPath(); ctx.moveTo(0, cy0 + 30); ctx.quadraticCurveTo(w / 2, cy0 - 26, w, cy0 + 30);
    ctx.strokeStyle = "rgba(190,215,255,.22)"; ctx.lineWidth = 1.5; ctx.stroke();
    for (let i = 0; i < 9; i++) {
      const x = w * (.18 + i * .08), y = cy0 + 14 + Math.pow((x - w / 2) / (w / 2), 2) * 50;
      glow(x, y, 7, [[0, "rgba(170,200,255,.45)"], [1, "rgba(0,0,0,0)"]]);
    }
    ctx.restore();

    // window bevel: inner rim light per pane + struts' edges
    for (let i = 0; i < panes; i++) {
      const x = wx + i * (pw + strut);
      const rl = i === 0 ? r : r * .25, rr = i === panes - 1 ? r : r * .25;
      ctx.beginPath();
      ctx.moveTo(x + rl, wy);
      ctx.arcTo(x + pw, wy, x + pw, wy + wh, rr); ctx.arcTo(x + pw, wy + wh, x, wy + wh, rr);
      ctx.arcTo(x, wy + wh, x, wy, rl); ctx.arcTo(x, wy, x + pw, wy, rl); ctx.closePath();
      ctx.strokeStyle = "rgba(150,190,255,.28)"; ctx.lineWidth = 2; ctx.stroke();
      ctx.strokeStyle = "rgba(0,0,0,.6)"; ctx.lineWidth = 6; ctx.stroke();
      ctx.save(); ctx.clip();
      const refl = ctx.createLinearGradient(x, wy, x + pw * .7, wy + wh);        // faint reflection on the glass
      refl.addColorStop(0, "rgba(255,255,255,.05)"); refl.addColorStop(.3, "rgba(255,255,255,0)");
      refl.addColorStop(.55, "rgba(255,255,255,.025)"); refl.addColorStop(.62, "rgba(255,255,255,0)");
      ctx.fillStyle = refl; ctx.fillRect(x, wy, pw, wh);
      const inner = ctx.createLinearGradient(0, wy, 0, wy + wh);                 // cabin light falling on the glass edge
      inner.addColorStop(0, "rgba(150,180,230,.10)"); inner.addColorStop(.12, "rgba(0,0,0,0)");
      ctx.fillStyle = inner; ctx.fillRect(x, wy, pw, wh);
      ctx.restore();
    }
    // a soft vignette: the cabin is darker towards the corners
    const v = ctx.createRadialGradient(w / 2, h * .45, Math.min(w, h) * .3, w / 2, h * .45, Math.max(w, h) * .8);
    v.addColorStop(0, "rgba(0,0,0,0)"); v.addColorStop(1, "rgba(0,0,0,.55)");
    ctx.fillStyle = v; ctx.fillRect(0, 0, w, h);
  }

  function draw() {
    seed = 11;
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const w = window.innerWidth, h = window.innerHeight;
    if (!w || !h) return;                // not laid out yet (hidden window): the resize redraws
    cv.width = w * dpr; cv.height = h * dpr;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    sky(w, h);
    cabin(w, h);
  }

  // a photo of the user's own (server /bg) replaces the drawn cabin when there is one
  const photo = new Image();
  photo.onload = () => { document.body.classList.add("photo"); document.body.style.setProperty("--photo", "url(/bg)"); cv.remove(); };
  photo.onerror = () => {
    let t = null;
    window.addEventListener("resize", () => { clearTimeout(t); t = setTimeout(draw, 150); });
    draw();
  };
  photo.src = "/bg";
})();
