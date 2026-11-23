// PZA's Composer for Meitou. Author: PZA. Licence: GPLv3 or later.
using System.Globalization;

namespace PZA.Composer.Meitou;

/// <summary>
/// PZA's Composer for other Meitou mods (version 1). It is a public static class read by reflection
/// (<c>Type.GetType("PZA.Composer.Meitou.ComposerApi, PZAComposer.Meitou")</c>), so no mod depends on the Composer. It works the same way as
/// other mods' public APIs (The Wasteland Remembers' <c>WastelandApi</c>).
/// <list type="bullet">
/// <item>Plain types only: strings, numbers, bools, and string-keyed dictionaries.</item>
/// <item>Nothing throws.</item>
/// <item>Times are game hours (<c>World.GameHours</c>).</item>
/// <item>Call it from your own system's serial steps (<c>Inputs</c>, <c>Schedule</c>, <c>SlowWorld</c>).</item>
/// <item><see cref="Version"/> only grows: nothing is removed or changes meaning.</item>
/// </list>
/// <para>
/// A mod proposes a beat: a true story moment that may reach the player now, later, or only as news. The Composer answers go, wait or no
/// (design Part 4, section 20). Go: show it now. Wait: ask again (not before RetryAt); the window slides, never past its latest. No: it still
/// happens, but as news, not near the player. With no Composer in the world, every proposal is a go.
/// </para>
/// </summary>
public static class ComposerApi
{
    public const int Version = 1;

    static readonly object s_lock = new();
    static ComposerSystem? s_system;
    static long s_generation;

    internal static long NewWorld(ComposerSystem system)
    {
        lock (s_lock)
        {
            s_system = system;
            return ++s_generation;
        }
    }

    internal static void Detach(ComposerSystem system)
    {
        lock (s_lock) if (s_system == system) s_system = null;
    }

    /// <summary>Which game world the answers are about: it changes with every new game and load.</summary>
    public static long Generation { get { lock (s_lock) return s_generation; } }

    /// <summary>Whether the Composer paces this world (off in its settings: false, and every proposal is a go).</summary>
    public static bool Active { get { lock (s_lock) return s_system?.On == true; } }

    /// <summary>The drama setting: "calm", "normal" or "harsh" ("" when inactive).</summary>
    public static string Drama { get { lock (s_lock) return s_system?.On == true ? s_system.Settings.Drama.ToString().ToLowerInvariant() : ""; } }

    /// <summary>The biggest beat the setting takes (a bigger one is refused): calm 60, normal 85, harsh 100. 100 when inactive.</summary>
    public static double Cap { get { lock (s_lock) return s_system?.On == true ? s_system.Settings.Cap : 100; } }

    /// <summary>A beat at least this strong is a peak (0.75 x Cap): one a cycle, in the peak slot only.</summary>
    public static double PeakAt { get { lock (s_lock) return s_system?.On == true ? s_system.Settings.PeakAt : 75; } }

    /// <summary>Tension near the player now: the active beats' intensities, each fading with the setting's half-life (0 when inactive).</summary>
    public static double Tension(double now) => With(c => c.Tension(now), 0.0);

    /// <summary>The curve's phase now: "build", "peak" (the peak slot) or "relief" ("" when inactive).</summary>
    public static string Phase(double now) => With(c => c.Phase(now).Phase.ToString().ToLowerInvariant(), "");

    /// <summary>The game hour the next relief begins (now, in a relief). A mod with a gentle moment can save it for then. NaN when inactive.</summary>
    public static double NextQuiet(double now) => With(c =>
    {
        var (ph, _, cycle) = c.Phase(now);
        return ph == CurvePhase.Relief ? now : c.Origin + cycle * c.Settings.CycleHours + c.Settings.BuildDays * 24;
    }, double.NaN);

