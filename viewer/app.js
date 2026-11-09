// OCT hologram viewer: previews an exported scan the way the Looking Glass
// HLD shows it (white = see-through, dark = visible). It can also run
// full-screen on the HLD itself:  index.html?hld=1
//
// URL params:  scan=<name>  data=<folder with index.json>  hld=1  sim=0|1
import * as THREE from "three";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";
import { OrbitControls } from "three/addons/controls/OrbitControls.js";

const params = new URLSearchParams(location.search);
const DATA = (params.get("data") || "../out").replace(/\/$/, "");
const HLD = params.get("hld") === "1";
document.body.classList.toggle("hld", HLD);
document.body.classList.toggle("sim", !HLD && params.get("sim") !== "0");

const $ = (id) => document.getElementById(id);
const stage = $("stage");

// ------------------------------------------------------------------ renderer
const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
renderer.setClearColor(0xffffff, 1);
renderer.toneMapping = THREE.NoToneMapping;          // tone mapping turns white grey
renderer.outputColorSpace = THREE.SRGBColorSpace;
stage.prepend(renderer.domElement);

const scene = new THREE.Scene();
// narrow lens like the HLD template camera (15 deg) so the flat image
// matches the physical box perspective
const camera = new THREE.PerspectiveCamera(15, 9 / 16, 0.1, 200);
scene.add(new THREE.HemisphereLight(0xffffff, 0xb8b4ac, 1.6));
const key = new THREE.DirectionalLight(0xffffff, 2.2);
key.position.set(Math.sin(THREE.MathUtils.degToRad(55)) * 10, 8, Math.cos(THREE.MathUtils.degToRad(55)) * 10);
scene.add(key);

const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true;
controls.enablePan = false;
controls.minPolarAngle = THREE.MathUtils.degToRad(20);
controls.maxPolarAngle = THREE.MathUtils.degToRad(100);

function resize() {
  const w = stage.clientWidth, h = stage.clientHeight;
  renderer.setSize(w, h, false);
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
}
new ResizeObserver(resize).observe(stage);

// ------------------------------------------------------------------ state
const state = {
  scans: [], index: 0,
  spin: true, sway: false, help: false,
  showTissue: false, showFluids: true,
  mode: "volume",                 // volume | bscan | off
  bscan: 0,
  density: 0.22,
  ink: new THREE.Color("#121a33"),
  threshold: 0.05,                // added to the exported threshold
  gamma: 1.4,
};
let current = null;               // { root, spinner, meta, stacks, tissue, fluids, shadow }

// ------------------------------------------------------------------ loading
async function loadIndex() {
  try {
    const r = await fetch(`${DATA}/index.json`);
    if (r.ok) state.scans = (await r.json()).scans || [];
  } catch { /* single scan via ?scan= */ }
  const want = params.get("scan");
  if (want && !state.scans.includes(want)) state.scans.unshift(want);
  if (!state.scans.length) throw new Error(`No scans found in ${DATA}/index.json. Run the exporter first.`);
  state.index = Math.max(0, state.scans.indexOf(want));
}

