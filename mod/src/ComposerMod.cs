// PZA's Composer for Meitou. Author: PZA. Licence: GPLv3 or later.
using System.Reflection;
using Meitou.Modding;
using Meitou.Simulation;

namespace PZA.Composer.Meitou;

/// <summary>The mod's entry (meitou.json "entry"): its settings and its world system. Other mods reach it through <see cref="ComposerApi"/>.</summary>
public sealed class ComposerMod : IMod
{
    public const string Id = "pza.composer";
    public const string SystemName = "composer";

    public void Initialize(ModContext context)
    {
        string version = typeof(ComposerMod).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        int plus = version.IndexOf('+');
        if (plus >= 0) version = version[..plus];
        var settings = new ComposerSettings();
        context.AddSettings(settings);
        context.Log($"v{version} - PZA's Composer for Meitou, by PZA: the world's stories near you in a build-up, a peak and a quiet spell "
                    + $"({(settings.Enabled ? settings.Drama : "off")}; ComposerApi v{ComposerApi.Version} for other mods)");
        context.AddSystem(SystemName, _ => new ComposerSystem(settings, context.Log), SystemAnchor.End);
    }
}
