# PZA's Composer

> **Version 0.2.0** (beta, not on the Workshop yet: the pacing core, and the Meitou mod `pza.composer` with `ComposerApi` v1 for other
> mods, its settings (on/off, drama) and its save. No mod feeds it yet.) · Meitou mod id `pza.composer`

The story broker for PZA's mods and the Meitou ecosystem. Mods propose **beats** (a siege beginning, refugees on the road, a hunt, a raid
on the player). The Composer decides which ones reach the player now, which wait, and which are told as news instead, so the world's
stories rise and fall in a curve: a build-up, a peak, then a relief. Author: PZA. GPL-3.0-or-later.

The maths, its definitions and its checks: [docs/maths.md](docs/maths.md).

## The rules
| Setting | Cap (biggest beat) | Build-up | Peak slot (last part of the build-up) | Relief | Fade half-life |
|---|---|---|---|---|---|
| Calm | 60 | 5 days | 1 day | 5.5 days | 41.64 h |
| Normal (default) | 85 | 3 days | 1 day | 3 days | 17.25 h |
| Harsh | 100 | 1.5 days | 0.5 day | 1.5 days | 7.75 h |

- Tension is the sum of the active beats, each fading with the half-life; never over `TMax = 1.5 x Cap`.
- A peak is a beat of at least `0.75 x Cap`; one per cycle, only in the peak slot.
- Build-up: the total ramps from Quiet (30) to `BuildTop = TMax - 0.75 x Cap`. Because `BuildTop` equals the peak size exactly, the ramp
  alone keeps peaks out of the build-up.
- Relief: the total stays at Quiet or below.
- At most three arcs (stories) at once, and the third is always kept for the peak.
- One hostile beat at a time (or Rivalry Core's hostile slot decides).
- A beat goes only inside its window; one whose window has passed is told as news. A kind (siege, refugees...) waits 12 h before repeating.
- The half-life is the closed form `HALF = (R x 24 / 2) / log2(TMax / Quiet)`: from TMax, tension reaches Quiet half way through the relief.

## Checks (definitions, closed forms, brute force before code, fuzz and planted bugs after)
- `python tools/euler/admission_check.py`: the rule in Python, 1,000 seeds x 30 days per setting (`admission_as_written.py`: the first
  version and how it failed).
- `dotnet test Composer.Tests`: one test per claim (11).
- `dotnet run --project Composer.Sim -c Release`: 1,000 seeds x 30 days per setting. TMax is never passed, peaks stay under one per cycle,
  the gap between peaks is at least the relief, and the second half of each relief stays at Quiet.
- `mod\build.ps1 -Fuzz` (with [PZA's Meitou UI](https://github.com/Meitou-Modding-Ecosystem/PZAMeitouUi) checked out beside this repo for its fuzz kit): fuzz targets `composer.pacing`, `composer.carry` and `composer.read`,
  plus 10 planted bugs in `tests/fuzz/mutants.txt`. One more mutant, the build-up's peak rule, is equivalent: the ramp already does its
  work. It is noted in the file.

## The mod (`mod/`)
- `mod\build.ps1` builds and stages `mod\out\PZAComposer\` (installs nothing); `-Check` also runs the offline check (Meitou's own
  code-mod checks, the settings page, `ComposerApi` called by reflection the way a client mod calls it, the save round trip), the tests, the
  1,000-seed check and the fuzz harness.
- Settings > Mods: **Pace the world's stories** (on/off) and **Drama** (calm / normal / harsh, default normal).
- `ComposerApi` (read by reflection: `Type.GetType("PZA.Composer.Meitou.ComposerApi, PZAComposer.Meitou")`): `Propose(...)` returns
  `Answer` go / wait / no, `Why`, `RetryAt`; `Ended`, `Tension`, `Phase`, `NextQuiet`, `Shown`, `Drama`, `Cap`, `PeakAt`, `Active`,
  `Generation`, `Summary`. With no Composer in the world every proposal is a go.
