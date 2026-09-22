"""The experiment: a class on one cell, four schedulers.

Three sweeps, each answering one question a reader will ask.

  size        how far does a cell stretch? Quality against class size, at a
              fixed link -- the curve where unicast falls away and sharing
              does not.
  bandwidth   what does a hub need? Quality against link capacity, at a fixed
              class, which is the number a deployment has to buy.
  attention   what if the class does not follow the plan? Quality against the
              share of students who do, which is the honest test of reading a
              lesson plan at all.

Every figure reports the worst student as well as the mean, because a cell
that serves nineteen children well and one badly has not served a class.

    python -m xrstream.bench_cell                     # all three, into results/cell
    python -m xrstream.bench_cell --sweep size        # just one
    python -m xrstream.bench_cell --lesson titration --seconds 20
"""
from __future__ import annotations

import argparse
import csv
import json
import pathlib
import time

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

from . import cell

OUT = pathlib.Path("results/cell")

#: the sweeps, and what varies in each
SWEEPS = {
    "size": ("students", (1, 2, 4, 8, 16, 24, 32)),
    "bandwidth": ("mbps", (5, 10, 15, 25, 50, 100)),
    "attention": ("attention", (1.0, 0.75, 0.5, 0.25, 0.0)),
}

LABELS = {"unicast": "a stream each", "fair": "a stream each, leftovers shared",
          "shared": "sent once for everyone waiting",
          "hub": "sent once, priced per byte (ours)"}


def sweep(name: str, lesson: str, seconds: float, base: dict) -> list[dict]:
    field, values = SWEEPS[name]
    rows = []
    for value in values:
        for mode in cell.MODES:
            kw = dict(base, lesson=lesson, seconds=seconds, mode=mode)
            kw[field] = value
            started = time.perf_counter()
            res = cell.run(**kw)
            row = res.row()
            row["sweep"] = name
            row["seconds"] = seconds
            row["took_s"] = round(time.perf_counter() - started, 2)
            rows.append(row)
            print(f"  {name:9} {field}={value:<6} {mode:10} "
                  f"mean={row['mean_qoe']:.3f} worst={row['worst_qoe']:.3f} "
                  f"{row['cell_mbytes']:7.1f} MB  x{row['sharing_gain']:.1f}")
    return rows


def chart(rows: list[dict], name: str, out: pathlib.Path) -> None:
    field, values = SWEEPS[name]
    fig, axes = plt.subplots(1, 3, figsize=(15, 4.2), sharex=True)
    for mode in cell.MODES:
        got = [r for r in rows if r["mode"] == mode]
        xs = [r[field] for r in got]
        axes[0].plot(xs, [r["mean_qoe"] for r in got], marker="o",
                     label=LABELS.get(mode, mode))
        axes[1].plot(xs, [r["worst_qoe"] for r in got], marker="o",
                     label=LABELS.get(mode, mode))
        axes[2].plot(xs, [r["cell_mbytes"] for r in got], marker="o",
                     label=LABELS.get(mode, mode))
    titles = {"size": "students in the class", "bandwidth": "cell capacity (Mbit/s)",
              "attention": "share of the class following the lesson"}
    for ax, what in zip(axes, ("the average student", "the worst-served student",
                               "what the cell carried")):
        ax.set_xlabel(titles[name])
        ax.set_title(what)
        ax.grid(alpha=0.3)
    for ax in axes[:2]:
        ax.set_ylabel("picture quality (0-1)")
        ax.set_ylim(0, 1)
    axes[2].set_ylabel("megabytes on the air")
    axes[2].set_yscale("log")
    axes[0].legend(fontsize=8)
    fig.tight_layout()
    fig.savefig(out, dpi=140)
    plt.close(fig)


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--sweep", choices=(*SWEEPS, "all"), default="all")
    ap.add_argument("--lesson", default="titration")
    ap.add_argument("--seconds", type=float, default=15.0)
    ap.add_argument("--students", type=int, default=16)
    ap.add_argument("--mbps", type=float, default=25.0)
    ap.add_argument("--attention", type=float, default=0.75)
    ap.add_argument("--out", default=str(OUT))
    ap.add_argument("--replot", action="store_true",
                    help="redraw the charts from the last run, without running it")
    args = ap.parse_args()

    out = pathlib.Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    base = {"students": args.students, "mbps": args.mbps, "attention": args.attention}
    names = list(SWEEPS) if args.sweep == "all" else [args.sweep]

    if args.replot:
        saved = json.loads((out / "cell.json").read_text(encoding="utf-8"))
        for name in SWEEPS:
            rows = [r for r in saved["rows"] if r["sweep"] == name]
            if rows:
                chart(rows, name, out / f"cell_{name}.png")
        print(f"charts redrawn in {out}")
        return

    every: list[dict] = []
    for name in names:
        print(f"\n{name}: {args.lesson}, {args.seconds:g}s per run, "
              f"{len(SWEEPS[name][1]) * len(cell.MODES)} runs")
        rows = sweep(name, args.lesson, args.seconds, base)
        chart(rows, name, out / f"cell_{name}.png")
        every.extend(rows)

    with (out / "cell.csv").open("w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=list(every[0]))
        w.writeheader()
        w.writerows(every)
    (out / "cell.json").write_text(json.dumps(
        {"lesson": args.lesson, "seconds": args.seconds, "base": base, "rows": every},
        indent=1), encoding="utf-8")
    print(f"\nwritten to {out}")


if __name__ == "__main__":
    main()
