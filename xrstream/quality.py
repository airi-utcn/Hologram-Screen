"""The quality model, defined once.

Both the metric (`session`) and the rate allocator (`policies`) need to agree
on what "good" means -- if they drift apart the policy optimises something the
benchmark does not measure.  So the penalty functions live here and both sides
import them.

Penalty is 0 when the viewer sees what they should and 1 when nothing usable is
on screen.  The key constraint is MAX_LOD_PENALTY < 1: the very coarsest copy
of an object must still score strictly better than no copy at all, otherwise a
quality-maximising allocator sees no reason to send cheap coarse geometry and
simply drops everything it cannot afford at full detail.
"""
from __future__ import annotations

#: worst penalty attributable to level of detail alone (must stay below 1.0)
MAX_LOD_PENALTY = 0.8

#: how many capture periods an update may lag before it counts as fully stale
STALE_TOLERANCE = 3.0

#: penalty for an object the client does not hold at all
MISSING_PENALTY = 1.0


def lod_penalty(level: int, required: int, coarsest: int) -> float:
    """Cost of holding `level` when the viewpoint wants `required` (0 = finest)."""
    if coarsest <= 0:
        return 0.0
    gap = max(0, level - required) / coarsest
    return MAX_LOD_PENALTY * min(1.0, gap)


def staleness_penalty(age_s: float, hz: float) -> float:
    """Cost of showing a capture frame `age_s` seconds old."""
    return min(1.0, max(0.0, age_s) * hz / STALE_TOLERANCE)


def value(importance: float, level: int, required: int, coarsest: int) -> float:
    """Quality delivered by holding `level`, in the same units as importance."""
    return importance * (1.0 - lod_penalty(level, required, coarsest))
