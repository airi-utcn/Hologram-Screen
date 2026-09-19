// UC7 viewer: renders whatever the server has streamed so far, and reports
// the camera back so the server's policy can react to the viewpoint.
(() => {
  "use strict";

  const LOD_TINT = [0x5fd38d, 0xffd166, 0xff9f43, 0xff6b6b];

  // The server names policies technically; show plain language instead.
  const FRIENDLY = {
    full:          ["Send everything at full quality",
                    "Ignores where you are looking and never gives up detail. " +
                    "The obvious approach - and it fails badly."],
    lod:           ["Simplify things that are far away",
                    "Distant objects are sent with less detail, but everything " +
                    "in the room is still sent."],
    roi:           ["Only send what you are looking at",
                    "Skips anything outside your view, but still sends it at " +
                    "full quality."],
    adaptive_geom: ["Smart: fit the connection",
                    "Works out how much data fits, then gives detail to whoever " +
                    "fills most of your screen."],
    lesson_aware:  ["Smart + follows the lesson plan",
                    "Knows who is speaking and what is being shown, protects " +
                    "the view of it, and prepares what the lesson shows next."],
    adaptive:      ["Smart + knows who matters",
                    "Same, but whoever matters most in this lesson gets " +
                    "priority over furniture and distant people."],
  };
  const label = n => (FRIENDLY[n] ? FRIENDLY[n][0] : n);
  const blurb = (n, fallback) => (FRIENDLY[n] ? FRIENDLY[n][1] : fallback || "");

  const speedHint = mbps =>
    mbps <= 5 ? "(weak mobile connection)"
    : mbps <= 15 ? "(typical home Wi-Fi)"
    : mbps <= 40 ? "(good broadband)"
    : mbps < 100 ? "(fast fibre)"
    : "(5G: everything arrives)";

  // ---------------------------------------------------------------- three.js
  const host = document.getElementById("view");
  const renderer = new THREE.WebGLRenderer({ antialias: true });
  renderer.setPixelRatio(Math.min(2, window.devicePixelRatio));
  host.appendChild(renderer.domElement);

  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0x0e1116);
  scene.add(new THREE.HemisphereLight(0xdfe8ff, 0x20242c, 0.9));
  const sun = new THREE.DirectionalLight(0xffffff, 0.7);
  sun.position.set(4, 8, 6);
  scene.add(sun);
  scene.add(new THREE.GridHelper(40, 40, 0x1c2230, 0x161b25));

  const camera = new THREE.PerspectiveCamera(60, 1, 0.05, 260);
  let yaw = 0, pitch = -0.02;
  function home(eye = [0, 1.65, 7], look = [0, 1.4, -5]) {
    camera.position.set(eye[0], eye[1], eye[2]);
    const dx = look[0] - eye[0], dy = look[1] - eye[1], dz = look[2] - eye[2];
    yaw = Math.atan2(-dx, -dz);
    pitch = Math.atan2(dy, Math.hypot(dx, dz));
  }
  home();

  function resize() {
    renderer.setSize(window.innerWidth, window.innerHeight);
    camera.aspect = window.innerWidth / window.innerHeight;
    camera.updateProjectionMatrix();
  }
  window.addEventListener("resize", resize);
  resize();

  // ---------------------------------------------------------------- controls
  const keys = new Set();
  let autopilot = false;          // true while the lesson drives the camera
  function takeControl() {
    if (!autopilot) return;
    autopilot = false;
    if (ws && ws.readyState === 1) ws.send(JSON.stringify({ type: "autopilot", on: false }));
    document.getElementById("follow").hidden = false;
  }
  window.addEventListener("keydown", e => {
    if (e.target.tagName === "SELECT" || e.target.tagName === "INPUT") return;
    if ("wasdqe".includes(e.key.toLowerCase())) takeControl();
    keys.add(e.key.toLowerCase());
    if (e.key.toLowerCase() === "r") sendConfig(true);
    if (e.key.toLowerCase() === "h") document.body.classList.toggle("clean");
  });
  window.addEventListener("keyup", e => keys.delete(e.key.toLowerCase()));
  let dragging = false, lx = 0, ly = 0;
  renderer.domElement.addEventListener("pointerdown", e => {
    dragging = true; lx = e.clientX; ly = e.clientY; takeControl();
  });
  window.addEventListener("pointerup", () => dragging = false);
  window.addEventListener("pointermove", e => {
    if (!dragging) return;
    yaw -= (e.clientX - lx) * 0.004;
    pitch = Math.max(-1.4, Math.min(1.4, pitch - (e.clientY - ly) * 0.004));
    lx = e.clientX; ly = e.clientY;
  });

  function forward() {
    return new THREE.Vector3(-Math.sin(yaw) * Math.cos(pitch), Math.sin(pitch),
                             -Math.cos(yaw) * Math.cos(pitch));
  }

  // ---------------------------------------------------------------- objects
  // Static objects can have several looks during a lesson (an empty flask, a
  // pink one...).  Every look received is kept, keyed "oid:state", and the one
  // the lesson says is current is shown -- so a look fetched ahead of time can
  // appear the instant the plan calls for it.
  const meta = new Map();       // oid -> manifest entry
  const variants = new Map();   // "oid:state" -> { obj3d, level }
  const shown = new Map();      // oid -> the variant currently on screen
  const current = new Map();    // oid -> { state, t0 } the lesson says it is in
  const people = new Map();     // oid -> { obj3d, level } live participants
  let received = 0, receivedBytes = 0;
  const tinted = () => document.getElementById("lodTint").checked;

  function dispose(o) {
    o.geometry.dispose(); o.material.dispose();
    for (const c of o.children) { if (c.geometry) c.geometry.dispose(); if (c.material) c.material.dispose(); }
  }

  function clearScene() {
    for (const v of variants.values()) { scene.remove(v.obj3d); dispose(v.obj3d); }
    for (const v of people.values()) { scene.remove(v.obj3d); dispose(v.obj3d); }
    variants.clear(); shown.clear(); current.clear(); people.clear();
    received = 0; receivedBytes = 0;
  }

  function tintFor(oid, level) {
    if (tinted()) return new THREE.Color(LOD_TINT[Math.min(level, 3)]);
    const m = meta.get(oid);
    return new THREE.Color(...(m ? m.colour : [0.8, 0.8, 0.8]));
  }

  function paintMaterial(obj3d, level) {
    // vertex colours when the object carries them, one flat colour otherwise;
    // the LOD view overrides both
    const u = obj3d.userData, mat = obj3d.material;
    mat.vertexColors = u.hasRgb && !tinted();
    mat.color = tinted() ? new THREE.Color(LOD_TINT[Math.min(level, 3)])
      : u.hasRgb ? new THREE.Color(0xffffff) : u.baseColour.clone();
    mat.needsUpdate = true;
  }

  function show(oid) {
    const v = variants.get(`${oid}:${stateOf(oid)}`);
    if (!v) return;                      // not here yet: keep showing the old look
    const s = shown.get(oid);
    if (s === v) return;
    if (s) scene.remove(s.obj3d);
    scene.add(v.obj3d);
    shown.set(oid, v);
  }

  const stateOf = oid => (current.get(oid) || { state: "default" }).state;

  function setStates(states) {
    for (const [k, st] of Object.entries(states || {})) {
      current.set(+k, typeof st === "string" ? { state: st, t0: 0 } : st);
      show(+k);
    }
  }

  async function inflate(bytes) {
    const stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream("deflate"));
    return new Uint8Array(await new Response(stream).arrayBuffer());
  }

  function positions(buf, h) {
    const n = h.nv * 3;
    if (h.pos === "f32") return { arr: new Float32Array(buf.buffer, buf.byteOffset, n).slice(), used: n * 4 };
    const q = new Uint16Array(buf.buffer.slice(buf.byteOffset, buf.byteOffset + n * 2));
    const out = new Float32Array(n);
    for (let i = 0; i < n; i++) {
      const a = i % 3;
      out[i] = h.lo[a] + (q[i] / 65535) * h.span[a];
    }
    return { arr: out, used: n * 2 };
  }

  function colours(body, offset, nv) {
    const rgb = new Uint8Array(body.buffer.slice(body.byteOffset + offset,
                                                 body.byteOffset + offset + nv * 3));
    const col = new Float32Array(nv * 3);
    for (let i = 0; i < col.length; i++) col[i] = rgb[i] / 255;
    return new THREE.BufferAttribute(col, 3);
  }

  async function onChunk(data) {
    const view = new DataView(data);
    const hlen = view.getUint32(0, true);
    const h = JSON.parse(new TextDecoder().decode(new Uint8Array(data, 4, hlen)));
    let body = new Uint8Array(data, 4 + hlen);
    if (h.z) body = await inflate(body);
    received++; receivedBytes += data.byteLength;

    const geo = new THREE.BufferGeometry();
    const p = positions(body, h);
    geo.setAttribute("position", new THREE.BufferAttribute(p.arr, 3));

    if (h.kind === "points") {                       // a live participant
      geo.setAttribute("color", colours(body, p.used, h.nv));
      const mat = new THREE.PointsMaterial({
        size: 0.012 * Math.pow(2, h.level), sizeAttenuation: true,
        vertexColors: !tinted(), color: tinted() ? tintFor(h.oid, h.level) : 0xffffff });
      const obj3d = new THREE.Points(geo, mat);
      // the capture is in the person's own frame: place and turn it
      if (h.place) { obj3d.position.set(h.place[0], h.place[1], h.place[2]);
                     obj3d.rotation.y = h.place[3]; }
      else if (h.offset) obj3d.position.set(h.offset[0], h.offset[1], h.offset[2]);
      const prev = people.get(h.oid);
      if (prev) { scene.remove(prev.obj3d); dispose(prev.obj3d); }
      scene.add(obj3d);
      people.set(h.oid, { obj3d, level: h.level });
      return;
    }

    const state = h.state || "default";
    const key = `${h.oid}:${state}`;
    const have = variants.get(key);
    if (have && have.level <= h.level) return;       // never replace a finer copy

    const off = body.byteOffset + p.used;
    const n = h.nt * 3;
    const idx = h.idx === "u16"
      ? new Uint16Array(body.buffer.slice(off, off + n * 2))
      : new Uint32Array(body.buffer.slice(off, off + n * 4));
    geo.setIndex(new THREE.BufferAttribute(idx, 1));
    if (h.rgb) geo.setAttribute("color", colours(body, p.used + idx.byteLength, h.nv));
    let parts = null;
    if (h.parts) {
      const at = body.byteOffset + p.used + idx.byteLength + (h.rgb ? h.nv * 3 : 0);
      parts = new Uint16Array(body.buffer.slice(at, at + h.nv * 2));
    }
    geo.computeVertexNormals();
    const obj3d = new THREE.Mesh(geo, new THREE.MeshStandardMaterial({
      roughness: 0.7, metalness: 0.05, side: THREE.DoubleSide,
      flatShading: h.level >= 2 }));
    obj3d.userData = { hasRgb: !!h.rgb, oid: h.oid,
                       baseColour: new THREE.Color(...(h.colour ||
                         (meta.get(h.oid) || { colour: [0.8, 0.8, 0.8] }).colour)) };
    if (parts && h.anim && h.anim.length) prepareAnimation(obj3d, parts, h.anim);
    paintMaterial(obj3d, h.level);

    const entry = { obj3d, level: h.level };
    if (have) {
      if (shown.get(h.oid) === have) { scene.remove(have.obj3d); shown.delete(h.oid); }
      dispose(have.obj3d);
    }
    variants.set(key, entry);
    if (stateOf(h.oid) === state) show(h.oid);
  }

  document.getElementById("lodTint").addEventListener("change", () => {
    for (const v of variants.values()) paintMaterial(v.obj3d, v.level);
    for (const [oid, v] of people) {
      v.obj3d.material.vertexColors = !tinted();
      v.obj3d.material.color = tinted() ? tintFor(oid, v.level) : new THREE.Color(0xffffff);
      v.obj3d.material.needsUpdate = true;
    }
  });

  // ------------------------------------------------ who speaks, what is shown
  // A ring on the floor under whoever is speaking, and a soft glow on the
  // objects the lesson is about -- so the caption points at something visible.
  const rings = [];
  const ringGeo = new THREE.RingGeometry(0.38, 0.5, 40);
  let spotlight = { speakers: [], focus: [] };

  function ring(i) {
    if (!rings[i]) {
      const m = new THREE.Mesh(ringGeo, new THREE.MeshBasicMaterial({
        color: 0x4fb3ff, transparent: true, opacity: 0.85, side: THREE.DoubleSide }));
      m.rotation.x = -Math.PI / 2;
      scene.add(m);
      rings[i] = m;
    }
    return rings[i];
  }

  // A hand over whoever has raised theirs -- from behind, in a crowd, a raised
  // arm is easy to miss, and a question is a moment the audience must not.
  const handTex = (() => {
    const c = document.createElement("canvas"); c.width = c.height = 128;
    const g = c.getContext("2d");
    g.fillStyle = "rgba(255, 209, 102, 0.95)";
    g.beginPath(); g.arc(64, 64, 58, 0, Math.PI * 2); g.fill();
    g.font = "76px serif"; g.textAlign = "center"; g.textBaseline = "middle";
    g.fillText("\u270B", 64, 70);
    return new THREE.CanvasTexture(c);
  })();
  const hands = [];
  function handIcon(i) {
    if (!hands[i]) {
      hands[i] = new THREE.Sprite(new THREE.SpriteMaterial({ map: handTex, depthTest: false }));
      hands[i].scale.set(0.34, 0.34, 1);
      scene.add(hands[i]);
    }
    return hands[i];
  }

  function updateSpotlight(t) {
    let usedHands = 0;
    for (const oid of spotlight.hands || []) {
      const m = meta.get(oid), person = people.get(oid);
      if (!m) continue;
      const hi = handIcon(usedHands++);
      const at = person ? person.obj3d.position : { x: m.centre[0], z: m.centre[2] };
      hi.position.set(at.x, 2.35 + 0.05 * Math.sin(t * 5), at.z);
      hi.visible = true;
    }
    for (let i = usedHands; i < hands.length; i++) hands[i].visible = false;

    let used = 0;
    for (const oid of spotlight.speakers) {
      const m = meta.get(oid), person = people.get(oid);
      if (!m) continue;
      const r = ring(used++);
      const at = person ? person.obj3d.position : { x: m.centre[0], z: m.centre[2] };
      r.position.set(at.x, 0.02, at.z);
      r.material.opacity = 0.55 + 0.3 * Math.sin(t * 4);
      r.visible = true;
    }
    for (let i = used; i < rings.length; i++) rings[i].visible = false;

    const glow = 0.18 + 0.12 * Math.sin(t * 3);
    for (const v of variants.values()) {
      const on = spotlight.focus.includes(v.obj3d.userData.oid) && shown.get(v.obj3d.userData.oid) === v;
      v.obj3d.material.emissive.setHex(on ? 0x2a6fa8 : 0x000000);
      v.obj3d.material.emissiveIntensity = on ? glow : 0;
    }
  }

  // ---------------------------------------------------------------- animation
  // Continuous change -- a pendulum swinging, drops falling, a colour creeping
  // towards pink, the board filling up stroke by stroke -- arrives as a few
  // motion parameters with the geometry and is played here, in step with the
  // lesson clock.  The network never sends a new mesh for any of it.
  let lessonT = 0, lessonAt = 0, lessonOn = false, pausedAt = 0;
  const clock = () => !lessonOn ? performance.now() / 1000
    : lessonT + ((pausedAt || performance.now()) - lessonAt) / 1000;
  const clamp01 = x => Math.max(0, Math.min(1, x));
  const ease = x => x * x * (3 - 2 * x);

  function prepareAnimation(obj3d, parts, anim) {
    const pos = obj3d.geometry.getAttribute("position");
    const col = obj3d.geometry.getAttribute("color");
    const byPart = new Map();
    for (let i = 0; i < parts.length; i++) {
      if (!byPart.has(parts[i])) byPart.set(parts[i], []);
      byPart.get(parts[i]).push(i);
    }
    Object.assign(obj3d.userData, {
      anim, byPart, sound: {}, lights: [],
      basePos: pos.array.slice(), baseCol: col ? col.array.slice() : null });

    for (const a of anim) {
      if (a.kind === "hot" && col) {
        // Fire, sparks and molten metal glow: the same triangles drawn again,
        // unlit and added on top, sharing the animated positions and colours.
        const hot = new Set(a.parts), src = obj3d.geometry.index.array, keep = [], rest = [];
        for (let t = 0; t < src.length; t += 3)
          (hot.has(parts[src[t]]) && hot.has(parts[src[t + 1]]) && hot.has(parts[src[t + 2]])
            ? keep : rest).push(src[t], src[t + 1], src[t + 2]);
        const g2 = new THREE.BufferGeometry();
        g2.setAttribute("position", pos);
        g2.setAttribute("color", col);
        g2.setIndex(keep);
        let glow;
        if (a.unlit) {
          // a fireball is light, not a lit surface: drawn flat in its own
          // colours instead of the solid mesh, and it can fade away
          obj3d.geometry.setIndex(rest);
          glow = new THREE.Mesh(g2, new THREE.MeshBasicMaterial({
            vertexColors: true, transparent: true, depthWrite: false }));
          obj3d.userData.fireLayer = glow;
        } else {
          glow = new THREE.Mesh(g2, new THREE.MeshBasicMaterial({
            vertexColors: true, blending: THREE.AdditiveBlending, transparent: true,
            depthWrite: false, polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -4 }));
          glow.userData.glow = true;
        }
        glow.frustumCulled = false;
        obj3d.add(glow);
      }
      if (a.kind === "soft") {
        // smoke: take its triangles out of the solid mesh and draw them
        // see-through, so it hangs in the air instead of looking like balls
        const soft = new Set(a.parts), src = obj3d.geometry.index.array, keep = [], rest = [];
        for (let t = 0; t < src.length; t += 3)
          (soft.has(parts[src[t]]) ? keep : rest).push(src[t], src[t + 1], src[t + 2]);
        obj3d.geometry.setIndex(rest);
        const g2 = new THREE.BufferGeometry();
        g2.setAttribute("position", pos);
        if (col) g2.setAttribute("color", col);
        g2.setAttribute("normal", obj3d.geometry.getAttribute("normal"));
        g2.setIndex(keep);
        const puff = new THREE.Mesh(g2, new THREE.MeshLambertMaterial({
          vertexColors: !!col, transparent: true, opacity: a.opacity, depthWrite: false }));
        puff.frustumCulled = false;
        obj3d.add(puff);
      }
      if (a.kind === "light") {
        const l = new THREE.PointLight(new THREE.Color(...a.colour), 0, 7, 2);
        l.position.set(a.at[0], a.at[1], a.at[2]);
        obj3d.add(l);
        obj3d.userData.lights.push([l, a]);
      }
      if (a.kind === "burst" || a.kind === "blast") obj3d.frustumCulled = false;
    }
  }

  function centroid(u, k, idx) {
    u.centroids = u.centroids || new Map();
    if (!u.centroids.has(k)) {
      const c = [0, 0, 0];
      for (const i of idx) for (let d = 0; d < 3; d++) c[d] += u.basePos[3 * i + d];
      u.centroids.set(k, c.map(x => x / idx.length));
    }
    return u.centroids.get(k);
  }

  // deterministic "random" numbers per particle, identical on every viewer
  const rnd = (j, k) => { const x = Math.sin(j * 12.9898 + k * 78.233) * 43758.5453; return x - Math.floor(x); };
  const FIRE = [[1, 1, 0.9], [1, 0.85, 0.35], [1, 0.45, 0.1], [0.45, 0.1, 0.04], [0.22, 0.2, 0.2]];
  function fireColour(f) {                    // white -> yellow -> orange -> red -> smoke
    const x = clamp01(f) * (FIRE.length - 1), i = Math.min(FIRE.length - 2, Math.floor(x));
    return mix(FIRE[i], FIRE[i + 1], x - i);
  }
  let flash = 0, shake = 0;                   // a blast flashes the screen and shakes the view

  function rotateZ(P, B, idx, pv, a) {
    const c = Math.cos(a), s = Math.sin(a);
    for (const i of idx) {
      const x = B[3 * i] - pv[0], y = B[3 * i + 1] - pv[1];
      P[3 * i] = pv[0] + c * x - s * y;
      P[3 * i + 1] = pv[1] + s * x + c * y;
    }
  }
  function collapse(P, idx) {               // hide a part: all its vertices on one point
    const j = idx[0];
    for (const i of idx) { P[3*i] = P[3*j]; P[3*i+1] = P[3*j+1]; P[3*i+2] = P[3*j+2]; }
  }
  function setColour(C, idx, rgb) {
    for (const i of idx) { C[3 * i] = rgb[0]; C[3 * i + 1] = rgb[1]; C[3 * i + 2] = rgb[2]; }
  }
  const mix = (a, b, w) => [a[0] + (b[0] - a[0]) * w, a[1] + (b[1] - a[1]) * w, a[2] + (b[2] - a[2]) * w];
  const range = (a, b) => Array.from({ length: b - a + 1 }, (_, k) => a + k);

  // A marker pen that sits on the stroke being written right now, so the
  // audience sees exactly where on the board the writing is happening.
  const pen = new THREE.Mesh(
    new THREE.CylinderGeometry(0.018, 0.012, 0.15, 12),
    new THREE.MeshStandardMaterial({ color: 0x1b3a8a, roughness: 0.4 }));
  pen.rotation.x = Math.PI / 2.6;
  pen.visible = false;
  scene.add(pen);

  function animate(v) {
    const u = v.obj3d.userData;
    if (!u.anim) return;
    const geo = v.obj3d.geometry;
    const pos = geo.getAttribute("position"), col = geo.getAttribute("color");
    const P = pos.array, C = col ? col.array : null;
    P.set(u.basePos);
    if (C && u.baseCol) C.set(u.baseCol);
    const el = Math.max(0, clock() - ((current.get(u.oid) || {}).t0 || 0));

    for (const a of u.anim) {
      const idxOf = k => u.byPart.get(k) || [];
      switch (a.kind) {
        case "reveal": case "sweep": {
          const ids = range(a.from, a.to), n = ids.length;
          let shown;
          if (a.kind === "reveal" && a.times) {
            // strokes appear on the writer's timetable: pauses between lines
            let lo = 0, hi = a.times.length;
            while (lo < hi) { const mid = (lo + hi) >> 1; if (a.times[mid] <= el) lo = mid + 1; else hi = mid; }
            shown = el <= 0 ? 0 : lo;
          } else {
            const f = a.kind === "reveal" ? clamp01(el / a.duration)
                                          : (el % a.period) / a.period;
            shown = Math.floor(f * n + (a.kind === "sweep" ? 1 : 0));
          }
          ids.forEach((k, j) => { if (j >= shown) collapse(P, idxOf(k)); });
          if (a.kind === "reveal" && shown > 0 && shown < n) {
            // put the pen on the tip of the newest stroke
            const idx = idxOf(ids[shown - 1]);
            let x = 0, y = 0, z = 0;
            for (const i of idx) { x += P[3 * i]; y += P[3 * i + 1]; z += P[3 * i + 2]; }
            pen.position.set(x / idx.length, y / idx.length, z / idx.length + 0.07);
            pen.visible = true;
          }
          sfx(v, a, el, shown < n && a.kind === "reveal");
          break;
        }
        case "swing": {
          const ang = (a.bias || 0) + a.amp * Math.cos(2 * Math.PI * el / a.period)
                      * Math.exp(-(a.decay || 0) * el);
          rotateZ(P, u.basePos, idxOf(a.part), a.pivot, ang);
          sfx(v, a, el, true);
          break;
        }
        case "turn": {
          const w = ease(clamp01(el / a.duration));
          rotateZ(P, u.basePos, idxOf(a.part), a.pivot, a.from + (a.to - a.from) * w);
          sfx(v, a, el, true);
          break;
        }
        case "fall": case "rise": {
          const ids = range(a.from, a.to), n = ids.length;
          ids.forEach((k, j) => {
            const ph = ((el / a.period) + j / n) % 1;
            const dy = a.kind === "fall" ? -a.dist * ph * ph : a.height * ph;
            const dx = a.kind === "rise" ? (a.drift || 0.012) * Math.sin(6 * ph + j) : 0;
            const dz = a.kind === "rise" && a.drift ? a.drift * Math.cos(4 * ph + 2 * j) : 0;
            const idx = idxOf(k);
            if (a.grow && idx.length) {
              // smoke swells as it rises: scale each puff about its own centre
              const c = centroid(u, k, idx), sc = 1 + a.grow * ph;
              for (const i of idx)
                for (let d = 0; d < 3; d++) P[3 * i + d] = c[d] + (P[3 * i + d] - c[d]) * sc;
            }
            for (const i of idx) { P[3 * i] += dx; P[3 * i + 1] += dy; P[3 * i + 2] += dz; }
          });
          sfx(v, a, el, true);
          break;
        }
        case "flicker": {
          const sc = 1 + a.amount * (0.6 * Math.sin(2 * Math.PI * a.freq * el)
                                    + 0.4 * Math.sin(2 * Math.PI * 2.3 * a.freq * el));
          for (const i of idxOf(a.part)) P[3 * i + 1] = a.pivot[1] + (u.basePos[3 * i + 1] - a.pivot[1]) * sc;
          sfx(v, a, el, true);
          break;
        }
        case "burst": {
          // particles thrown from a centre under gravity: debris once, or a
          // fountain of sparks over and over; they land on the bench or floor
          const ids = range(a.from, a.to), n = ids.length, g = a.gravity || 9.8;
          ids.forEach((k, j) => {
            const idx = idxOf(k);
            if (!idx.length) return;
            let life = el - (a.delay || 0);
            if (a.period) life = (((life / a.period) + j / n) % 1 + 1) % 1 * a.period;
            if (life < 0) { collapse(P, idx); return; }
            const th = 2 * Math.PI * rnd(j, 1), h = a.spread * Math.sqrt(rnd(j, 2));
            const sp = a.speed * (0.55 + 0.45 * rnd(j, 3));
            const v = [Math.cos(th) * h * sp, a.up * (0.5 + 0.5 * rnd(j, 4)) * sp, Math.sin(th) * h * sp];
            const c = a.centre;
            // where it comes down: on the bench if over it, else on the floor
            let y0 = a.floor || 0;
            const land = y => (v[1] + Math.sqrt(Math.max(0, v[1] * v[1] + 2 * g * (c[1] - y)))) / g;
            if (a.table) {
              const tl = land(a.table[4]), x = c[0] + v[0] * tl, z = c[2] + v[2] * tl;
              if (c[1] >= a.table[4] && x > a.table[0] && x < a.table[1] && z > a.table[2] && z < a.table[3])
                y0 = a.table[4];
            }
            const t = Math.min(life, land(y0));
            const d = [v[0] * t, v[1] * t - 0.5 * g * t * t, v[2] * t];
            for (const i of idx) { P[3 * i] += d[0]; P[3 * i + 1] += d[1]; P[3 * i + 2] += d[2]; }
            if (C && a.hot && !tinted()) setColour(C, idx, mix(a.hot, a.cool, clamp01(life / a.cool_s)));
          });
          sfx(v, a, el, true);
          break;
        }
        case "blast": {
          // a fireball: bursts out in a fraction of a second, rolls upwards,
          // burns from white through orange to dark smoke, and is gone
          const idx = idxOf(a.part), t = el - (a.delay || 0), c = a.centre;
          if (t < 0 || t > a.duration) { collapse(P, idx); break; }
          const sc = (0.12 + 0.88 * ease(clamp01(t / a.grow))) * (1 + 0.35 * t / a.duration);
          const lift = 0.5 * t;
          for (const i of idx) {
            P[3 * i] = c[0] + (u.basePos[3 * i] - c[0]) * sc;
            P[3 * i + 1] = c[1] + lift + (u.basePos[3 * i + 1] - c[1]) * sc;
            P[3 * i + 2] = c[2] + (u.basePos[3 * i + 2] - c[2]) * sc;
          }
          if (C && !tinted()) {
            // hotter in the core than at the edges, with churning turbulence
            const f = t / a.duration;
            let rmax = 1e-6;
            for (const i of idx) {
              const x = u.basePos[3 * i] - c[0], y = u.basePos[3 * i + 1] - c[1], z = u.basePos[3 * i + 2] - c[2];
              rmax = Math.max(rmax, x * x + y * y + z * z);
            }
            rmax = Math.sqrt(rmax);
            for (const i of idx) {
              const x = u.basePos[3 * i] - c[0], y = u.basePos[3 * i + 1] - c[1], z = u.basePos[3 * i + 2] - c[2];
              const edge = Math.sqrt(x * x + y * y + z * z) / rmax;
              const churn = Math.sin(x * 23 + t * 9) * Math.sin(y * 19 - t * 7) * Math.sin(z * 21 + t * 5);
              const col = fireColour(f * 1.1 + 0.45 * edge * edge + 0.12 * churn);
              const w = 0.85 + 0.15 * churn;
              C[3 * i] = col[0] * w; C[3 * i + 1] = col[1] * w; C[3 * i + 2] = col[2] * w;
            }
          }
          if (u.fireLayer) u.fireLayer.material.opacity = 1 - clamp01((t / a.duration - 0.55) / 0.45);
          if (t < 0.4) { flash = Math.max(flash, 1 - t / 0.4); shake = Math.max(shake, 1 - t / 0.6); }
          sfx(v, a, t, true);
          break;
        }
        case "colour": case "pulse": {
          if (!C || tinted()) break;
          const w = a.kind === "colour" ? ease(clamp01(el / a.duration))
                                        : 0.5 + 0.5 * Math.sin(2 * Math.PI * el / a.period);
          setColour(C, idxOf(a.part), mix(a.from, a.to, w));
          if (a.kind === "pulse") sfx(v, a, el, true);
          break;
        }
      }
    }
    // lights at fires and blasts: a steady flicker, or a flash dying away
    for (const [l, a] of u.lights) {
      const flick = 1 + (a.flicker || 0) * (0.6 * Math.sin(el * 31) + 0.4 * Math.sin(el * 17.3 + 1));
      l.intensity = tinted() ? 0 : a.intensity * flick * (a.decay ? Math.exp(-el / a.decay) : 1);
    }
    for (const c of v.obj3d.children) if (c.userData.glow) c.visible = !tinted();
    pos.needsUpdate = true;
    if (col) col.needsUpdate = true;
  }

  // ---------------------------------------------------------------- sound
  // Narration reads each step aloud.  Effects are synthesised on the spot (no
  // audio files) and placed where they happen, so the drip comes from the
  // burette and the pendulum ticks from its bench.  Sound is not part of the
  // bandwidth model; in a real system each voice would add roughly 24-64
  // kbit/s, small next to the geometry.
  const soundBox = document.getElementById("sound");
  let actx = null, master = null, noiseBuf = null;
  const loops = new Map();         // "oid:kind" -> { nodes, seen }

  function audioReady() {
    if (!soundBox.checked) return false;
    if (!actx) {
      try {
        actx = new (window.AudioContext || window.webkitAudioContext)();
        master = actx.createGain(); master.gain.value = 0.35;
        master.connect(actx.destination);
        noiseBuf = actx.createBuffer(1, actx.sampleRate, actx.sampleRate);
        const d = noiseBuf.getChannelData(0);
        for (let i = 0; i < d.length; i++) d[i] = Math.random() * 2 - 1;
      } catch (e) { return false; }
    }
    if (actx.state === "suspended") actx.resume();
    return actx.state === "running";
  }
  for (const ev of ["pointerdown", "keydown", "change"])
    window.addEventListener(ev, () => { if (soundBox.checked) audioReady(); }, true);

  function panner(pos) {
    const p = actx.createPanner();
    p.panningModel = "HRTF"; p.distanceModel = "inverse"; p.refDistance = 1.2;
    p.positionX.value = pos[0]; p.positionY.value = pos[1]; p.positionZ.value = pos[2];
    p.connect(master);
    return p;
  }

  function blip(pos, { freq = 800, to = null, dur = 0.08, gain = 0.25, type = "sine", noise = false, q = 1 }) {
    const now = actx.currentTime, out = panner(pos), g = actx.createGain();
    g.gain.setValueAtTime(gain, now);
    g.gain.exponentialRampToValueAtTime(0.0008, now + dur);
    g.connect(out);
    let src;
    if (noise) {
      src = actx.createBufferSource(); src.buffer = noiseBuf;
      const f = actx.createBiquadFilter(); f.type = "bandpass"; f.frequency.value = freq; f.Q.value = q;
      src.connect(f); f.connect(g);
    } else {
      src = actx.createOscillator(); src.type = type;
      src.frequency.setValueAtTime(freq, now);
      if (to) src.frequency.exponentialRampToValueAtTime(to, now + dur);
      src.connect(g);
    }
    src.start(now); src.stop(now + dur + 0.02);
  }

  function loop(key, pos, make) {
    let l = loops.get(key);
    if (!l) { l = { nodes: make(panner(pos)), seen: 0 }; loops.set(key, l); }
    l.seen = performance.now();
  }
  function hum(freq, type, gain, lp) {
    return out => {
      const o = actx.createOscillator(), g = actx.createGain(), f = actx.createBiquadFilter();
      o.type = type; o.frequency.value = freq; f.type = "lowpass"; f.frequency.value = lp;
      g.gain.value = gain; o.connect(f); f.connect(g); g.connect(out); o.start();
      return [o, g, f];
    };
  }
  function hiss(freq, gain) {
    return out => {
      const s = actx.createBufferSource(), g = actx.createGain(), f = actx.createBiquadFilter();
      s.buffer = noiseBuf; s.loop = true; f.type = "bandpass"; f.frequency.value = freq; f.Q.value = 0.7;
      g.gain.value = gain; s.connect(f); f.connect(g); g.connect(out); s.start();
      return [s, g, f];
    };
  }
  function reapLoops() {
    const now = performance.now();
    for (const [k, l] of loops) {
      if (now - l.seen > 300 || !soundBox.checked) {
        for (const n of l.nodes) { try { n.stop && n.stop(); } catch (e) {} n.disconnect(); }
        loops.delete(k);
      }
    }
  }

  function sfx(v, a, el, active) {
    if (!active || !audioReady()) return;
    const u = v.obj3d.userData, m = meta.get(u.oid);
    if (!m) return;
    const pos = m.centre, st = u.sound, key = `${u.oid}:${a.kind}`;
    const tick = (period, name) => {            // true once per period
      const n = Math.floor(el / period);
      if (st[name] === n) return false;
      st[name] = n; return true;
    };
    switch (a.kind) {
      case "reveal":                             // a marker squeaking on the board
        if (tick(0.11, "pen") && Math.random() < 0.8)
          blip(pos, { noise: true, freq: 2600 + Math.random() * 1400, dur: 0.07, gain: 0.12, q: 4 });
        break;
      case "fall":                               // a drop landing
        if (tick(a.period / (a.to - a.from + 1), "drip"))
          blip(pos, { freq: 1500, to: 380, dur: 0.09, gain: 0.3 });
        break;
      case "rise":                               // bubbling
        if (tick(0.09, "bub") && Math.random() < 0.55)
          blip(pos, { freq: 500 + Math.random() * 700, to: 900 + Math.random() * 600, dur: 0.05, gain: 0.08 });
        break;
      case "flicker": loop(key, pos, hiss(a.freq > 7 ? 1400 : 900, a.freq > 7 ? 0.14 : 0.07)); break; // fire
      case "turn": loop(key, pos, hum(55, "sawtooth", 0.06, 300)); break;  // pump motor
      case "swing":                              // tick at each end of the swing
        if (tick(a.period / 2, "swing"))
          blip(pos, { freq: 2100, dur: 0.025, gain: 0.1, type: "square" });
        break;
      case "burst":
        if (a.period) {                          // sparks crackle
          if (tick(0.05, "crackle") && Math.random() < 0.6)
            blip(pos, { noise: true, freq: 3000 + Math.random() * 3000, dur: 0.03, gain: 0.12, q: 2 });
        } else if (!st.debris && el > 0.25) {     // debris rattling down
          st.debris = true;
          for (let k = 0; k < 6; k++)
            setTimeout(() => blip(pos, { noise: true, freq: 4000 + Math.random() * 2000, dur: 0.06,
                                         gain: 0.18, q: 3 }), 250 + k * 90 + Math.random() * 80);
        }
        break;
      case "blast":                              // the bang: once, as it goes off
        if (!st.boom && el >= 0 && el < 0.5) {
          st.boom = true;
          blip(pos, { noise: true, freq: 90, dur: 1.8, gain: 1.4, q: 0.4 });
          blip(pos, { noise: true, freq: 900, dur: 0.35, gain: 0.8, q: 0.5 });
          blip(pos, { freq: 70, to: 30, dur: 1.0, gain: 0.9 });
        }
        break;
      case "pulse":                              // the light gate beeps
        if (a.period >= 0.5 && tick(a.period, "beep")) blip(pos, { freq: 1320, dur: 0.06, gain: 0.07 });
        break;
    }
  }

  let spoken = "";
  function narrate(text, byStudent) {
    if (!soundBox.checked || !("speechSynthesis" in window) || text === spoken) return;
    spoken = text;
    speechSynthesis.cancel();
    const say = new SpeechSynthesisUtterance(text);
    const voices = speechSynthesis.getVoices().filter(v => /^en/i.test(v.lang));
    if (voices.length) say.voice = voices.find(v => /GB|UK/i.test(v.lang + v.name)) || voices[0];
    say.rate = 1.0; say.pitch = byStudent ? 1.2 : 1.0;
    speechSynthesis.speak(say);
  }
  soundBox.addEventListener("change", () => {
    if (!soundBox.checked) { if (window.speechSynthesis) window.speechSynthesis.cancel(); reapLoops(); }
    else { spoken = ""; audioReady(); }
  });

  function updateListener() {
    if (!actx) return;
    const L = actx.listener, f = forward();
    if (L.positionX) {
      L.positionX.value = camera.position.x; L.positionY.value = camera.position.y;
      L.positionZ.value = camera.position.z;
      L.forwardX.value = f.x; L.forwardY.value = f.y; L.forwardZ.value = f.z;
      L.upX.value = 0; L.upY.value = 1; L.upZ.value = 0;
    }
  }

  // ---------------------------------------------------------------- network
  const $ = id => document.getElementById(id);
  const policySel = $("policy"), bw = $("bw"), scenarioSel = $("scenario");
  const lessonSel = $("lesson");
  let ws, policies = [], scenarioList = [], scenario = null, lessonList = [];
  const qoeHistory = [];
  const FIVE_G = 100;              // the top of the slider: a fast fibre / 5G link
  let beforeFiveG = 10;            // the speed to come back to
  const rate = { t: 0, bytes: 0, mbps: 0 };

  // Changing the strategy or the speed does not restart anything: the lesson
  // carries on and what is already on screen stays. Only R (or a new room)
  // starts over.
  function sendConfig(restart = false) {
    if (!ws || ws.readyState !== 1) return;
    ws.send(JSON.stringify({
      type: "config", scenario: scenarioSel.value || undefined,
      policy: policySel.value || undefined, bandwidth_mbps: +bw.value, restart }));
  }

  function connect() {
    ws = new WebSocket(`${location.protocol === "https:" ? "wss" : "ws"}://${location.host}/ws`);
    ws.binaryType = "arraybuffer";
    ws.onopen = () => { $("conn").textContent = "live"; $("conn").style.color = "var(--ok)"; sendConfig(true); };
    ws.onclose = () => { $("conn").textContent = "reconnecting…"; $("conn").style.color = "var(--bad)"; setTimeout(connect, 1000); };
    ws.onmessage = ev => {
      if (typeof ev.data !== "string") { onChunk(ev.data); return; }
      const msg = JSON.parse(ev.data);
      if (msg.type === "manifest") onManifest(msg);
      else if (msg.type === "reset") {
        clearScene(); qoeHistory.length = 0; rate.t = 0; rate.bytes = 0;
        spoken = ""; lessonT = 0; lessonAt = performance.now();
        if (pausedAt) { pausedAt = 0; $("pause").textContent = "⏸ Pause";
                        $("caption").classList.remove("paused"); }
      }
      else if (msg.type === "states") setStates(msg.states);
      else if (msg.type === "metrics") onMetrics(msg);
      else if (msg.type === "pose" && autopilot) {
        camera.position.set(msg.pos[0], msg.pos[1], msg.pos[2]);
        const f = msg.forward;
        yaw = Math.atan2(-f[0], -f[2]);
        pitch = Math.asin(Math.max(-1, Math.min(1, f[1])));
      }
    };
  }

  function onManifest(m) {
    meta.clear();
    for (const o of m.objects) meta.set(o.oid, o);
    setStates(m.states);

    if (m.lessons && !lessonList.length) {
      lessonList = m.lessons;
      lessonSel.appendChild(new Option("Free exploration (no lesson)", ""));
      for (const l of lessonList) lessonSel.appendChild(new Option(l.title, l.name));
    }
    if ("lesson" in m) {
      lessonSel.value = m.lesson || "";
      const l = lessonList.find(x => x.name === m.lesson);
      $("lessonDesc").textContent = l ? l.summary : "";
      $("safety").hidden = !(l && l.banner);
      if (l && l.banner) $("safetyText").textContent = l.banner;
      document.body.classList.toggle("safe", !!(l && l.banner));
      autopilot = !!m.lesson;
      $("caption").hidden = !m.lesson;
      $("follow").hidden = true;
    }

    if (m.scenarios && !scenarioList.length) {
      scenarioList = m.scenarios;
      for (const sc of scenarioList) {
        const opt = document.createElement("option");
        opt.value = sc.name; opt.textContent = sc.title;
        scenarioSel.appendChild(opt);
      }
    }
    if (m.scenario && m.scenario !== scenario) {
      scenario = m.scenario;
      scenarioSel.value = scenario;
      const sc = scenarioList.find(x => x.name === scenario);
      if (sc) {
        $("scenarioDesc").textContent = sc.blurb || "";
        home(sc.home, sc.look);       // drop the viewer somewhere sensible
      }
    }
    if (!policies.length) {
      policies = m.policies;
      for (const p of policies) {
        const opt = document.createElement("option");
        opt.value = p.name; opt.textContent = label(p.name);
        policySel.appendChild(opt);
      }
      policySel.value = "adaptive";
      $("policyDesc").textContent = blurb("adaptive");
    }
  }

  lessonSel.addEventListener("change", () => {
    clearScene();
    $("verdict").textContent = lessonSel.value ? "starting lesson…" : "loading…";
    if (ws && ws.readyState === 1)
      ws.send(JSON.stringify({ type: "config", lesson: lessonSel.value,
                               policy: policySel.value || undefined }));
  });

  function setPaused(on) {
    if (!lessonOn && on) return;                 // nothing to pause
    const now = performance.now();
    if (on && !pausedAt) pausedAt = now;
    if (!on && pausedAt) { lessonAt += now - pausedAt; pausedAt = 0; }
    $("pause").textContent = on ? "▶ Resume" : "⏸ Pause";
    $("caption").classList.toggle("paused", on);
    if (ws && ws.readyState === 1) ws.send(JSON.stringify({ type: "pause", on }));
    if (window.speechSynthesis) on ? speechSynthesis.pause() : speechSynthesis.resume();
    if (actx) on ? actx.suspend() : actx.resume();
  }
  $("pause").addEventListener("click", () => setPaused(!pausedAt));
  window.addEventListener("keydown", e => {
    if (e.code === "Space" && e.target.tagName !== "INPUT" && e.target.tagName !== "SELECT") {
      e.preventDefault(); setPaused(!pausedAt);
    }
  });

  $("hideBtn").addEventListener("click", () => document.body.classList.add("clean"));
  $("showBtn").addEventListener("click", () => document.body.classList.remove("clean"));

  $("follow").addEventListener("click", () => {
    autopilot = true;
    $("follow").hidden = true;
    if (ws && ws.readyState === 1) ws.send(JSON.stringify({ type: "autopilot", on: true }));
  });

  scenarioSel.addEventListener("change", () => {
    clearScene();
    $("verdict").textContent = "loading lesson…";
    sendConfig(true);
  });

  policySel.addEventListener("change", () => {
    const p = policies.find(x => x.name === policySel.value);
    $("policyDesc").textContent = blurb(policySel.value, p && p.description);
    sendConfig();
  });
  function showSpeed() {
    $("bwLabel").textContent = bw.value;
    $("bwHint").textContent = speedHint(+bw.value);
    $("fiveG").classList.toggle("on", +bw.value >= FIVE_G);
  }
  bw.addEventListener("input", showSpeed);
  showSpeed();
  bw.addEventListener("change", () => sendConfig());

  // One click for the happy case: a 5G link, where everything arrives at full
  // quality. Clicking again puts the old speed back, so the difference is easy
  // to show off. Neither restarts the lesson.
  $("fiveG").addEventListener("click", () => {
    const on = +bw.value >= FIVE_G;
    bw.value = on ? beforeFiveG : FIVE_G;
    if (!on) beforeFiveG = +$("bwLabel").textContent;
    showSpeed();
    sendConfig();
  });

  // click on the lesson's progress bar to jump to that moment
  let lessonDuration = 0;
  $("capBar").parentElement.addEventListener("click", e => {
    if (!lessonDuration || !ws || ws.readyState !== 1) return;
    const r = e.currentTarget.getBoundingClientRect();
    if (!r.width) return;
    const t = clamp01((e.clientX - r.left) / r.width) * lessonDuration;
    if (pausedAt) setPaused(false);
    ws.send(JSON.stringify({ type: "seek", t }));
  });

  function onLesson(l) {
    spotlight = l ? { speakers: l.speakers || [], focus: l.focus || [], hands: l.hands || [] }
                  : { speakers: [], focus: [], hands: [] };
    lessonOn = !!l && !l.finished;
    if (!l) return;
    lessonDuration = l.duration;
    if (!pausedAt) { lessonT = l.t; lessonAt = performance.now(); }
    if (!l.finished) {
      const students = (l.speakers || []).some(o => /student|athlete/.test((meta.get(o) || {}).name || ""));
      narrate(l.caption, students);
    }
    $("caption").hidden = false;
    $("capStep").textContent = l.finished
      ? `${l.title} · finished`
      : `${l.title} · step ${l.step} of ${l.steps}`;
    $("capText").textContent = l.finished
      ? "The lesson is over. Look around, or pick it again to replay."
      : l.caption;
    $("capBar").style.width = `${Math.min(100, (l.t / l.duration) * 100)}%`;
    if (l.finished) { autopilot = false; spotlight = { speakers: [], focus: [] }; }
  }

  function onMetrics(m) {
    onLesson(m.lesson);
    // Show quality as a 0-100 score with a plain-language verdict, rather
    // than the raw weighted-penalty figure the benchmark uses.
    const q = m.qoe;
    const score = Math.round(q * 100);
    $("qoe").textContent = score;
    const colour = q > 0.85 ? "var(--ok)" : q > 0.6 ? "var(--warn)" : "var(--bad)";
    $("qoe").style.color = colour;
    const verdict = $("verdict");
    verdict.textContent = q > 0.85 ? "Looks good"
      : q > 0.6 ? "Noticeably degraded" : "Badly broken";
    verdict.style.color = colour;

    // "people moving smoothly" = participants arriving at a watchable rate
    $("qoe5g").hidden = !(m.bandwidth_mbps >= FIVE_G);

    const fps = Object.values(m.participant_fps || {});
    const smooth = fps.filter(v => v >= 8).length;
    $("liveCount").textContent = `${smooth} of ${fps.length || 7}`;
    $("liveBar").style.width = `${fps.length ? (smooth / fps.length) * 100 : 0}%`;
    $("liveBar").style.background = smooth === fps.length ? "var(--ok)"
      : smooth > 0 ? "var(--warn)" : "var(--bad)";

    $("recv").textContent = `${(receivedBytes / 1e6).toFixed(1)} MB`;

    const now = performance.now();
    if (rate.t) {
      const inst = (receivedBytes - rate.bytes) * 8 / 1e6 / ((now - rate.t) / 1000);
      rate.mbps = rate.mbps * 0.7 + Math.max(0, inst) * 0.3;
    }
    rate.t = now; rate.bytes = receivedBytes;
    $("mbps").textContent = `${rate.mbps.toFixed(1)} Mbit/s`;
    $("cap").textContent = `${(m.capacity_mbps ?? 0).toFixed(1)} Mbit/s`;
    $("stale").textContent = `${m.staleness_ms.toFixed(0)} ms`;
    $("vis").textContent = `${m.visible} / ${m.missing}`;

    qoeHistory.push(q);
    if (qoeHistory.length > 130) qoeHistory.shift();
    const c = $("spark"), g = c.getContext("2d");
    g.clearRect(0, 0, c.width, c.height);
    g.strokeStyle = "#4fb3ff"; g.lineWidth = 1.5; g.beginPath();
    qoeHistory.forEach((v, i) => {
      const x = (i / 129) * c.width, y = c.height - v * (c.height - 2) - 1;
      i ? g.lineTo(x, y) : g.moveTo(x, y);
    });
    g.stroke();
  }

  // ---------------------------------------------------------------- loop
  let last = performance.now(), lastSent = 0;
  function frame(now) {
    const dt = Math.min(0.1, (now - last) / 1000);
    last = now;

    const f = forward();
    const flat = new THREE.Vector3(f.x, 0, f.z).normalize();
    const right = new THREE.Vector3().crossVectors(flat, new THREE.Vector3(0, 1, 0));
    const speed = autopilot ? 0 : (keys.has("shift") ? 6 : 2.5) * dt;
    if (keys.has("w")) camera.position.addScaledVector(flat, speed);
    if (keys.has("s")) camera.position.addScaledVector(flat, -speed);
    if (keys.has("d")) camera.position.addScaledVector(right, speed);
    if (keys.has("a")) camera.position.addScaledVector(right, -speed);
    if (keys.has("e")) camera.position.y += speed;
    if (keys.has("q")) camera.position.y -= speed;
    camera.lookAt(camera.position.clone().add(f));

    if (ws && ws.readyState === 1 && now - lastSent > 50) {
      lastSent = now;
      ws.send(JSON.stringify({
        type: "camera", pos: camera.position.toArray(), forward: f.toArray(),
        fov_y_deg: camera.fov, aspect: camera.aspect, screen_h: window.innerHeight }));
    }
    updateSpotlight(now / 1000);
    pen.visible = false;
    flash = 0; shake = 0;
    for (const v of shown.values()) animate(v);
    $("flash").style.opacity = (0.85 * flash).toFixed(3);
    if (shake > 0) {
      const k = 0.035 * shake;
      camera.position.x += (Math.random() - 0.5) * k; camera.position.y += (Math.random() - 0.5) * k;
    }
    updateListener();
    reapLoops();
    renderer.render(scene, camera);
    requestAnimationFrame(frame);
  }

  // for debugging and automated checks: the scene and what is on screen
  window.__uc7 = { scene, meta, shown, variants, current, clock: () => clock() };

  connect();
  requestAnimationFrame(frame);
})();
