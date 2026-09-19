"""Payload encoding for streamed geometry.

Two compression axes, both reported in the benchmark (cf. SOTA 1.4.4 / 1.5.4):

  quantise  : float32 positions -> uint16 in the object's local bounding box
              (14 bytes/vertex -> 6 bytes/vertex for positions)
  deflate   : zlib on top of the packed buffer (entropy coding)

The wire format is deliberately simple and mirrored 1:1 by client/main.js:

    [uint32 header_len][utf8 json header][binary body]
"""
from __future__ import annotations

import json
import struct
import zlib

import numpy as np


def _pack_positions(pos: np.ndarray, quantise: bool):
    """Return (buffer, header_fields)."""
    if not quantise:
        return pos.astype(np.float32).tobytes(), {"pos": "f32"}
    lo = pos.min(axis=0)
    hi = pos.max(axis=0)
    span = np.maximum(hi - lo, 1e-6)
    q = np.round((pos - lo) / span * 65535.0).clip(0, 65535).astype(np.uint16)
    return q.tobytes(), {
        "pos": "u16",
        "lo": [float(v) for v in lo],
        "span": [float(v) for v in span],
    }


def encode_mesh(pos: np.ndarray, idx: np.ndarray, quantise=True, deflate=True,
                rgb: np.ndarray | None = None,
                part: np.ndarray | None = None) -> tuple[bytes, dict]:
    body, head = _pack_positions(pos, quantise)
    if len(pos) <= 65535:
        body += idx.astype(np.uint16).tobytes()
        head["idx"] = "u16"
    else:
        body += idx.astype(np.uint32).tobytes()
        head["idx"] = "u32"
    if rgb is not None:                   # per-vertex colour, after the indices
        body += rgb.astype(np.uint8).tobytes()
        head["rgb"] = True
    if part is not None:                  # animatable part ids, after the colours
        body += part.astype(np.uint16).tobytes()
        head["parts"] = True
    head.update(kind="mesh", nv=int(len(pos)), nt=int(len(idx)))
    return _finish(body, head, deflate)


def encode_points(pos: np.ndarray, rgb: np.ndarray, quantise=True, deflate=True) -> tuple[bytes, dict]:
    body, head = _pack_positions(pos, quantise)
    body += rgb.astype(np.uint8).tobytes()
    head.update(kind="points", nv=int(len(pos)), nt=0)
    return _finish(body, head, deflate)


def _finish(body: bytes, head: dict, deflate: bool) -> tuple[bytes, dict]:
    head["raw_bytes"] = len(body)
    if deflate:
        body = zlib.compress(body, level=6)
        head["z"] = True
    else:
        head["z"] = False
    head["bytes"] = len(body)
    return body, head


def frame(header: dict, body: bytes) -> bytes:
    """Wrap a header + body into one websocket binary frame."""
    raw = json.dumps(header, separators=(",", ":")).encode("utf-8")
    return struct.pack("<I", len(raw)) + raw + body
