// PZA's Composer: the Part 4 check re-run on the C# core. Author: PZA. Licence: GPLv3 or later.
// Same claims as tools/euler/admission_check.py (1,000 seeds x 30 days per setting, hourly):
//   tension never over TMax; peaks at most one per cycle (30 days / cycle); the gap between peaks at least the relief; in the second half of
//   each relief tension at or under Quiet; and the share of peak slots filled when a peak was offered. Exit code 1 when a claim fails.
using System.Globalization;
using PZA.Composer;

int fails = 0;
foreach (var drama in new[] { Drama.Calm, Drama.Normal, Drama.Harsh })
{
    var s = DramaSettings.For(drama);
    double maxT = 0, overHours = 0, reliefBad = 0, minGap = double.MaxValue, peaksSum = 0, offered = 0, filled = 0;
    for (int seed = 0; seed < 1000; seed++)
    {
        var rnd = new Random(seed);
        var c = new Composer(s);
        var peaks = new List<double>();
        var peakOffered = new HashSet<long>();
        var peakFilled = new HashSet<long>();
        int next = 0;
        string[] kinds = ["siege", "march", "refugees", "hunt", "rumour", "trade", "raid", "column"];
        int[] intensities = [10, 15, 20, 30, 45, 60, 75, 90];
        for (int h = 0; h < 30 * 24; h++)
        {
            var props = new List<Beat>();
            int n = new[] { 0, 0, 0, 1, 1, 2 }[rnd.Next(6)];
            for (int k = 0; k < n; k++)
            {
                double i = Math.Min(intensities[rnd.Next(intensities.Length)], s.Cap);
                props.Add(new Beat($"b{next++}", "sim", kinds[rnd.Next(kinds.Length)], $"arc{rnd.Next(6)}", i, rnd.NextDouble() < 0.3, h, h + rnd.Next(1, 24),
                    rnd.Next(4, 49), rnd.NextDouble(), rnd.Next(0, 6), "sim"));
            }

            var (ph, _, cycle) = c.Phase(h);
            if (ph == CurvePhase.Peak && props.Any(c.IsPeak)) peakOffered.Add(cycle);
            foreach (var (b, v) in c.Offer(props, h))
                if (v.Answer == Answer.Go && c.IsPeak(b)) { peaks.Add(h); peakFilled.Add(cycle); }

            double t = c.Tension(h);
            maxT = Math.Max(maxT, t);
            if (t > s.TMax + 1e-9) overHours++;
            double inCycle = (h - c.Origin) % s.CycleHours;
            if (ph == CurvePhase.Relief && inCycle >= (s.BuildDays + s.ReliefDays / 2) * 24 && t > DramaSettings.Quiet + 1e-9) reliefBad++;
        }

        for (int k = 1; k < peaks.Count; k++) minGap = Math.Min(minGap, peaks[k] - peaks[k - 1]);
        peaksSum += peaks.Count;
        offered += peakOffered.Count;
        filled += peakOffered.Count(peakFilled.Contains);
    }

    double perMonth = peaksSum / 1000, bound = 30 * 24 / s.CycleHours;
    bool ok = overHours == 0 && reliefBad == 0 && perMonth <= bound + 1e-9 && minGap >= s.ReliefDays * 24;
    if (!ok) fails++;
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{drama,-6}: most tension {maxT:0.0} (TMax {s.TMax:0}), hours over {overHours}, peaks/30 d {perMonth:0.00} (bound {bound:0.00}), shortest gap {minGap:0} h (relief {s.ReliefDays * 24:0} h), peak slots filled when offered {100 * filled / Math.Max(1, offered):0}%, 2nd-half relief hours over {DramaSettings.Quiet:0}: {reliefBad}, half-life {s.HalfLifeHours:0.00} h  {(ok ? "PASS" : "FAIL")}"));
}

return fails == 0 ? 0 : 1;
