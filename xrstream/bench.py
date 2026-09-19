"""Headless benchmark: replay camera paths under every policy and bandwidth.

    python -m xrstream.bench                          # every scenario
    python -m xrstream.bench --scenario gym           # just the PE class
    python -m xrstream.bench --link step --loss 0.05  # congested, lossy link
    python -m xrstream.bench --lesson all             # replay the lesson plans

Produces
    <out>/ticks.csv     per-frame trace for every run
    <out>/summary.csv   one row per run
    <out>/summary.md    the tables to paste into a report
    <out>/*.png         plots (only if matplotlib is installed)
"""
from __future__ import annotations

import argparse
import csv
import json
import pathlib
import time

from . import lesson as lessons, paths, policies, scenarios
from .link import Link, PROFILES
from .session import StreamSession

DEFAULT_POLICIES = ["full", "lod", "roi", "adaptive_geom", "adaptive"]
LESSON_POLICIES = ["full", "adaptive_geom", "adaptive", "lesson_ctx",
                   "lesson_ctx_occ", "lesson_aware"]
DEFAULT_BANDWIDTHS = [3.0, 10.0, 25.0, 50.0]

TICK_FIELDS = ["run", "scenario", "policy", "bandwidth_mbps", "path", "t", "pose",
               "bytes", "cum_bytes", "mbps", "capacity_mbps", "visible", "missing",
               "coverage", "penalty", "qoe", "staleness_ms", "static_resident",
               "queue"]

SUMMARY_FIELDS = ["run", "scenario", "path", "policy", "bandwidth_mbps", "link",
                  "total_mb", "wasted_mb", "lost_mb", "mean_qoe", "p05_qoe",
                  "mean_coverage", "mean_staleness_ms", "instructor_fps",
                  "student_fps_mean", "t_qoe_90", "quantise", "deflate",
                  "decide_ms_per_tick", "ticks", "duration_s"]

LESSON_FIELDS = ["run", "lesson", "scenario", "policy", "bandwidth_mbps", "link",
                 "mean_qoe", "p05_qoe", "focus_quality", "focus_quality_p05",
                 "clear_view", "clear_view_p05", "time_to_detail_s",
                 "ready_on_arrival", "steps_never_ready", "mean_staleness_ms",
                 "instructor_fps", "student_fps_mean", "total_mb", "wasted_mb"]


