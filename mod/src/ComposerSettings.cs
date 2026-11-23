// PZA's Composer for Meitou. Author: PZA. Licence: GPLv3 or later.
using Meitou.Modding;

namespace PZA.Composer.Meitou;

/// <summary>
/// The Settings > Mods group: the Composer on or off, and the drama setting. Drama is a ready-made preset (design Part 4, section 20); there
/// are no sliders. A drama change takes effect at once, and the curve goes on from where it is.
/// </summary>
public sealed class ComposerSettings
{
    /// <summary>Off: every proposal is a go, the way the mods behave without the Composer.</summary>
    [Setting("Pace the world's stories", Hint = "The war and other mods' moments near you come in a build-up, a peak and a quiet spell, instead of all at once.")]
    public bool Enabled { get; set; } = true;

    /// <summary>calm / normal / harsh (the user's default: normal).</summary>
    [Setting("Drama", Hint = "Calm: long quiet spells, smaller peaks (about one every 10 days). Normal: a peak about every 6 days. Harsh: about every 3 days, and bigger.",
        Options = ["calm", "normal", "harsh"])]
    public string Drama { get; set; } = "normal";

    public PZA.Composer.Drama DramaSetting => Drama switch
    {
        "calm" => PZA.Composer.Drama.Calm,
        "harsh" => PZA.Composer.Drama.Harsh,
        _ => PZA.Composer.Drama.Normal,
    };
}
