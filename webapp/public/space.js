// Background: deep space, a starfield, faint nebulae, the band of the galaxy and a ringed
// galaxy (bright core inside a tilted ring of stars). Drawn once per size, no animation.
"use strict";
(function () {
  const cv = document.getElementById("space");
  const ctx = cv.getContext("2d");
  let seed = 7;
  const rnd = () => ((seed = (seed * 16807) % 2147483647) / 2147483647);
  const gauss = () => { let u = 0, v = 0; while (!u) u = rnd(); while (!v) v = rnd(); return Math.sqrt(-2 * Math.log(u)) * Math.cos(2 * Math.PI * v); };

  function glow(x, y, r, stops) {
    const g = ctx.createRadialGradient(x, y, 0, x, y, r);
    for (const [o, c] of stops) g.addColorStop(o, c);
    ctx.fillStyle = g;
    ctx.beginPath(); ctx.arc(x, y, r, 0, Math.PI * 2); ctx.fill();
  }

  function draw() {
    seed = 7;
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const w = window.innerWidth, h = window.innerHeight;
    cv.width = w * dpr; cv.height = h * dpr;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.fillStyle = "#02030a";
    ctx.fillRect(0, 0, w, h);
    const m = Math.max(w, h);

    // nebulae
    ctx.globalCompositeOperation = "lighter";
    glow(w * .18, h * .82, m * .45, [[0, "rgba(70,40,140,.20)"], [1, "rgba(0,0,0,0)"]]);
    glow(w * .95, h * .1, m * .35, [[0, "rgba(20,70,140,.18)"], [1, "rgba(0,0,0,0)"]]);
    glow(w * .5, h * 1.05, m * .4, [[0, "rgba(140,50,90,.12)"], [1, "rgba(0,0,0,0)"]]);

    // the band of the Milky Way: dense faint stars along a tilted strip
    ctx.save();
    ctx.translate(w * .5, h * .55); ctx.rotate(-0.42);
    const band = ctx.createLinearGradient(0, -m * .18, 0, m * .18);
    band.addColorStop(0, "rgba(0,0,0,0)"); band.addColorStop(.5, "rgba(120,130,180,.10)"); band.addColorStop(1, "rgba(0,0,0,0)");
    ctx.fillStyle = band; ctx.fillRect(-m, -m * .18, m * 2, m * .36);
    for (let i = 0; i < 2600; i++) {
      const x = (rnd() - .5) * m * 2, y = gauss() * m * .06;
      ctx.fillStyle = `rgba(210,215,255,${.08 + rnd() * .3})`;
      ctx.fillRect(x, y, .8, .8);
    }
    ctx.restore();

    // stars
    for (let i = 0; i < Math.round(w * h / 1400); i++) {
      const x = rnd() * w, y = rnd() * h, r = Math.pow(rnd(), 6) * 1.8 + .3, t = rnd();
      const col = t < .7 ? "255,255,255" : t < .85 ? "170,200,255" : "255,215,170";
      ctx.fillStyle = `rgba(${col},${.35 + rnd() * .65})`;
      ctx.beginPath(); ctx.arc(x, y, r, 0, Math.PI * 2); ctx.fill();
      if (r > 1.4) glow(x, y, r * 5, [[0, `rgba(${col},.25)`], [1, "rgba(0,0,0,0)"]]);
    }

    // ringed galaxy
    const gx = w * (w > 900 ? .74 : .62), gy = h * .3, R = Math.min(m * .2, 300), tilt = -0.32, flat = .3;
    const ring = (front) => {
      ctx.save();
      ctx.translate(gx, gy); ctx.rotate(tilt);
      for (let i = 0; i < 5200; i++) {
        const a = rnd() * Math.PI * 2, rr = R * (1 + gauss() * .07), s = Math.sin(a);
        if ((s > 0) !== front) continue;
        const x = Math.cos(a) * rr, y = s * rr * flat;
        const b = rnd();
        ctx.fillStyle = b < .75 ? `rgba(160,195,255,${.2 + rnd() * .5})` : `rgba(255,190,150,${.2 + rnd() * .5})`;
        ctx.fillRect(x, y, 1.1, 1.1);
      }
      ctx.restore();
    };
    ctx.save();
    ctx.translate(gx, gy); ctx.rotate(tilt); ctx.scale(1, flat);
    glow(0, 0, R * 1.18, [[0, "rgba(0,0,0,0)"], [.78, "rgba(0,0,0,0)"], [.86, "rgba(90,140,255,.10)"], [.94, "rgba(0,0,0,0)"]]);
    ctx.restore();
    ring(false);                                    // far side of the ring, behind the core
    glow(gx, gy, R * .55, [[0, "rgba(255,236,210,.95)"], [.08, "rgba(255,200,150,.6)"], [.3, "rgba(200,120,90,.18)"], [1, "rgba(0,0,0,0)"]]);
    glow(gx, gy, R * .1, [[0, "rgba(255,255,255,1)"], [1, "rgba(255,240,220,0)"]]);
    ring(true);                                     // near side, in front of the core
    ctx.globalCompositeOperation = "source-over";
  }

  let t = null;
  window.addEventListener("resize", () => { clearTimeout(t); t = setTimeout(draw, 150); });
  draw();
})();
