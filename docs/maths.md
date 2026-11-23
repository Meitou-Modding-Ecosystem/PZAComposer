# PZA's Composer: the maths

Every rule here is defined exactly and checked by brute force before it was coded. It was fuzzed with planted bugs after:
- `tools/euler/admission_check.py` is the rule in Python, run over 1,000 seeds x 30 days per setting.
- `Composer.Sim` re-runs the same claims on the C# core.
- `tests/fuzz` holds the fuzz targets, with the planted bugs in `tests/fuzz/mutants.txt`.

## Definitions

```
CAP(d)      the biggest single beat           calm 60, normal 85, harsh 100
TMAX(d)     the most tension at once          1.5 * CAP(d)
QUIET       relief's ceiling on tension       30
peak beat   intensity >= 0.75 * CAP(d)        (relative to the setting: calm has peaks too)
phases      build-up B(d) days, its last P(d) days the peak slot, then relief R(d) days
            calm B 5 / P 1 / R 5.5;  normal B 3 / P 1 / R 3;  harsh B 1.5 / P 0.5 / R 1.5
fade        tension(t) = sum over active beats of intensity * 0.5^((t - start) / HALF(d))
HALF(d)     (R(d) * 24 / 2) / log2(TMAX(d) / QUIET)
            closed form: from TMAX, tension falls to QUIET exactly half way through the relief
            calm 41.64 h, normal 17.25 h, harsh 7.75 h
BuildTop    TMAX - 0.75 * CAP  (= 0.75 * CAP: the build-up's ramp ends exactly at the smallest peak)
```

The curve runs from its origin, the first game hour the Composer sees in a world, so a new game opens with a build-up.

## Admission (in this order; the first rule that refuses names the reason)

1. A bad beat (not a number, or no duration) is refused (**no**).
2. A beat already active is refused (**no**).
3. Before its window opens, the beat **waits** until the window's start.
4. After its window closes, it is refused (**no**) and is told as news instead.
5. A beat over CAP is refused (**no**).
6. A kind shown less than 12 h ago **waits**.
7. If tension + beat > TMAX, it **waits**.
8. At most 3 arcs (stories) are active at once. **The third arc is always the peak's**: a new arc for a beat that is not a peak waits
   while 2 arcs are active. This holds in every phase. The fuzz target `composer.pacing` showed why. Three long beats admitted in a relief
   lasted into the next build-up and blocked its peak.
9. Each phase has its own rule:
   - **relief:** tension + beat ≤ QUIET. Relief caps the total, not only each beat.
   - **build-up:** tension + beat ≤ QUIET + (BuildTop − QUIET) × (how far through the ramp). BuildTop equals the smallest peak, so the
     ramp alone keeps peaks out of the build-up. The "peak comes later" rule only names the reason (an equivalent mutant, noted in
     `mutants.txt`).
   - **peak slot:** one peak beat per cycle.
10. A beat hostile to the player: one at a time. Rivalry Core's hostile slot decides instead, when it is there.

## Order (score)

Every term is in [0, 1]. Ties are broken by source, then id, so the order never depends on the list's order.

```
score     = 3*relevance + 2*continuity + 1*variety + 1*urgency
relevance = 0.5*closeness + 0.5*min(1, ties / 3)
```

## What the first version got wrong (`tools/euler/admission_as_written.py`)

The first version was a tension target curve with one cap. Simulation broke it in five ways:
1. The build-up's ceiling left no room for a peak at its end.
2. Lowering that ceiling starved the build-up.
3. A fixed peak size meant calm never had a peak.
4. The relevance term could exceed 1.
5. The fade was too slow for the relief to be quiet.

The definitions above fix all five.

## Checked: 1,000 seeds x 30 days per setting (C#, `Composer.Sim`)

| Setting | Most tension (TMAX) | Hours over TMAX | Peaks / 30 days (bound) | Shortest gap between peaks (relief) | Peak slots filled when a peak was offered | 2nd-half relief hours over QUIET |
| --- | --- | --- | --- | --- | --- | --- |
| calm | 90.0 (90) | 0 | 2.59 (2.86) | 229 h (132 h) | 86% | 0 |
| normal | 127.5 (128) | 0 | 3.66 (5.00) | 121 h (72 h) | 74% | 0 |
| harsh | 150.0 (150) | 0 | 6.92 (10.00) | 61 h (36 h) | 80% | 0 |

Peaks stay under the bound because a peak needs the world to offer one in the slot. The offered peaks that weren't taken were refused by
the hostile slot, a full set of arcs, or the kind's cooldown. That is correct behaviour, and the log names the reason.

The Python check, which has no kind cooldown, gives 2.80 / 3.85 / 8.18 peaks a month.
