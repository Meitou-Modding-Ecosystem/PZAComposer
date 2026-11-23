// PZA's Composer. Author: PZA. Licence: GPLv3 or later.
using System.Globalization;
using System.Text;

namespace PZA.Composer;

/// <summary>
/// The Composer's core: which of the world's true moments reach the player, when, and how many at once (design Part 4, section 20, the
/// corrected rule; section 19 lists what the first version got wrong). It never invents or hides a world event: it only paces what is
/// proposed to it. Deterministic (no randomness: the curve runs from <see cref="Origin"/>), saved, and every decision is counted by reason.
/// </summary>
public sealed class Composer(DramaSettings settings, double origin = 0)
{
    /// <summary>At most this many arcs active near the player; the last one is always kept for the peak.</summary>
    public const int MaxArcs = 3;

    /// <summary>A kind shown within this many hours scores no variety.</summary>
    public const double VarietyHours = 72;

    /// <summary>The same kind is never shown twice within this many hours.</summary>
    public double KindCooldownHours { get; init; } = 12;

    public DramaSettings Settings { get; private set; } = settings;

    /// <summary>The hour the first build-up began: the curve's phases are counted from it.</summary>
    public double Origin { get; private set; } = origin;

    sealed record Active(Beat Beat, double Start);

    readonly List<Active> _active = [];
    readonly Dictionary<string, double> _kindShown = new(StringComparer.Ordinal);
    readonly Dictionary<string, double> _arcShown = new(StringComparer.Ordinal);
    long _peakCycle = -1;

    /// <summary>Decisions by reason (every rule counts when it fires).</summary>
    public Dictionary<string, long> Counts { get; } = new(StringComparer.Ordinal);

    /// <summary>The hostile slot (Rivalry Core's when it is there); null: one hostile beat at a time, the Composer's own rule.</summary>
    public IHostileGate? Gate { get; set; }

    public IReadOnlyList<Beat> ActiveBeats => [.. _active.Select(a => a.Beat)];

    /// <summary>Changes the drama setting (the Mods page) without restarting the curve.</summary>
    public void SetDrama(DramaSettings s) => Settings = s;

    // ------------------------------------------------------------------ the curve

    /// <summary>Where the curve stands at <paramref name="now"/>: the phase, how far through the build-up's ramp (0 to 1), and the cycle number.</summary>
    public (CurvePhase Phase, double Frac, long Cycle) Phase(double now)
    {
        var s = Settings;
        double x = now - Origin;
        long cycle = (long)Math.Floor(x / s.CycleHours);
        double inCycle = x - cycle * s.CycleHours;
        double rampHours = (s.BuildDays - s.PeakDays) * 24;
        if (inCycle < rampHours) return (CurvePhase.Build, rampHours <= 0 ? 1 : inCycle / rampHours, cycle);
        if (inCycle < s.BuildDays * 24) return (CurvePhase.Peak, 1, cycle);
        return (CurvePhase.Relief, 0, cycle);
    }

    /// <summary>The next hour the phase changes (for a "wait until").</summary>
    public double NextPhaseAt(double now)
    {
        var s = Settings;
        var (ph, _, cycle) = Phase(now);
        double start = Origin + cycle * s.CycleHours;
        return ph switch
        {
            CurvePhase.Build => start + (s.BuildDays - s.PeakDays) * 24,
            CurvePhase.Peak => start + s.BuildDays * 24,
            _ => start + s.CycleHours,
        };
    }

    /// <summary>Tension now: each active beat's intensity, fading with the setting's half-life since it started.</summary>
    public double Tension(double now)
    {
        double t = 0, h = Settings.HalfLifeHours;
        foreach (var a in _active) t += a.Beat.Intensity * Math.Pow(0.5, Math.Max(0, now - a.Start) / h);
        return t;
    }

    public bool IsPeak(Beat b) => b.Intensity >= Settings.PeakAt;