def run_one(scene, policy: str, bandwidth: float, path_keys, fps: float,
            quantise: bool, deflate: bool, link_kw: dict | None = None):
    link = Link(mbps=bandwidth, **(link_kw or {}))
    sess = StreamSession(scene, policy=policy, quantise=quantise,
                         deflate=deflate, link=link)
    t0 = time.perf_counter()
    for _t, dt, cam in paths.frames(path_keys, fps=fps):
        sess.tick(cam, dt)
    wall = time.perf_counter() - t0
    summary = sess.summary()
    summary["decide_ms_per_tick"] = round(wall * 1000 / max(1, len(sess.history)), 3)
    summary["participant_fps"] = sess.participant_fps()
    return summary, sess.history


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--scenario", nargs="+", default=list(scenarios.NAMES),
                    choices=list(scenarios.NAMES))
    ap.add_argument("--policies", nargs="+", default=None,
                    choices=sorted(policies.REGISTRY))
    ap.add_argument("--lesson", nargs="+", default=None,
                    help="replay lesson plans instead of scenario paths "
                         "(names from lessons/, or 'all')")
    ap.add_argument("--bandwidth", nargs="+", type=float, default=DEFAULT_BANDWIDTHS,
                    help="link capacities in Mbit/s")
    ap.add_argument("--path", nargs="+", default=None,
                    help="camera paths to replay (default: all of each scenario's)")
    ap.add_argument("--fps", type=float, default=30.0, help="client update rate")
    ap.add_argument("--link", default="constant", choices=PROFILES,
                    help="how capacity varies over time")
    ap.add_argument("--loss", type=float, default=0.0,
                    help="probability a completed transfer is lost and re-sent")
    ap.add_argument("--link-period", type=float, default=6.0)
    ap.add_argument("--link-depth", type=float, default=0.25,
                    help="congested capacity as a fraction of nominal")
    ap.add_argument("--trace", default=None, help="CSV with t,mbps for --link trace")
    ap.add_argument("--no-quantise", action="store_true")
    ap.add_argument("--no-deflate", action="store_true")
    ap.add_argument("--out", default="results")
    args = ap.parse_args(argv)

    quantise, deflate = not args.no_quantise, not args.no_deflate
    link_kw = {"profile": args.link, "loss": args.loss, "period": args.link_period,
               "depth": args.link_depth, "trace": args.trace}
    out = pathlib.Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    print(f"link: {args.link} profile, loss={args.loss:.1%}, "
          f"codec quantise={quantise} deflate={deflate}\n", flush=True)

    if args.lesson:
        return _run_lessons(args, link_kw, quantise, deflate, out)
    args.policies = args.policies or DEFAULT_POLICIES

    summaries, tick_rows, scene_stats = [], [], []
    for scen_name in args.scenario:
        sc = scenarios.get(scen_name)
        t0 = time.perf_counter()
        scene = sc.build()
        stats = scene.stats(quantise, deflate)
        scene_stats.append(stats)
        print(f"== {sc.title} ({scen_name}) -- built in {time.perf_counter()-t0:.1f}s")
        print(f"   {stats['objects']} objects "
              f"({stats['static_objects']} static, {stats['dynamic_objects']} live) | "
              f"{stats['triangles_l0']:,} triangles | {stats['points_l0']:,} points")
        print(f"   static {stats['static_bytes']/1e6:.2f} MB one-off | "
              f"live capture {stats['full_stream_mbps']:.0f} Mbit/s at "
              f"{stats['volumetric_hz']:g} Hz", flush=True)

        for pth in (args.path or list(sc.paths)):
            if pth not in sc.paths:
                continue
            for bw in args.bandwidth:
                for pol in args.policies:
                    run = f"{scen_name}|{pth}|{pol}|{bw:g}"
                    summary, hist = run_one(scene, pol, bw, sc.paths[pth], args.fps,
                                            quantise, deflate, link_kw)
                    summary.update(run=run, scenario=scen_name, path=pth)
                    summaries.append(summary)
                    tick_rows.extend({"run": run, "scenario": scen_name,
                                      "policy": pol, "bandwidth_mbps": bw,
                                      "path": pth, **row} for row in hist)
                    print(f"   {pth:16s} {pol:14s} {bw:5g} Mbit/s | "
                          f"QoE {summary['mean_qoe']:.3f} "
                          f"(p05 {summary['p05_qoe']:.3f}) | "
                          f"instr {summary['instructor_fps']:5.2f} fps | "
                          f"others {summary['student_fps_mean']:5.2f} fps | "
                          f"stale {summary['mean_staleness_ms']:7.1f} ms | "
                          f"{summary['total_mb']:6.2f} MB", flush=True)
        print(flush=True)

    _write_csv(out / "ticks.csv", TICK_FIELDS, tick_rows)
    _write_csv(out / "summary.csv", SUMMARY_FIELDS, summaries)
    (out / "summary.json").write_text(
        json.dumps({"scenes": scene_stats, "runs": summaries}, indent=2),
        encoding="utf-8")
    md = _markdown(summaries, scene_stats)
    (out / "summary.md").write_text(md, encoding="utf-8")
    print(md)

    _plot(out, tick_rows)
    print(f"wrote {out.resolve()}")
    return 0


