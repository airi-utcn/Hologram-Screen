"""Live demo server: streams a holographic lesson to a browser over WebSocket.

    python -m xrstream.server            # then open http://localhost:8000

Scenes are built lazily and cached, so the first switch to a scenario takes a
moment and every later one is instant.

Protocol (client -> server, JSON text frames)
    {"type": "config", "scenario": "gym", "policy": "adaptive",
     "bandwidth_mbps": 10, "link": "step", "loss": 0.02}
                                                   takes effect from now on; add
                                                   "restart": true to start over
    {"type": "config", "lesson": "titration"}      play a lesson ("" to stop)
    {"type": "autopilot", "on": true}              follow the lesson's camera again
    {"type": "pause", "on": true}                  freeze the lesson (and resume it)
    {"type": "seek", "t": 45.0}                    jump to that second of the lesson
    {"type": "camera", "pos": [...], "forward": [...], "fov_y_deg": 60,
     "aspect": 1.77, "screen_h": 900}

While a lesson plays on autopilot the server drives the viewpoint from the
lesson plan and tells the client where to look with {"type": "pose", ...}.
When the plan changes an object's state (the flask turns pink) the server
sends {"type": "states", "states": {oid: state}}; the client shows the new
look at once if it already holds it, otherwise when the geometry arrives.

Protocol (server -> client)
    text   {"type": "manifest", ...}                 on connect and after config
    text   {"type": "reset", "epoch": n}             drop everything and restart
    text   {"type": "metrics", ...}                  ~5 Hz
    binary [u32 header_len][json header][payload]    one per delivered chunk
"""
from __future__ import annotations

import argparse
import asyncio
import json
import math
import pathlib
import time

from fastapi import FastAPI, WebSocket, WebSocketDisconnect
from fastapi.responses import FileResponse
from fastapi.staticfiles import StaticFiles

from . import codec, lesson as lessons, paths, policies, scenarios
from .camera import Camera
from .link import Link
from .session import StreamSession

CLIENT_DIR = pathlib.Path(__file__).resolve().parent.parent / "client"
TICK_HZ = 30.0
DEFAULT_SCENARIO = "classroom"

app = FastAPI(title="UC7 holographic lesson streaming")
_scenes: dict[str, object] = {}


def get_scene(name: str):
    """Build a scenario on first use, then reuse it across sessions."""
    if name not in _scenes:
        _scenes[name] = scenarios.get(name).build()
    return _scenes[name]


def lesson_list() -> list[dict]:
    return [{"name": les.name, "title": les.title, "scenario": les.scenario,
             "summary": les.summary, "steps": len(les.steps), "banner": les.banner,
             "duration": les.duration}
            for les in lessons.load_all().values()]


def scenario_list() -> list[dict]:
    """Describe every scenario, including where the viewer should start.

    The opening keyframe of each scenario's walk is a sensible home pose --
    a sports hall needs a very different starting distance from a classroom.
    """
    out = []
    for s in scenarios.REGISTRY.values():
        eye, look = s.viewpoint()
        out.append({"name": s.name, "title": s.title, "blurb": s.blurb,
                    "paths": list(s.paths), "home": eye, "look": look})
    return out


@app.middleware("http")
async def no_stale_client(request, call_next):
    """Never let the browser reuse an old copy of the viewer.

    A cached index.html next to a fresh main.js (or the reverse) half-works in
    confusing ways -- an empty board, missing buttons -- after every update.
    """
    response = await call_next(request)
    if request.url.path == "/" or request.url.path.startswith("/client/"):
        response.headers["Cache-Control"] = "no-store"
    return response


@app.get("/")
def index():
    return FileResponse(CLIENT_DIR / "index.html")


app.mount("/client", StaticFiles(directory=CLIENT_DIR), name="client")

LOGO_TYPES = {".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg"}


@app.get("/logos")
def logos() -> list[str]:
    """Whatever images sit in client/logos, in name order.

    Read from the folder rather than written into the page, so that a logo is
    added by dropping the file in -- and so that a lab can put its own there
    without touching the viewer. Name them 1-..., 2-... to fix their order.
    """
    folder = CLIENT_DIR / "logos"
    if not folder.is_dir():
        return []
    # the file's own timestamp rides along, so a swapped logo cannot be served
    # from a browser cache that still holds the old one
    return [f"/client/logos/{f.name}?v={int(f.stat().st_mtime)}"
            for f in sorted(folder.iterdir())
            if f.suffix.lower() in LOGO_TYPES]