async function loadScan(name) {
  const base = `${DATA}/${name}`;
  const meta = await (await fetch(`${base}/meta.json`)).json();

  const root = new THREE.Group();      // fitted & placed in the alcove
  const spinner = new THREE.Group();   // spins on its own axis
  const content = new THREE.Group();   // export coordinates (mm, Y up)
  root.add(spinner); spinner.add(content);

  const tissue = [], fluids = [];
  if (meta.model) {
    const gltf = await new GLTFLoader().loadAsync(`${base}/${meta.model}`);
    gltf.scene.traverse((o) => {
      if (!o.isMesh) return;
      // GLTFLoader sanitises node names ("Cyst / IRF" -> "Cyst__IRF")
      const clean = (n) => THREE.PropertyBinding.sanitizeNodeName(n || "");
      const layer = (meta.layers || []).find((l) => l.name === o.name || clean(l.name) === o.name) || {};
      const isTissue = layer.kind === "tissue" || o.material.transparent;
      const col = new THREE.Color(layer.color || "#999999");
      o.material = isTissue
        ? new THREE.MeshStandardMaterial({ color: col, transparent: true, opacity: Math.max(0.12, layer.opacity ?? 0.18),
            roughness: 0.6, depthWrite: false, side: THREE.DoubleSide })
        : new THREE.MeshStandardMaterial({ color: col, roughness: 0.35, metalness: 0,
            emissive: col.clone().multiplyScalar(0.18), side: THREE.DoubleSide,
            // drawn after the ink slices (which write no depth) but still
            // depth-tested against itself: fluid shows through the tissue
            transparent: true, opacity: 1, depthWrite: true });
      if (!isTissue) o.renderOrder = 10;
      (isTissue ? tissue : fluids).push(o);
    });
    content.add(gltf.scene);
  }

  let stacks = null;
  if (meta.volume) {
    const buf = new Uint8Array(await (await fetch(`${base}/${meta.volume.file}`)).arrayBuffer());
    stacks = buildSlices(meta.volume, buf);
    content.add(stacks.group);
    state.bscan = Math.floor(meta.volume.dims[2] / 2);
  }

  // fit: largest horizontal extent -> 1 unit, sit on the floor (y = 0)
  const [sx, sy, sz] = meta.size;
  const s = 1 / Math.max(sx, sz);
  content.scale.setScalar(s);
  const hover = 0.08;
  spinner.position.y = hover;
  content.position.y = 0;           // exporter already put the bottom at y = 0
  root.userData.height = sy * s + hover;

  // soft contact shadow on the floor (HLD guideline: floor only)
  const shadow = contactShadow(Math.hypot(sx, sz) * s * 0.62);
  root.add(shadow);

  return { name, root, spinner, content, meta, stacks, tissue, fluids, shadow };
}

// ------------------------------------------------------------------ slices
// Three stacks of textured planes (one per axis); the one most facing the
// camera is shown. Texture alpha = OCT signal, colour = dark ink.
function buildSlices(vol, data) {
  const [nx, ny, nz] = vol.dims, sp = vol.spacing, org = vol.origin;
  const dims = [nx, ny, nz];
  const group = new THREE.Group();
  const stacks = [[], [], []];
  const textures = [[], [], []];
  const lut = new Uint8Array(256);
  const makeLut = () => {
    const thr = Math.min(0.95, vol.threshold + state.threshold);
    for (let i = 0; i < 256; i++) {
      const t = Math.min(1, Math.max(0, (i / 255 - thr) / (1 - thr)));
      lut[i] = Math.round(Math.pow(t, state.gamma) * 255);
    }
  };
  makeLut();
  const at = (x, y, z) => data[x + nx * (y + ny * z)];

  for (let axis = 0; axis < 3; axis++) {
    const ua = axis === 0 ? 2 : 0, va = axis === 1 ? 2 : 1;
    const w = dims[ua], h = dims[va];
    const geo = new THREE.PlaneGeometry(w * sp[ua], h * sp[va]);
    if (axis === 0) geo.rotateY(-Math.PI / 2);   // u -> +Z, v -> +Y
    if (axis === 1) geo.rotateX(Math.PI / 2);    // u -> +X, v -> +Z
    const center = [0, 1, 2].map((k) => org[k] + (dims[k] - 1) * sp[k] / 2);
    for (let s = 0; s < dims[axis]; s++) {
      const px = new Uint8Array(w * h * 4);
      const idx = [0, 0, 0]; idx[axis] = s;
      for (let y = 0; y < h; y++) {
        idx[va] = y;
        for (let x = 0; x < w; x++) {
          idx[ua] = x;
          const o = (y * w + x) * 4;
          px[o] = px[o + 1] = px[o + 2] = 255;
          px[o + 3] = at(idx[0], idx[1], idx[2]);   // raw value, LUT applied below
        }
      }
      const tex = new THREE.DataTexture(px, w, h, THREE.RGBAFormat);
      tex.userData.raw = px.filter((_, i) => i % 4 === 3);
      tex.magFilter = tex.minFilter = THREE.LinearFilter;
      const mat = new THREE.MeshBasicMaterial({ map: tex, color: state.ink, transparent: true,
        depthWrite: false, side: THREE.DoubleSide });
      const m = new THREE.Mesh(geo, mat);
      const pos = center.slice(); pos[axis] = org[axis] + s * sp[axis];
      m.position.set(pos[0], pos[1], pos[2]);
      m.visible = false;
      group.add(m);
      stacks[axis].push(m);
      textures[axis].push(tex);
    }
  }
  const applyLut = () => {
    makeLut();
    for (const list of textures) for (const tex of list) {
      const raw = tex.userData.raw, d = tex.image.data;
      for (let i = 0; i < raw.length; i++) d[i * 4 + 3] = lut[raw[i]];
      tex.needsUpdate = true;
    }
  };
  applyLut();
  return { group, stacks, sp, applyLut, shownAxis: -1 };
}

