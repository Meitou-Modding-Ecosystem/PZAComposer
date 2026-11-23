"""Check the Composer's maths as written in the design (section 7), the Project Euler way: define it exactly, run it, try to break it.
Hourly steps over 30 days, 200 seeds. Proposals arrive at random; the broker applies the hard limits and the score as written."""
import math, random, statistics as st

H = 12.0                                   # fade half-life, game hours
CAP = {"calm": 60, "normal": 85, "harsh": 100}
SHAPE = {"calm": (5, 5.5), "normal": (3, 3), "harsh": (1.5, 1.5)}   # (build-up days, relief days), mid of the written ranges

def target(t, drama):
    b, r = SHAPE[drama]; period = (b + r) * 24; x = t % period
    lo, hi = 10.0, CAP[drama]
    return lo + (hi - lo) * x / (b * 24) if x < b * 24 else lo          # ramp up over build-up, then relief

def run(seed, drama, as_written=True):
    rnd = random.Random(seed)
    active, shown, hostile_busy_until = [], [], -1
    T_series, peaks = [], 0
    last_peak_end = -10**9
    for t in range(30 * 24):
        # world proposes ~1 beat per 4 h near the player
        props = []
        if rnd.random() < 0.25:
            props.append(dict(i=rnd.choice([10, 20, 30, 45, 60, 75, 90]), d=rnd.randint(4, 48), hostile=rnd.random() < 0.3,
                              rel=rnd.random() + (1 if rnd.random() < 0.3 else 0), cont=rnd.random() < 0.3))
        active = [b for b in active if t < b["s"] + b["d"]]
        T = sum(b["i"] * 0.5 ** ((t - b["s"]) / H) for b in active)
        tgt = target(t, drama)
        for p in props:
            arcs_ok = len(active) < 3
            hostile_ok = not p["hostile"] or t >= hostile_busy_until
            peak = p["i"] >= 70
            relief_ok = not peak or t - last_peak_end > SHAPE[drama][1] * 24
            if not (arcs_ok and hostile_ok and relief_ok):
                continue
            if as_written:
                accept = True                                    # the design ranks but never rejects on the curve
            else:
                accept = T + p["i"] <= tgt + 10                  # fix: the curve is a ceiling, not only a ranking term
            if accept:
                p["s"] = t; active.append(p); shown.append(p)
                if p["hostile"]: hostile_busy_until = t + p["d"]
                if peak: peaks += 1; last_peak_end = t + p["d"]
        T = sum(b["i"] * 0.5 ** ((t - b["s"]) / H) for b in active)
        T_series.append(T)
    over = sum(1 for k, T in enumerate(T_series) if T > CAP[drama]) / len(T_series)
    return dict(maxT=max(T_series), over=over, peaks=peaks, shown=len(shown))

for drama in ("calm", "normal", "harsh"):
    for fix in (False, True):
        rs = [run(s, drama, as_written=not fix) for s in range(200)]
        print(f"{drama:6} {'fixed ' if fix else 'as written'}: max tension median {st.median(r['maxT'] for r in rs):6.1f} "
              f"(cap {CAP[drama]}), hours over the cap {100*st.mean(r['over'] for r in rs):5.1f}%, "
              f"peaks/30d {st.mean(r['peaks'] for r in rs):4.1f}, beats shown {st.mean(r['shown'] for r in rs):5.1f}")

# relevance as written: closeness in [0,1] + ties in [0, n]: unbounded, so it can outweigh the 3-weight of everything else
print("relevance range as written: [0, 1 + ties], e.g. ties=3 gives", 3 * (1 + 3), "vs the rest's max", 2 + 1 + 2 + 1)
