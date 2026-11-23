// PZA's Composer. Author: PZA. Licence: GPLv3 or later.
namespace PZA.Composer;

/// <summary>How often trouble reaches the player (the Mods page setting; normal by default, user 2026-11-22).</summary>
public enum Drama { Calm, Normal, Harsh }

/// <summary>Where the story curve stands: building up, the peak slot (the end of the build-up), or relief.</summary>
public enum CurvePhase { Build, Peak, Relief }

/// <summary>
/// The curve's numbers for one drama setting (`docs/maths.md`, checked over 1,000 seeds x
/// 30 days in `tools/euler/admission_check.py`). Everything is derived from the setting; nothing here is tuned by hand except the
/// table itself.
/// </summary>
public sealed record DramaSettings(Drama Drama, double Cap, double BuildDays, double PeakDays, double ReliefDays)
{
    /// <summary>Relief's ceiling on tension (the total, not only each beat).</summary>
    public const double Quiet = 30;

    /// <summary>The most tension at once.</summary>
    public double TMax => 1.5 * Cap;

    /// <summary>A peak beat: at least three quarters of the setting's biggest single beat (a peak is relative to the setting).</summary>
    public double PeakAt => 0.75 * Cap;

    /// <summary>
    /// The fade's half-life (hours), from its closed form: tension falls from <see cref="TMax"/> to <see cref="Quiet"/> within half the
    /// relief, so HALF = (R / 2) / log2(TMax / Quiet). Calm 41.64 h, normal 17.25 h, harsh 7.75 h.
    /// </summary>
    public double HalfLifeHours => ReliefDays * 24 / 2 / Math.Log2(TMax / Quiet);

    /// <summary>One whole curve: build-up then relief (hours).</summary>
    public double CycleHours => (BuildDays + ReliefDays) * 24;

    /// <summary>Where the build-up ends: the smallest peak still fits under <see cref="TMax"/> there.</summary>
    public double BuildTop => TMax - PeakAt;

    public static DramaSettings For(Drama d) => d switch
    {
        Drama.Calm => new(d, 60, 5.0, 1.0, 5.5),
        Drama.Harsh => new(d, 100, 1.5, 0.5, 1.5),
        _ => new(Drama.Normal, 85, 3.0, 1.0, 3.0),
    };
}