const _v = new THREE.Vector3(), _q = new THREE.Quaternion();
function updateSlices(c) {
  const st = c.stacks; if (!st) return;
  let axis = -1;
  if (state.mode === "volume") {
    // camera direction in the volume's own space
    camera.getWorldDirection(_v);
    c.content.getWorldQuaternion(_q);
    _v.applyQuaternion(_q.invert());
    const a = [Math.abs(_v.x), Math.abs(_v.y), Math.abs(_v.z)];
    axis = a[0] > a[1] && a[0] > a[2] ? 0 : a[1] > a[2] ? 1 : 2;
  } else if (state.mode === "bscan") axis = 2;

  st.stacks.forEach((list, a) => {
    const rel = st.sp[a] / st.sp[2];
    const alpha = state.mode === "bscan" ? 0.92 : 1 - Math.pow(1 - state.density, rel);
    list.forEach((m, s) => {
      m.visible = a === axis && (state.mode !== "bscan" || s === state.bscan);
      m.material.opacity = alpha;
      m.material.color.copy(state.ink);
    });
  });
}

function contactShadow(size) {
  const cv = document.createElement("canvas");
  cv.width = cv.height = 256;
  const g = cv.getContext("2d");
  const grd = g.createRadialGradient(128, 128, 0, 128, 128, 128);
  grd.addColorStop(0, "rgba(0,0,0,0.38)");
  grd.addColorStop(0.5, "rgba(0,0,0,0.14)");
  grd.addColorStop(1, "rgba(0,0,0,0)");
  g.fillStyle = grd; g.fillRect(0, 0, 256, 256);
  const m = new THREE.Mesh(new THREE.PlaneGeometry(size, size * 0.8),
    new THREE.MeshBasicMaterial({ map: new THREE.CanvasTexture(cv), transparent: true, depthWrite: false }));
  m.rotation.x = -Math.PI / 2;
  m.renderOrder = -1;
  return m;
}

// ------------------------------------------------------------------ scene switching
async function show(i) {
  state.index = (i + state.scans.length) % state.scans.length;
  const name = state.scans[state.index];
  setStatus(`loading ${name}…`);
  const next = await loadScan(name);
  if (current) {
    scene.remove(current.root);
    current.root.traverse((o) => { o.geometry?.dispose(); o.material?.map?.dispose(); o.material?.dispose(); });
  }
  current = next;
  scene.add(current.root);
  current.spinner.rotation.set(0, THREE.MathUtils.degToRad(30), 0);
  frame();
  applyLayers();
  renderInfo();
}

function frame() {
  // object in the lower-middle of the portrait frame (alcove safe area),
  // starting pitch 15 deg like GesturePlayer
  const h = current.root.userData.height;
  const target = new THREE.Vector3(0, h * 0.5 + 0.12, 0);
  const hfov = 2 * Math.atan(Math.tan(THREE.MathUtils.degToRad(camera.fov / 2)) * camera.aspect);
  const dist = (1.55 / 2) / Math.tan(hfov / 2);
  const pitch = THREE.MathUtils.degToRad(15);
  camera.position.set(0, target.y + Math.sin(pitch) * dist, Math.cos(pitch) * dist);
  controls.target.copy(target);
  controls.update();
}

function applyLayers() {
  if (!current) return;
  current.tissue.forEach((m) => (m.visible = state.showTissue));
  current.fluids.forEach((m) => (m.visible = state.showFluids));
  updateSlices(current);
  setStatus();
}

// ------------------------------------------------------------------ overlay
function esc(s) { return String(s ?? "").replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c])); }

// keep only the caveats from the source subtitle (its colour key describes
// the Plotly look, not this one)
function fineprint(m) {
  const keep = (m.subtitle || "").split("·").map((t) => t.trim())
    .filter((t) => /spacing|depth/i.test(t));
  return [...keep, "segmentation approximate, not validated"].join(" · ");
}