    /// <summary>Drops the beats whose time is over.</summary>
    public void Step(double now)
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var a = _active[i];
            if (now < a.Start + a.Beat.DurationHours) continue;
            _active.RemoveAt(i);
            if (a.Beat.Hostile) Gate?.Ended(a.Beat, now);
        }
    }

    /// <summary>A beat its mod ended early (beaten, called off).</summary>
    public void Ended(string source, string id, double now)
    {
        int i = _active.FindIndex(a => a.Beat.Source == source && a.Beat.Id == id);
        if (i < 0) return;
        var b = _active[i].Beat;
        _active.RemoveAt(i);
        if (b.Hostile) Gate?.Ended(b, now);
    }

    // ------------------------------------------------------------------ the score (section 20: every term in [0, 1])

    public double Score(Beat b, double now)
    {
        double rel = 0.5 * Math.Clamp(b.Closeness, 0, 1) + 0.5 * Math.Min(1.0, Math.Max(0, b.Ties) / 3.0);
        double cont = _active.Any(a => a.Beat.Arc == b.Arc) || _arcShown.ContainsKey(b.Arc) ? 1 : 0;
        double variety = _kindShown.TryGetValue(b.Kind, out double k) && now - k < VarietyHours ? 0 : 1;
        double span = b.Latest - b.Earliest;
        double urgency = span <= 0 ? 1 : Math.Clamp((now - b.Earliest) / span, 0, 1);
        return 3 * rel + 2 * cont + 1 * variety + 1 * urgency;
    }

    // ------------------------------------------------------------------ admission

    /// <summary>Decides one proposal now.</summary>
    public Verdict Propose(Beat b, double now) => Offer([b], now)[0].Verdict;

    /// <summary>
    /// Decides a batch: the admissible ones in score order (highest first, ties by source and id so the order never depends on the
    /// list's), each admitted beat counting against the next. Returns every proposal with its verdict, in the order given.
    /// </summary>
    public IReadOnlyList<(Beat Beat, Verdict Verdict)> Offer(IReadOnlyList<Beat> proposals, double now)
    {
        Step(now);
        var verdicts = new Verdict[proposals.Count];
        var order = Enumerable.Range(0, proposals.Count)
            .OrderByDescending(i => Score(proposals[i], now))
            .ThenBy(i => proposals[i].Source, StringComparer.Ordinal).ThenBy(i => proposals[i].Id, StringComparer.Ordinal).ToList();
        foreach (int i in order)
        {
            var v = Admit(proposals[i], now);
            verdicts[i] = v;
            Count(v.Why);
            if (v.Answer == Answer.Go) Start(proposals[i], now);
        }

        return [.. proposals.Select((b, i) => (b, verdicts[i]))];
    }

    void Count(string why) => Counts[why] = Counts.GetValueOrDefault(why) + 1;

    Verdict Admit(Beat b, double now)
    {
        var s = Settings;
        if (!double.IsFinite(b.Intensity) || b.Intensity < 0 || !double.IsFinite(b.DurationHours) || b.DurationHours <= 0) return Verdict.No("bad beat");
        if (_active.Any(a => a.Beat.Source == b.Source && a.Beat.Id == b.Id)) return Verdict.No("already active");
        if (now < b.Earliest) return Verdict.Wait("window not open", b.Earliest);
        if (now > b.Latest) return Verdict.No("window passed: tell it as news");
        if (b.Intensity > s.Cap) return Verdict.No("bigger than the setting's biggest beat");
        if (_kindShown.TryGetValue(b.Kind, out double k) && now - k < KindCooldownHours) return Verdict.Wait("kind cooling down", k + KindCooldownHours);

        double t = Tension(now);
        if (t + b.Intensity > s.TMax) return Verdict.Wait("too much going on", now + 1);

        var (ph, frac, cycle) = Phase(now);
        bool peak = IsPeak(b);
        bool newArc = !_active.Any(a => a.Beat.Arc == b.Arc);
        int arcs = _active.Select(a => a.Beat.Arc).Distinct(StringComparer.Ordinal).Count();
        if (newArc && arcs >= MaxArcs) return Verdict.Wait("three arcs already", now + 1);
        // the third arc is always the peak's: in every phase, not only in build-up (the composer.pacing fuzz target caught three long relief
        // beats carried into the next build-up and blocking its peak)
        if (!peak && newArc && arcs >= MaxArcs - 1) return Verdict.Wait("the last arc is the peak's", now + 1);

        switch (ph)
        {
            case CurvePhase.Relief:
                if (t + b.Intensity > DramaSettings.Quiet) return Verdict.Wait("relief: quiet now", NextPhaseAt(now));
                break;
            case CurvePhase.Build:
                if (peak) return Verdict.Wait("build-up: the peak comes later", NextPhaseAt(now));
                if (t + b.Intensity > DramaSettings.Quiet + (s.BuildTop - DramaSettings.Quiet) * frac) return Verdict.Wait("build-up: too soon for that much", now + 1);
                break;
            case CurvePhase.Peak:
                if (peak && _peakCycle == cycle) return Verdict.Wait("one peak per cycle", NextPhaseAt(now));
                break;
        }

        if (b.Hostile)
        {
            if (Gate == null && _active.Any(a => a.Beat.Hostile)) return Verdict.Wait("one hostile at a time", now + 1);
            if (Gate != null && !Gate.MaySend(b, now)) return Verdict.Wait("the hostile slot is taken", now + 1);
        }

        return Verdict.Go(peak ? "go: the peak" : ph == CurvePhase.Relief ? "go: a quiet beat" : "go");
    }

    void Start(Beat b, double now)
    {
        _active.Add(new Active(b, now));
        _kindShown[b.Kind] = now;
        _arcShown[b.Arc] = now;
        var (ph, _, cycle) = Phase(now);
        if (IsPeak(b) && ph == CurvePhase.Peak) _peakCycle = cycle;
        if (b.Hostile) Gate?.Started(b, now);
    }

    // ------------------------------------------------------------------ save

    const uint Magic = 0x504D_4F43;   // "COMP"
    const int Version = 1;
    const int MaxSaved = 10000;

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(Version);
        w.Write((int)Settings.Drama);
        w.Write(Origin);
        w.Write(_peakCycle);
        w.Write(_active.Count);
        foreach (var a in _active)
        {
            var b = a.Beat;
            foreach (string x in (string[])[b.Id, b.Source, b.Kind, b.Arc, b.Reason]) w.Write(x);
            foreach (double x in (double[])[b.Intensity, b.Earliest, b.Latest, b.DurationHours, b.Closeness, a.Start]) w.Write(x);
            w.Write(b.Hostile);
            w.Write(b.Ties);
        }

        foreach (var d in new[] { _kindShown, _arcShown })
        {
            var rows = d.OrderBy(e => e.Key, StringComparer.Ordinal).Take(MaxSaved).ToList();
            w.Write(rows.Count);
            foreach (var (key, v) in rows) { w.Write(key); w.Write(v); }
        }
    }

    /// <summary>Reads what <see cref="Write"/> wrote; a damaged section throws <see cref="InvalidDataException"/> and changes nothing.</summary>
    public void Read(Stream stream)
    {
        try
        {
            using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (r.ReadUInt32() != Magic) throw new InvalidDataException("not a Composer section");
            int v = r.ReadInt32();
            if (v != Version) throw new InvalidDataException($"Composer section version {v}");
            int drama = r.ReadInt32();
            if (!Enum.IsDefined((Drama)drama)) throw new InvalidDataException($"bad drama {drama}");
            double origin = Fin(r);
            long peakCycle = r.ReadInt64();
            int n = Count(r);
            var active = new List<Active>();
            for (int i = 0; i < n; i++)
            {
                string id = r.ReadString(), source = r.ReadString(), kind = r.ReadString(), arc = r.ReadString(), reason = r.ReadString();
                double intensity = Fin(r), earliest = Fin(r), latest = Fin(r), duration = Fin(r), closeness = Fin(r), start = Fin(r);
                bool hostile = r.ReadBoolean();
                int ties = r.ReadInt32();
                active.Add(new Active(new Beat(id, source, kind, arc, intensity, hostile, earliest, latest, duration, closeness, ties, reason), start));
            }

            var maps = new List<Dictionary<string, double>>();
            for (int m = 0; m < 2; m++)
            {
                var d = new Dictionary<string, double>(StringComparer.Ordinal);
                int c = Count(r);
                for (int i = 0; i < c; i++) d[r.ReadString()] = Fin(r);
                maps.Add(d);
            }

            // everything parsed: now the state changes
            Settings = DramaSettings.For((Drama)drama);
            Origin = origin;
            _peakCycle = peakCycle;
            _active.Clear();
            _active.AddRange(active);
            _kindShown.Clear();
            foreach (var (key, val) in maps[0]) _kindShown[key] = val;
            _arcShown.Clear();
            foreach (var (key, val) in maps[1]) _arcShown[key] = val;
        }
        catch (Exception e) when (e is EndOfStreamException or IOException or FormatException or ArgumentException or OverflowException)
        {
            throw new InvalidDataException("damaged Composer section: " + e.Message, e);
        }
    }

    static int Count(BinaryReader r)
    {
        int n = r.ReadInt32();
        if (n < 0 || n > MaxSaved) throw new InvalidDataException($"bad count {n}");
        return n;
    }

    static double Fin(BinaryReader r)
    {
        double v = r.ReadDouble();
        if (!double.IsFinite(v)) throw new InvalidDataException("not a number");
        return v;
    }

    /// <summary>One line for a log: the curve, tension, active beats, decisions by reason.</summary>
    public string Summary(double now)
    {
        var (ph, _, cycle) = Phase(now);
        return string.Create(CultureInfo.InvariantCulture,
            $"Composer ({Settings.Drama}): {ph.ToString().ToLowerInvariant()} of cycle {cycle}, tension {Tension(now):0}/{Settings.TMax:0}, {_active.Count} active; ")
            + string.Join(", ", Counts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => $"{c.Key} {c.Value}"));
    }
}
