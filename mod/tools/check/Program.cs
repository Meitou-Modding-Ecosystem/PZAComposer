// PZA's Composer for Meitou: offline check of the built mod (author PZA, GPLv3 or later). Never shipped. It does not start the game.
// 1. Meitou's own manifest reader and rule set (CodeModManifest.Read, CodeModChecks.Check), the entry type, the Settings page's validation.
// 2. ComposerApi found and called by reflection exactly as a client mod does it: no Composer -> go; a peak waits in
//    the build-up and goes in the peak slot; Shown lists it; Tension / Phase / NextQuiet read; the drama setting; off -> every proposal a go;
//    the save round trip through a second world's system, and a damaged section starting a new curve.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Meitou.Content;
using PZA.Composer.Meitou;

static class P
{
    static int s_bad;
    static readonly List<string> s_log = [];

    static int Main(string[] a)
    {
        var m = Manifest(a[0]);
        Settings(m);
        Api();
        Console.WriteLine(s_bad == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({s_bad})");
        return s_bad == 0 ? 0 : 1;
    }

    static void Expect(bool ok, string what)
    {
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok) s_bad++;
    }

    static CodeModManifest Manifest(string dir)
    {
        Console.WriteLine("== manifest");
        var m = CodeModManifest.Read(Path.Combine(dir, "meitou.json"));
        Console.WriteLine($"manifest: id={m.Id} api={m.ApiVersion} version={m.Version} entry={m.Entry} gameVersion={m.GameVersion}");
        Expect(CodeModManifest.IsValidId(m.Id) && m.Id == ComposerMod.Id, "manifest id is valid and matches ComposerMod.Id");
        foreach (var (ver, expectOk) in new[] { ("0.20.1", true), ("0.21.0", true), ("0.22.1", true), ("0.22.2", true), ("0.30.0", true), ("0.20.0", false) })
        {
            CodeModVersion.TryParse(ver, out var g);
            var problems = CodeModChecks.Check(new List<CodeModEntry> { new("PZAComposer", m, true) }, g);
            Expect((problems.Count == 0) == expectOk, $"CodeModChecks.Check against game {ver}: {(problems.Count == 0 ? "no problems" : string.Join("; ", problems.SelectMany(p => p.Value)))}");
        }
        foreach (var dll in m.Assemblies) Expect(File.Exists(Path.Combine(dir, dll)), $"staged {dll} present");
        Expect(File.Exists(Path.Combine(dir, "PZAComposer.mod")), "staged PZAComposer.mod present");
        var asm = typeof(ComposerMod).Assembly;
        Expect(File.ReadAllBytes(asm.Location).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(dir, m.Assemblies[0]))), "checked assembly is the staged one");
        var entry = asm.GetType(m.Entry);
        Expect(entry is not null && entry.IsPublic && typeof(Meitou.Modding.IMod).IsAssignableFrom(entry) && entry.GetConstructor(Type.EmptyTypes) is not null,
            $"entry {m.Entry} is a public IMod with a public parameterless constructor");
        Console.WriteLine("references: " + string.Join(", ", asm.GetReferencedAssemblies().Select(r => $"{r.Name} {r.Version}")));
        return m;
    }

    static void Settings(CodeModManifest m)
    {
        Console.WriteLine("== settings");
        var groupType = typeof(Meitou.Modding.IMod).Assembly.GetType("Meitou.Modding.ModSettingsGroup")!;
        var ctor = groupType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public).First(c => c.GetParameters().Length == 3);
        var s = new ComposerSettings();
        try
        {
            var g = (Meitou.Modding.ModSettingsGroup)ctor.Invoke(new object[] { m.Id, "PZAComposer", s });
            Expect(g.Settings.Count == 2, "ModSettingsGroup accepts the settings: " + string.Join(", ", g.Settings.Select(x => $"{x.Key}={x.Text}")));
            g.Settings.First(x => x.Key == "Drama").Set("harsh");
            Expect(s.DramaSetting == PZA.Composer.Drama.Harsh, "the page's Drama list sets harsh");
        }
        catch (TargetInvocationException e)
        {
            Expect(false, "ModSettingsGroup refused the settings: " + e.InnerException?.Message);
        }
    }

    // ---- the API by reflection, as a client sees it
    static Type? s_api;

    static object? Call(string name, params object[] args)
    {
        var m = s_api!.GetMethod(name, BindingFlags.Public | BindingFlags.Static, args.Select(x => x.GetType()).ToArray());
        if (m == null) { Expect(false, $"ComposerApi.{name}({string.Join(", ", args.Select(x => x.GetType().Name))}) found"); return null; }
        return m.Invoke(null, args);
    }

    static object? Get(string name) => s_api!.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

    static string Ask(string id, double intensity, double now, string arc = "", string kind = "") =>
        ((IReadOnlyDictionary<string, object>)Call("Propose", "check", id, kind.Length == 0 ? "k" + id : kind, arc.Length == 0 ? "a" + id : arc,
            intensity, false, now, now + 48.0, 24.0, 1.0, 0, "check", now)!)["Answer"].ToString()!;

    static void Api()
    {
        Console.WriteLine("== ComposerApi by reflection");
        s_api = Type.GetType("PZA.Composer.Meitou.ComposerApi, PZAComposer.Meitou");
        Expect(s_api != null, "Type.GetType(\"PZA.Composer.Meitou.ComposerApi, PZAComposer.Meitou\") finds it");
        if (s_api == null) return;
        Expect(s_api.GetField("Version")?.GetValue(null) is int v && v >= 1, "Version is an int >= 1");
        Expect(Get("Active") is false && Ask("x", 99, 0) == "go", "no Composer in the world: inactive, every proposal a go");

        var settings = new ComposerSettings();           // normal: build-up 0-48 h, peak slot 48-72 h, relief 72-144 h
        var sys = new ComposerSystem(settings, s_log.Add);
        Expect(Get("Active") is true && (string)Get("Drama")! == "normal" && (double)Get("Cap")! == 85, "a world's system: active, normal, cap 85");
        Expect(Ask("p", 70, 0) == "wait", "a peak (70 >= 63.75) proposed in the build-up waits (the curve starts at the first hour seen, 0)");
        Expect(s_log.Any(l => l.Contains("wait: check/p ")), "the log names the beat as source/id");
        Expect((string)Call("Phase", 10.0)! == "build" && (double)Call("NextQuiet", 10.0)! == 72, "Phase build, NextQuiet 72 h (the relief's start)");
        Expect(Ask("p", 70, 50) == "go", "the same peak goes in the peak slot");
        Expect(Ask("p2", 70, 52, kind: "other") == "wait", "a second peak in the same cycle waits");
        var shown = (IReadOnlyList<IReadOnlyDictionary<string, object>>)Call("Shown", 0L)!;
        Expect(shown.Count == 1 && (string)shown[0]["Id"] == "p" && (bool)shown[0]["Peak"] && (string)shown[0]["Phase"] == "peak", "Shown lists the peak once");
        double t = (double)Call("Tension", 51.0)!;
        Expect(t > 60 && t < 70, $"Tension at 51 h is the peak faded one hour ({t:0.0})");
        Expect(Call("Ended", "check", "p", 53.0) is true && Call("Ended", "check", "nope", 53.0) is false, "Ended: true for an active beat, false otherwise");

        // save round trip into a second world's system
        Expect(Ask("q", 20, 60) == "go", "a small beat goes in the peak slot");
        using var ms = new MemoryStream();
        sys.Write(ms);
        var sys2 = new ComposerSystem(new ComposerSettings(), s_log.Add);
        sys2.Read(new MemoryStream(ms.ToArray()));
        Expect(Ask("p3", 70, 62, kind: "third") == "wait", "after the load: still one peak per cycle (the saved cycle holds)");
        Expect(Math.Abs((double)Call("Tension", 61.0)! - 20 * Math.Pow(0.5, 1 / PZA.Composer.DramaSettings.For(PZA.Composer.Drama.Normal).HalfLifeHours)) < 1e-6, $"after the load: the small beat's tension, faded one hour ({(double)Call("Tension", 61.0)!:0.000} vs {20 * Math.Pow(0.5, 1 / PZA.Composer.DramaSettings.For(PZA.Composer.Drama.Normal).HalfLifeHours):0.000}; {Call("Summary", 61.0)})");
        var dmg = new ComposerSystem(new ComposerSettings(), s_log.Add);
        var bytes = ms.ToArray();
        dmg.Read(new MemoryStream(bytes[..^3]));
        Expect(Ask("z", 20, 5) == "go" && s_log.Any(l => l.Contains("could not be read")), "a damaged section: a line, and a new curve from the hour it was found");

        settings.Enabled = false;
        var off = new ComposerSystem(settings, s_log.Add);
        Expect(Get("Active") is false && Ask("w", 99, 0) == "go", "switched off: inactive, every proposal a go");
        Console.WriteLine("log lines: " + s_log.Count);
        foreach (var l in s_log.Take(8)) Console.WriteLine("  log: " + l);
    }
}
