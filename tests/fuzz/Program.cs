// PZA's Composer: the fuzz harness. Author: PZA. Licence: GPLv3 or later. Never shipped; it never starts the game.
//   composer.pacing  random proposals hour by hour on every drama setting: tension never over TMax; a peak only in the peak slot and one
//                    per cycle; in relief the total stays at Quiet once admitted; never more than three arcs, and the third only ever
//                    for a peak; one hostile beat at a time; nothing outside its window; a kind not twice within its cooldown
//   composer.carry   three arcs carried into the next cycle and a new peak in its slot: never a fourth arc
//   composer.read    damaged Composer sections: only InvalidDataException, and then the Composer is unchanged
using PZA.Composer;
using PZA.MeitouUi.Fuzz;

static class Program
{
    static int Main(string[] a) => PzaFuzz.Run("PZA's Composer", a,
    [
        new("composer.pacing", 1500, Pacing, fz =>
        {
            foreach (var rule in new[] { "go", "peak", "relief.wait", "build.wait", "arc.wait", "hostile.wait", "window.wait", "kind.wait", "tmax.wait" })
                fz.Expect(fz.Counted(rule) > 0, $"rule '{rule}' never fired in {fz.Case} cases (a rule that never fires is a failed test)");
        }),
        new("composer.carry", 2000, Carry, fz => fz.Expect(fz.Counted("carried") > 0, "three arcs were never carried over")),
        new("composer.read", 5000, Read),
    ]);

    static void Pacing(PzaFuzz fz)
    {
        var s = DramaSettings.For(fz.Pick(Enum.GetValues<Drama>()));
        var c = new Composer(s, origin: fz.Range(-500, 500)) { KindCooldownHours = fz.Pick(new[] { 0.0, 6, 12, 24 }) };
        var lastKind = new Dictionary<string, double>();
        var peakCycle = new HashSet<long>();
        int next = 0;
        double longest = fz.Chance(0.3) ? 200 : 60;           // long beats keep an arc alive across cycles (a fourth arc needs that)
        int arcsUsed = fz.Pick(new[] { 3, 6 });             // few arcs: the same ones live on, and a fourth would be a peak
        string[] kinds = ["siege", "march", "refugees", "hunt", "rumour", "trade"];
        for (int h = 0; h < fz.Int(24, 24 * 30); h++)
        {
            var props = new List<Beat>();
            for (int k = fz.Int(0, 3); k > 0; k--)
            {
                double early = h + fz.Range(-12, 12);
                props.Add(new Beat($"b{next++}", "fz", fz.Pick(kinds), $"arc{fz.Int(0, arcsUsed)}", Math.Round(fz.Range(1, s.Cap * 1.2)), fz.Chance(0.3),
                    early, early + fz.Range(0, 30), fz.Range(1, longest), fz.Range(-0.5, 1.5), fz.Int(-2, 8), "fz"));
            }

            var (ph, _, cycle) = c.Phase(h);
            // half the cases ask one at a time, so the arcs active just before each answer are known (the reservation is a rule at admission:
            // an arc that held the peak may outlive it, so three arcs without a peak can be fine afterwards)
            bool single = fz.Chance(0.5);
            var answers = new List<(Beat, Verdict)>();
            if (single)
                foreach (var p in props)
                {
                    c.Step(h);
                    var before = c.ActiveBeats.Select(x => x.Arc).ToHashSet();
                    var v1 = c.Propose(p, h);
                    if (v1.Answer == Answer.Go && !c.IsPeak(p) && !before.Contains(p.Arc))
                        fz.Expect(before.Count <= Composer.MaxArcs - 2, $"a new arc ({before.Count + 1} of {Composer.MaxArcs}) for a non-peak in {ph}: the last arc is the peak's");
                    answers.Add((p, v1));
                }
            else answers.AddRange(c.Offer(props, h));
            foreach (var (b, v) in answers)
            {
                switch (v.Why)
                {
                    case "relief: quiet now": fz.Count("relief.wait"); break;
                    case "build-up: the peak comes later" or "build-up: too soon for that much": fz.Count("build.wait"); break;
                    case "the last arc is the peak's" or "three arcs already": fz.Count("arc.wait"); break;
                    case "one hostile at a time": fz.Count("hostile.wait"); break;
                    case "window not open": fz.Count("window.wait"); break;
                    case "kind cooling down": fz.Count("kind.wait"); break;
                    case "too much going on": fz.Count("tmax.wait"); break;
                }

                if (v.Answer != Answer.Go) continue;
                fz.Count("go");
                fz.Expect(h >= b.Earliest && h <= b.Latest, $"shown outside its window ({b.Earliest:0.0}..{b.Latest:0.0}) at {h}");
                fz.Expect(b.Intensity <= s.Cap, $"a beat of {b.Intensity} over the cap {s.Cap}");
                if (c.IsPeak(b))
                {
                    fz.Count("peak");
                    fz.Expect(ph == CurvePhase.Peak, $"a peak shown in {ph}");
                    fz.Expect(peakCycle.Add(cycle), $"a second peak in cycle {cycle}");
                }

                if (ph == CurvePhase.Relief) fz.Expect(c.Tension(h) <= DramaSettings.Quiet + 1e-9, $"relief at tension {c.Tension(h):0.0} after a beat of {b.Intensity}");
                if (lastKind.TryGetValue(b.Kind, out double k2)) fz.Expect(h - k2 >= c.KindCooldownHours, $"{b.Kind} again after {h - k2} h (cooldown {c.KindCooldownHours})");
                lastKind[b.Kind] = h;
            }

            double t = c.Tension(h);
            // the build-up's ramp: Quiet at its start to BuildTop at the peak slot; since BuildTop = TMax - 0.75 Cap = PeakAt exactly, the
            // ramp alone keeps a peak out of the build-up (the "peak comes later" rule only names the reason; design Part 4, section 21)
            if (ph == CurvePhase.Build && answers.Any(x => x.Item2.Answer == Answer.Go))
            {
                double frac = c.Phase(h).Frac, top = DramaSettings.Quiet + (s.BuildTop - DramaSettings.Quiet) * frac;
                fz.Expect(t <= top + 1e-9, $"build-up at tension {t:0.0} over its ramp {top:0.0}");
            }
            fz.Expect(t <= s.TMax + 1e-9, $"tension {t:0.0} over TMax {s.TMax}");
            var arcs = c.ActiveBeats.Select(b => b.Arc).Distinct().Count();
            fz.Expect(arcs <= Composer.MaxArcs, $"{arcs} arcs at once");
            fz.Expect(c.ActiveBeats.Count(b => b.Hostile) <= 1, "two hostile beats at once");
        }
    }

