"""Bottleneck link model: time-varying capacity and packet loss.

A constant, lossless link flatters every streaming policy -- the interesting
question is what happens when capacity collapses mid-lecture or an update is
lost and has to be re-sent.  SOTA 1.5.7 and 14.5 both put adaptation to
*changing* network conditions at the centre of the problem.

    link = Link(mbps=25, profile="step")      # alternating good / congested
    link.bytes_per_second(t)                  # capacity right now
    link.lost()                               # did this transfer survive?
"""
from __future__ import annotations

import csv
import math
import pathlib

import numpy as np

PROFILES = ("constant", "sine", "step", "trace")


class Link:
    def __init__(self, mbps: float = 25.0, profile: str = "constant",
                 loss: float = 0.0, seed: int = 0, period: float = 6.0,
                 depth: float = 0.25, trace: str | None = None):
        if profile not in PROFILES:
            raise ValueError(f"unknown profile {profile!r}; have {PROFILES}")
        self.mbps = float(mbps)
        self.profile = profile
        self.loss = float(loss)
        self.period = float(period)
        self.depth = float(depth)
        self.rng = np.random.default_rng(seed)
        self.samples = self._load(trace) if profile == "trace" else None

    # -- capacity ---------------------------------------------------------
    def capacity_mbps(self, t: float) -> float:
        if self.profile == "constant":
            return self.mbps
        if self.profile == "sine":
            # smooth breathing between depth*mbps and (2-depth)*mbps
            swing = 1.0 - self.depth
            return self.mbps * (1.0 + swing * math.sin(2 * math.pi * t / self.period))
        if self.profile == "step":
            # alternating healthy / congested epochs
            return self.mbps if int(t / self.period) % 2 == 0 else self.mbps * self.depth
        ts, vs = self.samples
        return float(np.interp(t, ts, vs))

    def bytes_per_second(self, t: float) -> float:
        return max(0.0, self.capacity_mbps(t)) * 1e6 / 8.0

    # -- loss -------------------------------------------------------------
    def lost(self) -> bool:
        """True when a completed transfer must be discarded and re-sent."""
        return self.loss > 0.0 and bool(self.rng.random() < self.loss)

    # -- misc -------------------------------------------------------------
    @staticmethod
    def _load(path: str | None):
        if not path:
            raise ValueError("profile='trace' needs a trace file")
        ts, vs = [], []
        with open(pathlib.Path(path), newline="", encoding="utf-8") as fh:
            for row in csv.DictReader(fh):
                ts.append(float(row["t"]))
                vs.append(float(row["mbps"]))
        if not ts:
            raise ValueError(f"empty trace {path!r}")
        return np.asarray(ts), np.asarray(vs)

    def describe(self) -> str:
        base = f"{self.profile}@{self.mbps:g}Mbit/s"
        if self.profile in ("sine", "step"):
            base += f" period={self.period:g}s depth={self.depth:g}"
        if self.loss:
            base += f" loss={self.loss:.1%}"
        return base

    def __repr__(self) -> str:
        return f"Link({self.describe()})"