    /// <summary>
    /// Proposes a beat. Returns <c>Answer</c> ("go", "wait" or "no"), <c>Why</c> (a reason in English) and <c>RetryAt</c> (game hours, for a
    /// wait; NaN otherwise). Asking again with the same source and id after a wait is how a beat waits. Once it went, it is active for
    /// <paramref name="durationHours"/>, or until <see cref="Ended"/>.
    /// </summary>
    /// <param name="source">Your mod's id (its manifest id, e.g. <c>author.mymod</c>).</param>
    /// <param name="id">Unique among your beats.</param>
    /// <param name="kind">Its kind (<c>war.march</c>, <c>refugees</c>, <c>hunt</c>...). The same kind is spaced 12 h apart.</param>
    /// <param name="arc">The story it belongs to (<c>siege:stack</c>). At most three at once near the player, and the third is the peak's.</param>
    /// <param name="intensity">0 to <see cref="Cap"/>.</param>
    /// <param name="hostile">Aimed at the player: one at a time.</param>
    /// <param name="earliest">The window's start (game hours).</param>
    /// <param name="latest">The window's end: unshown by then, it is told as news.</param>
    /// <param name="durationHours">How long it is active once it went.</param>
    /// <param name="closeness">0 (about two days' walk away) to 1 (at the player).</param>
    /// <param name="ties">How many of the player's ties it touches (their town, a faction's regard, people they know).</param>
    /// <param name="reason">Why it happens (a reason key).</param>
    /// <param name="now">The game hour now.</param>
    public static IReadOnlyDictionary<string, object> Propose(string source, string id, string kind, string arc, double intensity, bool hostile,
        double earliest, double latest, double durationHours, double closeness, int ties, string reason, double now)
    {
        try
        {
            var beat = new Beat(id ?? "", source ?? "", kind ?? "", arc ?? "", intensity, hostile, earliest, latest, durationHours, closeness, ties, reason ?? "");
            Verdict? v;
            lock (s_lock) v = s_system?.On == true ? s_system.Propose(beat, now) : null;
            v ??= Verdict.Go("no Composer in this world");
            return Row(v);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return Row(Verdict.Go("the Composer failed: " + e.Message));
        }
    }

    /// <summary>Your beat ended early (a siege broken, a hunt over). False when the Composer did not have it.</summary>
    public static bool Ended(string source, string id, double now)
    {
        try
        {
            lock (s_lock) return s_system?.On == true && s_system.End(source ?? "", id ?? "", now);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>
    /// The beats shown after <paramref name="sinceSeq"/>, oldest first (the last 300 at most). Each row has <c>Seq</c> (long), <c>Hours</c>,
    /// <c>Source</c>, <c>Id</c>, <c>Kind</c>, <c>Arc</c>, <c>Intensity</c>, <c>Peak</c> (bool), <c>Phase</c>, <c>Reason</c>. A crier, a kill cam
    /// or a chronicle can follow the story with it. Keep the last Seq you saw, and start from 0 when <see cref="Generation"/> changes.
    /// </summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object>> Shown(long sinceSeq = 0)
    {
        try
        {
            lock (s_lock) return s_system?.On == true ? s_system.ShownSince(sinceSeq) : [];
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return [];
        }
    }

    /// <summary>One line on the Composer's state, for a debug page.</summary>
    public static string Summary(double now) => With(c => c.Summary(now), "PZA's Composer: inactive");

    static T With<T>(Func<Composer, T> read, T fallback)
    {
        try
        {
            lock (s_lock) return s_system?.On == true && s_system.Core is { } c ? read(c) : fallback;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return fallback;
        }
    }

    static Dictionary<string, object> Row(Verdict v) => new(StringComparer.Ordinal)
    {
        ["Answer"] = v.Answer.ToString().ToLowerInvariant(),
        ["Why"] = v.Why,
        ["RetryAt"] = v.RetryAt,
    };

    internal static string F(double x) => x.ToString("0.#", CultureInfo.InvariantCulture);
}
