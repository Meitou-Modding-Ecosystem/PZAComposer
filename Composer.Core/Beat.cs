// PZA's Composer. Author: PZA. Licence: GPLv3 or later.
namespace PZA.Composer;

/// <summary>
/// A story moment a mod proposes for the player (section 2 of the design): what it is, its arc, how strong, whether it is hostile to the
/// player, when it may happen, for how long, how much it concerns the player, and why. Plain values only, so any mod can build one.
/// </summary>
/// <param name="Id">Unique per proposing mod (the mod's own key).</param>
/// <param name="Source">The proposing mod's id (its manifest id, e.g. <c>author.mymod</c>).</param>
/// <param name="Kind">The beat's kind (e.g. <c>siege.invest</c>, <c>refugees</c>, <c>hunt</c>): repeats of a kind are spaced.</param>
/// <param name="Arc">The story it belongs to (e.g. <c>siege:stack:12</c>): the next phase of an arc near the player goes first.</param>
/// <param name="Intensity">0 to 100.</param>
/// <param name="Hostile">Aimed at the player: it also needs the hostile slot (Rivalry Core, or the Composer's own).</param>
/// <param name="Earliest">The window's start (game hours).</param>
/// <param name="Latest">The window's end (game hours): unshown by then, it reaches the player as news instead.</param>
/// <param name="DurationHours">How long it is active once shown.</param>
/// <param name="Closeness">0 (beyond about two days' walk) to 1 (at the player's town).</param>
/// <param name="Ties">How many of the player's ties it touches (their outpost, a faction's regard, people they know, their rival).</param>
/// <param name="Reason">Why it happens (a reason key the chronicle can name).</param>
public sealed record Beat(string Id, string Source, string Kind, string Arc, double Intensity, bool Hostile, double Earliest, double Latest,
    double DurationHours, double Closeness, int Ties, string Reason);

/// <summary>The Composer's answer to a proposal.</summary>
public enum Answer { Go, Wait, No }

/// <summary>An answer, why, and for a wait, the earliest hour worth asking again.</summary>
public sealed record Verdict(Answer Answer, string Why, double RetryAt = double.NaN)
{
    public static Verdict Go(string why) => new(Answer.Go, why);
    public static Verdict No(string why) => new(Answer.No, why);
    public static Verdict Wait(string why, double at) => new(Answer.Wait, why, at);
}

/// <summary>Whether a hostile beat may go now (Rivalry Core's one-hostile-party slot, when it is there).</summary>
public interface IHostileGate
{
    bool MaySend(Beat beat, double now);

    void Started(Beat beat, double now);

    void Ended(Beat beat, double now);
}