def _run_lessons(args, link_kw, quantise, deflate, out) -> int:
    names = (sorted(lessons.load_all()) if args.lesson == ["all"] else args.lesson)
    pols = args.policies or LESSON_POLICIES
    scenes, rows, ticks = {}, [], []
    for name in names:
        les = lessons.get(name)
        if les.scenario not in scenes:
            scenes[les.scenario] = scenarios.build(les.scenario)
        scene = scenes[les.scenario]
        les.bind(scene)
        keys = les.camera_keys()
        print(f"== {les.title} ({name}, {les.scenario}) -- {len(les.steps)} steps, "
              f"{les.duration:g}s", flush=True)
        for bw in args.bandwidth:
            for pol in pols:
                les_run = lessons.get(name).bind(scene)
                sess = StreamSession(scene, policy=pol, quantise=quantise,
                                     deflate=deflate, lesson=les_run,
                                     link=Link(mbps=bw, **link_kw))
                for _t, dt, cam in paths.frames(keys, fps=args.fps):
                    sess.tick(cam, dt)
                r = sess.summary()
                run = f"{name}|{pol}|{bw:g}"
                r.update(run=run, lesson=name, scenario=les.scenario)
                rows.append(r)
                ticks.extend({"run": run, "lesson": name, "policy": pol,
                              "bandwidth_mbps": bw, **row} for row in sess.history)
                print(f"   {pol:14s} {bw:5g} Mbit/s | QoE {r['mean_qoe']:.3f} | "
                      f"focus {r['focus_quality']:.3f} | view {r['clear_view']:.3f} "
                      f"(p05 {r['clear_view_p05']:.3f}) | detail in "
                      f"{r['time_to_detail_s']:5.2f}s | stale "
                      f"{r['mean_staleness_ms']:7.1f} ms", flush=True)
        print(flush=True)

    _write_csv(out / "lessons.csv", LESSON_FIELDS, rows)
    _write_csv(out / "lesson_ticks.csv",
               ["run", "lesson", "policy", "bandwidth_mbps", "t", "step", "qoe",
                "focus_q", "clear_view", "staleness_ms", "mbps"], ticks)
    md = _lesson_markdown(rows)
    (out / "lessons.md").write_text(md, encoding="utf-8")
    print(md)
    _plot_lessons(out, ticks)
    print(f"wrote {out.resolve()}")
    return 0


def _lesson_markdown(rows) -> str:
    cols = ["policy", "bandwidth_mbps", "mean_qoe", "focus_quality",
            "focus_quality_p05", "clear_view", "clear_view_p05",
            "time_to_detail_s", "ready_on_arrival", "mean_staleness_ms",
            "instructor_fps"]
    names = ["policy", "Mbit/s", "QoE", "focus", "focus p05", "clear view",
             "view p05", "detail in (s)", "ready on arrival", "stale ms",
             "instr fps"]
    lines = []
    for les in dict.fromkeys(r["lesson"] for r in rows):
        lines += ["", f"## {les}", "",
                  "| " + " | ".join(names) + " |",
                  "|" + "|".join(["---"] * len(names)) + "|"]
        for r in rows:
            if r["lesson"] == les:
                lines.append("| " + " | ".join(str(r[c]) for c in cols) + " |")
    return "\n".join(lines).lstrip() + "\n"


def _plot_lessons(out, ticks):
    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
    except Exception:
        return
    combos = sorted({(r["lesson"], r["bandwidth_mbps"]) for r in ticks})
    for les, bw in combos:
        fig, axes = plt.subplots(2, 1, figsize=(9, 6), sharex=True)
        for pol in dict.fromkeys(r["policy"] for r in ticks):
            rows = [r for r in ticks if r["lesson"] == les
                    and r["bandwidth_mbps"] == bw and r["policy"] == pol]
            if not rows:
                continue
            t = [r["t"] for r in rows]
            axes[0].plot(t, [r["focus_q"] if r["focus_q"] is not None else float("nan")
                             for r in rows], label=pol, lw=1.3)
            axes[1].plot(t, [r["clear_view"] if r["clear_view"] is not None
                             else float("nan") for r in rows], label=pol, lw=1.3)
        # mark where each lesson step starts
        steps = sorted({(r["step"], r["t"]) for r in ticks
                        if r["lesson"] == les and r["bandwidth_mbps"] == bw},
                       key=lambda x: x[1])
        seen = set()
        for step, t in steps:
            if step not in seen:
                seen.add(step)
                for a in axes:
                    a.axvline(t, color="0.75", lw=0.8, ls="--")
        axes[0].set_ylabel("quality of what\nis being taught")
        axes[0].set_title(f"lesson: {les} @ {bw:g} Mbit/s (dashed = new step)")
        axes[0].legend(fontsize=7, ncol=3)
        axes[1].set_ylabel("clear view\n(incl. who is in the way)")
        axes[1].set_xlabel("time (s)")
        for a in axes:
            a.set_ylim(-0.02, 1.02)
            a.grid(alpha=0.3)
        fig.tight_layout()
        fig.savefig(out / f"lesson_{les}_{bw:g}mbps.png", dpi=130)
        plt.close(fig)