function renderInfo() {
  const m = current.meta;
  const notes = (m.notes || []).join("\n");
  const amount = (layer) => {
    const line = notes.split("\n").find((l) => l.includes(layer + ":")) || "";
    return line.split(":").slice(1).join(":").trim();
  };
  const total = notes.split("\n").find((l) => l.includes("TOTAL")) || "";
  const rows = (m.layers || []).filter((l) => l.kind === "fluid").map((l) =>
    `<div class="row"><span class="sw" style="background:${esc(l.color)}"></span>${esc(l.name)}<span style="margin-left:auto">${esc(amount(l.name))}</span></div>`).join("");
  $("info").innerHTML = `<h1>${esc(m.title)}</h1>${rows}${total ? `<div class="total">${esc(total)}</div>` : ""}
    <div class="fine">${esc(fineprint(m))}</div>`;
  $("help").textContent = [
    "drag      turn / tilt", "wheel     zoom", "← →       switch scan",
    "T         tissue shell", "F         fluids", "V         volume / B-scan / off",
    ", .       prev / next B-scan", "K L       ink lighter / darker", "[ ]       noise threshold",
    "R         auto-spin", "D         depth sway", "M         alcove simulation",
    "I         info", "H         this help",
  ].join("\n");
}

function setStatus(text) {
  if (text) { $("status").textContent = text; return; }
  if (!current) return;
  const v = current.meta.volume;
  const mode = state.mode === "bscan" ? `B-scan ${state.bscan + 1} / ${v?.dims[2]}`
             : state.mode === "volume" ? "volume" : "volume off";
  $("status").textContent = `${current.name}  ·  ${mode}  ·  ${state.index + 1}/${state.scans.length}  ·  H for keys`;
}

// ------------------------------------------------------------------ input
const actions = {
  ArrowRight: () => show(state.index + 1),
  ArrowLeft: () => show(state.index - 1),
  t: () => { state.showTissue = !state.showTissue; applyLayers(); },
  f: () => { state.showFluids = !state.showFluids; applyLayers(); },
  v: () => { state.mode = { volume: "bscan", bscan: "off", off: "volume" }[state.mode]; applyLayers(); },
  ",": () => stepBScan(-1),
  ".": () => stepBScan(1),
  k: () => { state.density = Math.max(0.03, state.density / 1.25); applyLayers(); },
  l: () => { state.density = Math.min(1, state.density * 1.25); applyLayers(); },
  "[": () => { state.threshold = Math.max(-0.1, state.threshold - 0.03); current?.stacks?.applyLut(); },
  "]": () => { state.threshold = Math.min(0.6, state.threshold + 0.03); current?.stacks?.applyLut(); },
  r: () => (state.spin = !state.spin),
  d: () => (state.sway = !state.sway),
  m: () => document.body.classList.toggle("sim"),
  i: () => document.body.classList.toggle("hide-info"),
  h: () => document.body.classList.toggle("help"),
};
function stepBScan(d) {
  if (!current?.meta.volume) return;
  state.mode = "bscan";
  state.bscan = Math.min(current.meta.volume.dims[2] - 1, Math.max(0, state.bscan + d));
  applyLayers();
}
addEventListener("keydown", (e) => {
  const fn = actions[e.key.length === 1 ? e.key.toLowerCase() : e.key];
  if (fn) { fn(); e.preventDefault(); }
});
controls.addEventListener("start", () => (state.spin = false));

// ------------------------------------------------------------------ loop
const clock = new THREE.Clock();
renderer.setAnimationLoop(() => {
  const dt = Math.min(clock.getDelta(), 0.05), t = clock.elapsedTime;
  if (current) {
    if (state.spin) current.spinner.rotation.y += dt * 0.35;
    // depth sway: small figure-8 of the whole object, adds motion parallax cues
    current.root.rotation.set(state.sway ? Math.sin(t * 0.9) * 0.05 : 0,
                              state.sway ? Math.sin(t * 0.45) * 0.12 : 0, 0);
    updateSlices(current);
  }
  controls.update();
  renderer.render(scene, camera);
});

// ------------------------------------------------------------------ go
(async () => {
  try {
    resize();
    await loadIndex();
    await show(state.index);
  } catch (err) {
    console.error(err);
    const e = $("error");
    e.style.display = "grid";
    e.textContent = location.protocol === "file:"
      ? "Open this through a local web server (run ./serve.sh), not as a file."
      : String(err.message || err);
  }
})();