@app.middleware("http")
async def no_stale_client(request, call_next):
    """Never let the browser reuse an old copy of the viewer.

    A cached index.html next to a fresh main.js (or the reverse) half-works in
    confusing ways -- an empty board, missing buttons -- after every update.
    """
    response = await call_next(request)
    if request.url.path == "/" or request.url.path.startswith("/client/"):
        response.headers["Cache-Control"] = "no-store"
    return response


@app.get("/")
def index():
    return FileResponse(CLIENT_DIR / "index.html")


app.mount("/client", StaticFiles(directory=CLIENT_DIR), name="client")

LOGO_TYPES = {".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg"}


@app.websocket("/ws")
async def stream(ws: WebSocket):
    await ws.accept()
    scenario = DEFAULT_SCENARIO
    sess = StreamSession(get_scene(scenario), policy="adaptive",
                         bandwidth_mbps=10.0)
    cam = Camera(pos=[0, 1.65, 7], forward=[0, -0.02, -1])
    view = {"aspect": 16 / 9, "screen_h": 900, "fov_y_deg": 60.0}
    lesson = None          # the Lesson being played, if any
    lesson_keys = None     # its generated camera path
    autopilot = False
    paused = False         # a paused lesson's clock does not move
    # Sending geometry and handling messages are separate tasks on one socket,
    # so a room can be swapped while a batch from the old one is half sent. This
    # generation number tells both ends which scene a chunk belongs to.
    epoch = 0

    def manifest() -> str:
        m = sess.manifest()
        m["epoch"] = epoch
        m["scenario"] = scenario
        m["scenarios"] = scenario_list()
        m["lessons"] = lesson_list()
        m["lesson"] = lesson.name if lesson else ""
        m["states"] = {str(o.oid): sess.state_of(o) for o in sess.scene
                       if not o.dynamic and (o.state != "default" or o.anims)}
        return json.dumps(m)

    await ws.send_text(manifest())

    async def receive():
        nonlocal cam, sess, scenario, lesson, lesson_keys, autopilot, paused, epoch
        while True:
            msg = json.loads(await ws.receive_text())
            kind = msg.get("type")
            if kind == "camera":
                view.update(aspect=float(msg.get("aspect", view["aspect"])),
                            screen_h=int(msg.get("screen_h", view["screen_h"])),
                            fov_y_deg=float(msg.get("fov_y_deg", view["fov_y_deg"])))
                if not (lesson and autopilot):
                    cam = Camera.from_dict(msg)
            elif kind == "pause":
                paused = bool(msg.get("on"))
            elif kind == "autopilot":
                autopilot = bool(msg.get("on")) and lesson is not None
            elif (kind == "seek" and lesson is not None
                  and math.isfinite(float(msg.get("t", 0.0)))):
                # jump to a moment of the lesson (for demos): start the
                # stream over from there, with every object in its state
                sess.reset()
                sess.state.t = max(0.0, min(float(msg.get("t", 0.0)), lesson.duration))
                sess.state.context = lesson.context(sess.state.t)
                sess._apply_lesson(sess.state)
                autopilot, paused = True, False
                epoch += 1
                await ws.send_text(json.dumps({"type": "reset", "epoch": epoch}))
                await ws.send_text(manifest())
            elif kind == "config" and "lesson" in msg:
                name = msg.get("lesson") or ""
                paused = False
                if name:
                    les = lessons.get(name)
                    scenario = les.scenario
                    scene = await asyncio.to_thread(get_scene, scenario)
                    lesson = les.bind(scene)
                    lesson_keys = lesson.camera_keys()
                    autopilot = True
                    policy = msg.get("policy")
                    if policy not in policies.REGISTRY:
                        policy = sess.policy
                    sess = StreamSession(scene, policy=policy, link=sess.link, lesson=lesson)
                else:
                    lesson, lesson_keys, autopilot = None, None, False
                    sess = StreamSession(get_scene(scenario), policy=sess.policy,
                                         link=sess.link)
                epoch += 1
                await ws.send_text(manifest())
                await ws.send_text(json.dumps({"type": "reset", "epoch": epoch}))
            elif kind == "config":
                want = msg.get("scenario", scenario)
                restart = bool(msg.get("restart"))
                if want != scenario and want in scenarios.REGISTRY:
                    scenario = want
                    lesson, lesson_keys, autopilot = None, None, False
                    # building a new scene can take a moment; do it off the loop
                    await asyncio.to_thread(get_scene, scenario)
                    sess = StreamSession(get_scene(scenario),
                                         policy=sess.policy, link=sess.link)
                    restart = True
                # a new strategy or connection speed takes effect from now on:
                # the lesson carries on and the viewer keeps what it has
                policy = msg.get("policy")
                if policy not in policies.REGISTRY:     # not chosen yet, or unknown
                    policy = None
                sess.configure(policy=policy,
                               bandwidth_mbps=msg.get("bandwidth_mbps"),
                               quantise=msg.get("quantise"),
                               deflate=msg.get("deflate"), reset=restart)
                if msg.get("link") or msg.get("loss") is not None:
                    sess.link = Link(mbps=sess.link.mbps,
                                     profile=msg.get("link", sess.link.profile),
                                     loss=float(msg.get("loss", sess.link.loss)))
                if restart:
                    if lesson is not None:      # a restart replays the lesson
                        autopilot = True
                    paused = False
                    sess.reset()
                    epoch += 1
                    await ws.send_text(manifest())
                    await ws.send_text(json.dumps({"type": "reset", "epoch": epoch}))

    rx = asyncio.create_task(receive())
    last = time.perf_counter()
    last_metrics = 0.0
    try:
        while not rx.done():
            now = time.perf_counter()
            if paused:
                # nothing moves and nothing is sent; the link simply idles
                last = now
                await asyncio.sleep(1.0 / TICK_HZ)
                continue
            if lesson is not None and autopilot:
                if sess.state.t >= lesson.duration:
                    autopilot = False           # lesson over: hand back control
                else:
                    cam = paths.sample(lesson_keys, sess.state.t,
                                       aspect=view["aspect"], screen_h=view["screen_h"],
                                       fov_y_deg=view["fov_y_deg"])
                    await ws.send_text(json.dumps({
                        "type": "pose", "pos": list(map(float, cam.pos)),
                        "forward": list(map(float, cam.forward))}))
            res = sess.tick(cam, now - last)
            last = now
            if res.state_changes:
                await ws.send_text(json.dumps({
                    "type": "states",
                    "states": {str(k): v for k, v in res.state_changes.items()}}))
            sending = epoch
            for ch in res.chunks:
                if epoch != sending:
                    break          # the room changed under us: these are stale
                await ws.send_bytes(codec.frame({**ch.header, "e": epoch}, ch.body))
            if now - last_metrics > 0.2:
                last_metrics = now
                extra = {}
                ctx = sess.state.context
                if lesson is not None and ctx is not None:
                    extra["lesson"] = {**ctx.to_dict(len(lesson.steps)),
                                       "title": lesson.title,
                                       "duration": lesson.duration,
                                       "autopilot": autopilot,
                                       "hands": [o.oid for o in sess.scene.dynamic
                                                 if o.gesture == "hand_up"],
                                       "finished": sess.state.t >= lesson.duration}
                await ws.send_text(json.dumps({
                    "type": "metrics", "policy": sess.policy.name,
                    "scenario": scenario,
                    "bandwidth_mbps": sess.bandwidth_mbps,
                    "participant_fps": sess.participant_fps(),
                    **res.metrics, **extra}))
            await asyncio.sleep(max(0.0, 1.0 / TICK_HZ - (time.perf_counter() - now)))
        rx.result()
    except (WebSocketDisconnect, RuntimeError):
        pass
    finally:
        rx.cancel()


def main():
    import uvicorn
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=8000)
    ap.add_argument("--scenario", default=DEFAULT_SCENARIO,
                    choices=list(scenarios.NAMES))
    ap.add_argument("--https", action="store_true",
                    help="serve over https with a self-signed certificate, so a "
                         "headset on the network can start a WebXR session")
    args = ap.parse_args()
    globals()["DEFAULT_SCENARIO"] = args.scenario

    ssl = {}
    if args.https:
        from . import certs
        cert, key = certs.ensure_cert()
        ssl = {"ssl_certfile": str(cert), "ssl_keyfile": str(key)}
        # a headset reaches this machine by its address on the network, not by
        # localhost, so listening only on the loopback would hide the viewer
        if args.host == "127.0.0.1":
            args.host = "0.0.0.0"
        print()
        print("Open this in the headset, then press Enter VR:")
        for ip in certs.lan_addresses():
            print(f"    https://{ip}:{args.port}")
        print("The certificate is self-signed, so the browser warns once: "
              "Advanced -> Proceed.")
        print()

    uvicorn.run(app, host=args.host, port=args.port, log_level="info", **ssl)


if __name__ == "__main__":
    main()