def _write_csv(path, fields, rows):
    with open(path, "w", newline="", encoding="utf-8") as fh:
        w = csv.DictWriter(fh, fieldnames=fields, extrasaction="ignore")
        w.writeheader()
        w.writerows(rows)


def _markdown(summaries, scene_stats) -> str:
    lines = ["## Scenarios", "",
             "| scenario | objects | static / live | triangles | points | "
             "static MB | live capture |",
             "|---|---|---|---|---|---|---|"]
    for s in scene_stats:
        lines.append(f"| {s['title']} | {s['objects']} | "
                     f"{s['static_objects']} / {s['dynamic_objects']} | "
                     f"{s['triangles_l0']:,} | {s['points_l0']:,} | "
                     f"{s['static_bytes']/1e6:.2f} | "
                     f"{s['full_stream_mbps']:.0f} Mbit/s |")

    cols = ["path", "policy", "link", "mean_qoe", "p05_qoe", "instructor_fps",
            "student_fps_mean", "mean_staleness_ms", "total_mb", "wasted_mb"]
    names = ["path", "policy", "link", "QoE", "QoE p05", "instr fps",
             "others fps", "stale ms", "sent MB", "wasted MB"]
    for scen in dict.fromkeys(s["scenario"] for s in summaries):
        title = next((st["title"] for st in scene_stats
                      if st["scenario"] == scen), scen)
        lines += ["", f"## {title}", "",
                  "| " + " | ".join(names) + " |",
                  "|" + "|".join(["---"] * len(names)) + "|"]
        for s in summaries:
            if s["scenario"] == scen:
                lines.append("| " + " | ".join(str(s[c]) for c in cols) + " |")
    return "\n".join(lines) + "\n"


def _plot(out, tick_rows):
    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
    except Exception:
        print("(matplotlib not installed -- skipping plots)")
        return

    combos = sorted({(r["scenario"], r["path"], r["bandwidth_mbps"])
                     for r in tick_rows})
    for scen, path, bw in combos:
        fig, axes = plt.subplots(3, 1, figsize=(9, 8), sharex=True)
        for pol in dict.fromkeys(r["policy"] for r in tick_rows):
            rows = [r for r in tick_rows
                    if r["scenario"] == scen and r["path"] == path
                    and r["bandwidth_mbps"] == bw and r["policy"] == pol]
            if not rows:
                continue
            t = [r["t"] for r in rows]
            axes[0].plot(t, [r["qoe"] for r in rows], label=pol, lw=1.4)
            axes[1].plot(t, [r["staleness_ms"] for r in rows], label=pol, lw=1.4)
            axes[2].plot(t, [r["cum_bytes"] / 1e6 for r in rows], label=pol, lw=1.4)
        axes[0].set_ylabel("quality of experience\n(1 = ideal)")
        axes[0].set_ylim(-0.02, 1.02)
        axes[0].set_title(f"{scen} | {path} @ {bw:g} Mbit/s")
        axes[0].legend(fontsize=8, ncol=5)
        axes[1].set_ylabel("mean participant\nstaleness (ms)")
        axes[2].set_ylabel("cumulative MB sent")
        axes[2].set_xlabel("time (s)")
        for a in axes:
            a.grid(alpha=0.3)
        fig.tight_layout()
        fig.savefig(out / f"{scen}_{path}_{bw:g}mbps.png", dpi=130)
        plt.close(fig)
    print(f"({len(combos)} plots written to {out})")


if __name__ == "__main__":
    raise SystemExit(main())
