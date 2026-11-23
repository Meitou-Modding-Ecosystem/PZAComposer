"""The Composer's admission rule, second version: explicit phases with their own limits. 1000 seeds x 30 days per setting.
Claims to check (the design's own numbers): tension never over the cap; peaks per 30 days about 30 / gap (calm ~3, normal ~5, harsh ~10);
never two peaks within one relief; relief really quiet; the score's terms each in [0, 1]."""
import random, statistics as st

CAP = {"calm": 60, "normal": 85, "harsh": 100}
BUILD = {"calm": 5.0, "normal": 3.0, "harsh": 1.5}       # days
PEAKW = {"calm": 1.0, "normal": 1.0, "harsh": 0.5}       # the peak slot, days (end of build-up)
RELIEF = {"calm": 5.5, "normal": 3.0, "harsh": 1.5}      # days
QUIET = 30                                               # relief ceiling: tension and any single beat
import math
# fade half-life from the closed form: tension falls from tmax to QUIET within half the relief
HALF = {d: (RELIEF[d] * 24 / 2) / math.log2(1.5 * CAP[d] / QUIET) for d in ("calm", "normal", "harsh")}
print("half-lives (h):", {d: round(h, 2) for d, h in HALF.items()})


def phase(t, d):
    period = (BUILD[d] + RELIEF[d]) * 24
    x = t % period
    if x < (BUILD[d] - PEAKW[d]) * 24: return "build", x / ((BUILD[d] - PEAKW[d]) * 24)
    if x < BUILD[d] * 24: return "peak", 1.0
    return "relief", 0.0

def is_peak(i, d): return i >= 0.75 * CAP[d]            # a peak is relative to the setting (calm's peak is smaller)

def admit(T, beat, ph, frac, d, peak_done):
    cap = CAP[d]; tmax = 1.5 * cap                      # cap = the biggest single beat; tmax = the most tension at once
    if beat["i"] > cap: return False
    if T + beat["i"] > tmax: return False                 # never more than tmax at once, in any phase
    if ph == "relief": return T + beat["i"] <= QUIET      # relief caps the total, not only each beat
    if ph == "build":                                    # a ramp from QUIET up to 60% of tmax; no peak beats yet
        top = tmax - 0.75 * cap                          # the build-up ends where the smallest peak still fits under tmax
        return not is_peak(beat["i"], d) and T + beat["i"] <= QUIET + (top - QUIET) * frac
    if is_peak(beat["i"], d): return not peak_done and T + beat["i"] <= tmax   # the peak slot: one peak beat
    return T + beat["i"] <= tmax

def score(b):                                            # every term normalised to [0, 1] (the fix for relevance)
    rel = min(1.0, 0.5 * b["close"] + 0.5 * min(1.0, b["ties"] / 3))
    return 3 * rel + 2 * b["cont"] + 1 * b["var"] + 1 * b["urg"]

def run(seed, d):
    rnd = random.Random(seed)
    active, Ts, peaks, shown, hostile_until, peak_cycle = [], [], [], 0, -1, -1
    period = int((BUILD[d] + RELIEF[d]) * 24)
    relief_violations = 0
    offered = set()
    for t in range(30 * 24):
        active = [b for b in active if t < b["s"] + b["dur"]]
        T = sum(b["i"] * 0.5 ** ((t - b["s"]) / HALF[d]) for b in active)
        ph, frac = phase(t, d)
        cyc = t // period
        props = []
        for _ in range(rnd.choice([0, 0, 0, 1, 1, 2])):     # ~0.67 proposals an hour near the player
            props.append(dict(i=rnd.choice([10, 15, 20, 30, 45, 60, 75, 90]), dur=rnd.randint(4, 48), hostile=rnd.random() < 0.3,
                              close=rnd.random(), ties=rnd.randint(0, 5), cont=float(rnd.random() < 0.3), var=float(rnd.random() < 0.7),
                              urg=rnd.random()))
        props.sort(key=score, reverse=True)
        if ph == "peak" and any(is_peak(p["i"], d) and p["i"] <= CAP[d] for p in props): offered.add(cyc)
        for p in props:
            if len(active) >= 3 or (p["hostile"] and t < hostile_until): continue
            if not is_peak(p["i"], d) and len(active) >= 2: continue   # the last arc slot is the peak's, in every phase (C# fuzz, 2026-11-22)
            if not admit(T, p, ph, frac, d, peak_cycle == cyc): continue
            p["s"] = t; active.append(p); shown += 1; T += p["i"]
            if p["hostile"]: hostile_until = t + p["dur"]
            if is_peak(p["i"], d): peaks.append(t); peak_cycle = cyc
        Ts.append(T)
        period_x = t % period
        if ph == "relief" and period_x >= (BUILD[d] + RELIEF[d] / 2) * 24 and T > QUIET + 1e-9: relief_violations += 1   # second half of relief
    gaps = [b - a for a, b in zip(peaks, peaks[1:])]
    slots_with_offer = len(offered)
    return dict(maxT=max(Ts), over=sum(T > 1.5 * CAP[d] + 1e-9 for T in Ts), peaks=len(peaks), mingap=min(gaps) if gaps else None,
                relief_bad=relief_violations, shown=shown, fill=(len(peaks) / slots_with_offer) if slots_with_offer else None)

for d in ("calm", "normal", "harsh"):
    rs = [run(s, d) for s in range(1000)]
    expect = 30 / (BUILD[d] + RELIEF[d])
    mg = [r["mingap"] for r in rs if r["mingap"] is not None]
    print(f"{d:6}: max tension {max(r['maxT'] for r in rs):6.1f} (tmax {1.5*CAP[d]:.0f}), hours over tmax {sum(r['over'] for r in rs)}, "
          f"peaks/30d {st.mean(r['peaks'] for r in rs):4.2f} (expected <= {expect:.2f}), shortest gap between peaks {min(mg) if mg else '-'} h "
          f"(relief {RELIEF[d]*24:.0f} h), peak slots filled when a peak was offered {100*st.mean(r['fill'] for r in rs if r['fill'] is not None):.0f}%, 2nd-half relief hours over {QUIET}: {sum(r['relief_bad'] for r in rs)}, beats shown {st.mean(r['shown'] for r in rs):.1f}")