    // Three arcs carried into the next cycle (two from the build-up, the peak's own) and a new peak offered in the next peak slot: the
    // fourth arc must wait. The random walk rarely builds this by chance, so it is built on purpose, with random sizes and times.
    static void Carry(PzaFuzz fz)
    {
        var s = DramaSettings.For(fz.Pick(Enum.GetValues<Drama>()));
        var c = new Composer(s) { KindCooldownHours = 0 };
        double cyc = s.CycleHours, slot = s.BuildDays * 24 - s.PeakDays * 24, life = cyc * fz.Range(1.6, 3);
        double t1 = fz.Range(0, 2), t2 = t1 + fz.Range(0, 2);
        Beat Mk(string id, string arc, double i, double at) => new(id, "fz", id, arc, i, false, at, at + 1, life, 1, 1, "fz");
        c.Propose(Mk("a", "A", fz.Range(1, 10), t1), t1);
        c.Propose(Mk("b", "B", fz.Range(1, 10), t2), t2);
        double p1 = slot + fz.Range(0, s.PeakDays * 24 - 1);
        var first = c.Propose(Mk("p1", "C", Math.Round(fz.Range(s.PeakAt, s.Cap)), p1), p1);
        fz.Expect(first.Answer == Answer.Go, $"the first peak was refused: {first.Why}");
        double p2 = cyc + slot + fz.Range(0, s.PeakDays * 24 - 1);
        c.Step(p2);
        if (c.ActiveBeats.Select(x => x.Arc).Distinct().Count() < 3) return;
        fz.Count("carried");
        var second = c.Propose(Mk("p2", "D", Math.Round(fz.Range(s.PeakAt, s.Cap)), p2), p2);
        fz.Expect(second.Answer != Answer.Go, $"a fourth arc let in at {p2:0} ({second.Why})");
        fz.Expect(c.ActiveBeats.Select(x => x.Arc).Distinct().Count() <= Composer.MaxArcs, "four arcs at once");
    }

    static void Read(PzaFuzz fz)
    {
        var good = new Composer(DramaSettings.For(Drama.Normal), origin: 7);
        good.Propose(new Beat("x", "fz", "siege", "arc", 20, true, 0, 30, 24, 1, 1, "r"), 5);
        using var ms = new MemoryStream();
        good.Write(ms);
        byte[] valid = ms.ToArray();
        byte[] bytes = fz.Below(3) switch { 0 => valid, 1 => fz.Mutate(valid), _ => valid[..fz.Int(0, valid.Length)] };
        var c = new Composer(DramaSettings.For(Drama.Calm), origin: 1);
        string before = c.Summary(3);
        try
        {
            c.Read(new MemoryStream(bytes));
            if (ReferenceEquals(bytes, valid)) fz.Expect(c.Origin == 7 && c.ActiveBeats.Count == 1, "the good section didn't round-trip");
        }
        catch (InvalidDataException)
        {
            fz.Expect(c.Summary(3) == before, "a refused section changed the Composer");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            fz.Expect(false, $"Composer.Read threw {e.GetType().Name}: {e.Message}");
        }
    }
}
