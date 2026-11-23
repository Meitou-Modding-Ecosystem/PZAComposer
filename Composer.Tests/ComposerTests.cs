// PZA's Composer. Author: PZA. Licence: GPLv3 or later.
using PZA.Composer;

namespace PZA.Composer.Tests;

/// <summary>Each claim of the design's Part 4 (section 20) as a test: the curve, the caps, the phases' rules, the hostile slot, the window, saving.</summary>
public sealed class ComposerTests
{
    static readonly DramaSettings Normal = DramaSettings.For(Drama.Normal);

    static Beat B(string id, double intensity, double at = 0, string kind = "", string arc = "", bool hostile = false, double duration = 24,
        double latest = double.NaN) =>
        new(id, "test", kind.Length == 0 ? "k" + id : kind, arc.Length == 0 ? "a" + id : arc, intensity, hostile, at, double.IsNaN(latest) ? at + 48 : latest,
            duration, 1, 0, "test");

    // normal: build-up 3 days (ramp 0-48 h, peak slot 48-72 h), relief 72-144 h; cycle 144 h
    const double Ramp = 1, PeakSlot = 50, Relief = 80;

    [Fact]
    public void The_half_life_is_the_closed_form()
    {
        Assert.Equal(41.64, DramaSettings.For(Drama.Calm).HalfLifeHours, 2);
        Assert.Equal(17.25, DramaSettings.For(Drama.Normal).HalfLifeHours, 2);
        Assert.Equal(7.75, DramaSettings.For(Drama.Harsh).HalfLifeHours, 2);
        foreach (var d in Enum.GetValues<Drama>())
        {
            var s = DramaSettings.For(d);
            // from TMax, tension is at Quiet exactly half way through the relief
            Assert.Equal(DramaSettings.Quiet, s.TMax * Math.Pow(0.5, s.ReliefDays * 24 / 2 / s.HalfLifeHours), 6);
        }
    }

    [Fact]
    public void A_peak_is_relative_to_the_setting()
    {
        Assert.Equal(45, DramaSettings.For(Drama.Calm).PeakAt);                            // calm can have a peak (section 19, #3)
        Assert.Equal(63.75, Normal.PeakAt);
        Assert.Equal(Normal.TMax - Normal.PeakAt, Normal.BuildTop);
    }

    [Fact]
    public void The_phases_run_from_the_origin()
    {
        var c = new Composer(Normal, origin: 100);
        Assert.Equal(CurvePhase.Build, c.Phase(100).Phase);
        Assert.Equal(CurvePhase.Peak, c.Phase(100 + 48).Phase);
        Assert.Equal(CurvePhase.Relief, c.Phase(100 + 72).Phase);
        Assert.Equal((CurvePhase.Build, 0L + 1), (c.Phase(100 + 144).Phase, c.Phase(100 + 144).Cycle));
        Assert.Equal(100 + 72, c.NextPhaseAt(100 + 50));
    }

    [Fact]
    public void The_build_up_admits_no_peak_and_ramps_its_ceiling()
    {
        var c = new Composer(Normal);
        Assert.Equal(Answer.Wait, c.Propose(B("p", 75, Ramp), Ramp).Answer);                // a peak waits for the peak slot
        Assert.Equal(Answer.Go, c.Propose(B("q", 25, Ramp), Ramp).Answer);                  // under the ramp's start (Quiet 30)
        Assert.Equal(Answer.Wait, c.Propose(B("r", 20, Ramp + 1), Ramp + 1).Answer);        // 25 + 20 over the ramp near its start
        var late = new Composer(Normal);
        Assert.Equal(Answer.Go, late.Propose(B("s", 60, 47), 47).Answer);                   // near the ramp's top (63.75)
    }

    [Fact]
    public void The_peak_slot_takes_one_peak_per_cycle()
    {
        var c = new Composer(Normal);
        Assert.Equal(Answer.Go, c.Propose(B("p1", 64, PeakSlot, duration: 100), PeakSlot).Answer);
        var second = c.Propose(B("p2", 64, 70), 70);                                        // p1 has faded to ~29: only the peak rule stops it
        Assert.Equal(("one peak per cycle", 72.0), (second.Why, second.RetryAt));
    }

    [Fact]
    public void Relief_caps_the_total_not_only_each_beat()
    {
        var c = new Composer(Normal);
        Assert.Equal(Answer.Go, c.Propose(B("a", 20, Relief), Relief).Answer);
        Assert.Equal(Answer.Wait, c.Propose(B("b", 20, Relief), Relief).Answer);            // 20 + 20 > Quiet
        Assert.Equal(Answer.Go, c.Propose(B("c", 5, Relief), Relief).Answer);
    }

