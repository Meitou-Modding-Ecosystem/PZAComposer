// PZA's Composer for Meitou. Author: PZA. Licence: GPLv3 or later.
using Meitou.Modding;
using Meitou.Simulation;

namespace PZA.Composer.Meitou;

/// <summary>
/// The Composer's world system, one per game world (each new game and load builds it afresh).
/// <list type="bullet">
/// <item>It holds the core (<see cref="Composer"/>) and keeps it in the save.</item>
/// <item>It moves it on the game's clock.</item>
/// <item>It applies the drama setting.</item>
/// <item>It writes every decision to the log: a go and a no always; a wait the first time for each beat; a summary once a game day.</item>
/// </list>
/// The curve starts at the world's first game hour the Composer sees, so a new game opens with a build-up.
/// </summary>
public sealed class ComposerSystem : ITickSystem, IModSaveData, IStateHashed
{
    public const int MaxShown = 300;

    readonly ComposerSettings _settings;
    readonly Action<string> _log;
    readonly List<Dictionary<string, object>> _shown = [];
    readonly HashSet<string> _waitSaid = new(StringComparer.Ordinal);
    long _seq;
    byte[]? _pending;
    int _summaryDay = -1;
    double _now = double.NaN;

    public ComposerSystem(ComposerSettings settings, Action<string> log)
    {
        _settings = settings;
        _log = log;
        ComposerApi.NewWorld(this);
    }

    /// <summary>The core, once this world's first hour is known (null before).</summary>
    public Composer? Core { get; private set; }

    public bool On => _settings.Enabled;

    public DramaSettings Settings => Core?.Settings ?? DramaSettings.For(_settings.DramaSetting);

    public void SlowWorld(World world)
    {
        if (!On) return;
        double now = world.GameHours;
        var c = Ensure(now);
        _now = now;
        if (c.Settings.Drama != _settings.DramaSetting)
        {
            c.SetDrama(DramaSettings.For(_settings.DramaSetting));
            _log($"drama set to {_settings.Drama}: the curve goes on from where it is");
        }

        c.Step(now);
        int day = (int)Math.Floor(now / 24);
        if (day != _summaryDay)
        {
            if (_summaryDay >= 0) _log($"day {day}: {c.Summary(now)}");
            _summaryDay = day;
            _waitSaid.Clear();
        }
    }

    Composer Ensure(double now)
    {
        if (Core != null) return Core;
        var c = new Composer(DramaSettings.For(_settings.DramaSetting), double.IsFinite(now) ? now : 0);
        if (_pending != null)
        {
            try
            {
                c.Read(new MemoryStream(_pending));
                c.SetDrama(DramaSettings.For(_settings.DramaSetting));     // the player's setting wins over the saved one
                _log($"loaded: {c.Summary(now)}");
            }
            catch (InvalidDataException e)
            {
                _log($"the save's Composer section could not be read ({e.Message}): the curve starts again with a build-up");
                c = new Composer(DramaSettings.For(_settings.DramaSetting), double.IsFinite(now) ? now : 0);
            }

            _pending = null;
        }
        else _log($"a new curve ({_settings.Drama}): build-up from hour {ComposerApi.F(c.Origin)}");

        Core = c;
        return c;
    }

    internal Verdict Propose(Beat beat, double now)
    {
        var c = Ensure(now);
        var v = c.Propose(beat, now);
        string who = $"{beat.Source}/{beat.Id} ({beat.Kind}, arc {beat.Arc}, {ComposerApi.F(beat.Intensity)})";
        switch (v.Answer)
        {
            case Answer.Go:
                _shown.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["Seq"] = ++_seq, ["Hours"] = now, ["Source"] = beat.Source, ["Id"] = beat.Id, ["Kind"] = beat.Kind, ["Arc"] = beat.Arc,
                    ["Intensity"] = beat.Intensity, ["Peak"] = c.IsPeak(beat), ["Phase"] = c.Phase(now).Phase.ToString().ToLowerInvariant(),
                    ["Reason"] = beat.Reason,
                });
                if (_shown.Count > MaxShown) _shown.RemoveAt(0);
                _waitSaid.Remove(beat.Source + "/" + beat.Id);
                _log($"go: {who}: {v.Why}; tension now {ComposerApi.F(c.Tension(now))}");
                break;
            case Answer.No:
                _log($"no: {who}: {v.Why}");
                break;
            default:
                if (_waitSaid.Add(beat.Source + "/" + beat.Id)) _log($"wait: {who}: {v.Why} (until {ComposerApi.F(v.RetryAt)})");
                break;
        }

        return v;
    }

    internal bool End(string source, string id, double now)
    {
        if (Core is not { } c || !c.ActiveBeats.Any(b => b.Source == source && b.Id == id)) return false;
        c.Ended(source, id, now);
        _log($"ended: {source}/{id}");
        return true;
    }

    internal IReadOnlyList<IReadOnlyDictionary<string, object>> ShownSince(long seq) =>
        [.. _shown.Where(r => (long)r["Seq"] > seq).Select(r => (IReadOnlyDictionary<string, object>)new Dictionary<string, object>(r, StringComparer.Ordinal))];

    public void Hash(ref StateHasher hasher)
    {
        if (Core is not { } c) return;
        hasher.Add(BitConverter.DoubleToInt64Bits(c.Origin));
        hasher.Add(c.ActiveBeats.Count);
        if (double.IsFinite(_now)) hasher.Add(BitConverter.DoubleToInt64Bits(c.Tension(_now)));
    }

    /// <summary>The core; a save made before the loaded section was read writes that section back as it came.</summary>
    public void Write(Stream stream)
    {
        if (Core != null) Core.Write(stream);
        else if (_pending != null) stream.Write(_pending);
    }

    /// <summary>Kept until the first hour is known; then read into the core (a damaged section starts a new curve, with a line).</summary>
    public void Read(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _pending = ms.Length == 0 ? null : ms.ToArray();
    }
}