    [Fact]
    public void Tension_never_passes_TMax_and_the_last_arc_is_the_peaks()
    {
        var c = new Composer(Normal);
        Assert.Equal(Answer.Go, c.Propose(B("x", 20, 10, duration: 100), 10).Answer);
        Assert.Equal(Answer.Go, c.Propose(B("y", 20, 40, duration: 100), 40).Answer);
        Assert.Equal(Answer.Wait, c.Propose(B("z", 5, 45), 45).Answer);                     // a third arc in build-up: kept for the peak
        Assert.Equal(Answer.Go, c.Propose(B("p", 85, PeakSlot), PeakSlot).Answer);          // the peak takes it
        Assert.True(c.Tension(PeakSlot) <= Normal.TMax);
        Assert.Equal(Answer.Wait, c.Propose(B("w", 5, PeakSlot), PeakSlot).Answer);         // three arcs: no fourth
    }

    [Fact]
    public void One_hostile_at_a_time_or_the_gate_decides()
    {
        var c = new Composer(Normal);
        Assert.Equal(Answer.Go, c.Propose(B("h1", 10, Ramp, hostile: true), Ramp).Answer);
        Assert.Equal("one hostile at a time", c.Propose(B("h2", 5, Ramp, hostile: true), Ramp).Why);
        c.Ended("test", "h1", Ramp + 1);
        Assert.Equal(Answer.Go, c.Propose(B("h3", 5, Ramp + 1, hostile: true), Ramp + 1).Answer);

        var gated = new Composer(Normal) { Gate = new Gate { Allow = false } };
        Assert.Equal("the hostile slot is taken", gated.Propose(B("h", 10, Ramp, hostile: true), Ramp).Why);
    }

    sealed class Gate : IHostileGate
    {
        public bool Allow = true;
        public bool MaySend(Beat beat, double now) => Allow;
        public void Started(Beat beat, double now) { }
        public void Ended(Beat beat, double now) { }
    }

    [Fact]
    public void The_window_and_the_kind_cooldown()
    {
        var c = new Composer(Normal);
        var early = c.Propose(B("e", 10, at: 30), 10);
        Assert.Equal((Answer.Wait, 30.0), (early.Answer, early.RetryAt));
        Assert.Equal(Answer.No, c.Propose(B("l", 10, at: 0, latest: 5), 10).Answer);         // passed: told as news
        Assert.Equal(Answer.Go, c.Propose(B("k1", 10, 10, kind: "siege"), 10).Answer);
        var again = c.Propose(B("k2", 10, 11, kind: "siege"), 11);
        Assert.Equal((Answer.Wait, 22.0), (again.Answer, again.RetryAt));
    }

    [Fact]
    public void The_score_is_bounded_and_the_order_never_depends_on_the_list()
    {
        var c = new Composer(Normal);
        var rnd = new Random(5);
        var beats = Enumerable.Range(0, 40).Select(i => new Beat($"b{i}", "test", $"k{i % 7}", $"a{i % 5}", rnd.Next(1, 40), false, 0, 48, 12,
            rnd.NextDouble() * 3 - 1, rnd.Next(-3, 20), "r")).ToList();
        Assert.All(beats, b => Assert.InRange(c.Score(b, 10), 0, 7));
        var a = new Composer(Normal).Offer(beats, 10).Select(x => x.Verdict.Answer).ToList();
        var shuffled = beats.OrderBy(_ => rnd.Next()).ToList();
        var bOut = new Composer(Normal).Offer(shuffled, 10).ToDictionary(x => x.Beat.Id, x => x.Verdict.Answer);
        Assert.Equal(a, beats.Select(x => bOut[x.Id]).ToList());
    }

    [Fact]
    public void A_save_round_trip_decides_the_same_afterwards_and_damage_changes_nothing()
    {
        var c = new Composer(Normal, origin: 3);
        c.Propose(B("x", 20, 10, kind: "siege", arc: "siege:stack"), 10);
        c.Propose(B("p", 85, PeakSlot + 3), PeakSlot + 3);
        using var ms = new MemoryStream();
        c.Write(ms);
        var d = new Composer(DramaSettings.For(Drama.Harsh));
        ms.Position = 0;
        d.Read(ms);
        Assert.Equal(c.Origin, d.Origin);
        Assert.Equal(c.Tension(60), d.Tension(60), 9);
        var probe = B("q", 70, PeakSlot + 4);
        Assert.Equal(c.Propose(probe, PeakSlot + 4), d.Propose(probe, PeakSlot + 4));

        var bytes = ms.ToArray();
        bytes[^3] ^= 0x7F;
        var e = new Composer(Normal);
        string before = e.Summary(1);
        Assert.ThrowsAny<InvalidDataException>(() => e.Read(new MemoryStream(bytes[..^2])));
        Assert.Equal(before, e.Summary(1));
    }
}
