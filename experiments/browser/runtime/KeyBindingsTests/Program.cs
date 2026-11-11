using System.Text.Json;
using BrowserAdapters;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEngine;
using Verse;

static class Check
{
    static void Expect(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    static void Main(string[] args)
    {
        // Exercise installed managed binding operations without engine startup,
        // KeyPrefs.Init, file writes, game/host profile reads, or native rendering.
        var own = new KeyBindingDef { defName = "SlopTest", defaultKeyCodeA = KeyCode.F9 };
        var host = new KeyBindingDef { defName = "HostSecret", defaultKeyCodeA = KeyCode.T };
        KeyPrefs.KeyPrefsData = new KeyPrefsData();
        KeyPrefs.KeyPrefsData.keyPrefs.Add(own, new KeyBindingData(KeyCode.F9, KeyCode.None));
        KeyPrefs.KeyPrefsData.keyPrefs.Add(host, new KeyBindingData(KeyCode.T, KeyCode.None));
        int prematureSaves = 0;
        BrowserKeyBindings.Persist = _ => prematureSaves++;
        BrowserKeyBindings.Apply(
            """{"SlopTest":{"a":"G","b":"F8"},"HostSecret":{"a":"X"},"Unknown":{"a":"F1"}}"""
        );
        BrowserKeyBindings.Save();
        Expect(prematureSaves == 0, "Initial native Save must not replace pending storage");
        Expect(
            KeyPrefs.KeyPrefsData.GetBoundKeyCode(own, KeyPrefs.BindingSlot.A) == KeyCode.F9,
            "Restore must wait for installed definitions and native defaults"
        );
        BrowserKeyBindings.Configure([own]);
        Expect(
            KeyPrefs.KeyPrefsData.GetBoundKeyCode(own, KeyPrefs.BindingSlot.A) == KeyCode.G
                && KeyPrefs.KeyPrefsData.GetBoundKeyCode(own, KeyPrefs.BindingSlot.B) == KeyCode.F8,
            "Both native slots must restore"
        );
        Expect(
            KeyPrefs.KeyPrefsData.GetBoundKeyCode(host, KeyPrefs.BindingSlot.A) == KeyCode.T,
            "Non-authored bindings changed"
        );
        BrowserKeyBindings.Apply("""{"SlopTest":{"a":"None","b":"not-a-key"}}""");
        Expect(
            KeyPrefs.KeyPrefsData.GetBoundKeyCode(own, KeyPrefs.BindingSlot.A) == KeyCode.None
                && KeyPrefs.KeyPrefsData.GetBoundKeyCode(own, KeyPrefs.BindingSlot.B) == KeyCode.F8,
            "Explicit clear or invalid-slot handling changed"
        );
        foreach (var invalid in new[] { "999999", "-1", "103", "g", "G, T", "Unknown" })
            BrowserKeyBindings.Apply(
                JsonSerializer.Serialize(new { SlopTest = new { a = invalid } })
            );
        foreach (
            var invalid in new[]
            {
                "null",
                "[]",
                "{",
                "false",
                new string('x', 131073),
                """{"SlopTest":[]} """,
                """{"SlopTest":{"a":103,"b":null}}""",
            }
        )
            BrowserKeyBindings.Apply(invalid);
        Expect(
            KeyPrefs.KeyPrefsData.GetBoundKeyCode(own, KeyPrefs.BindingSlot.A) == KeyCode.None
                && KeyPrefs.KeyPrefsData.GetBoundKeyCode(own, KeyPrefs.BindingSlot.B) == KeyCode.F8,
            "Malformed values changed slots"
        );
        string saved = "";
        BrowserKeyBindings.Persist = json => saved = json;
        BrowserKeyBindings.Save();
        using (var document = JsonDocument.Parse(saved))
        {
            Expect(document.RootElement.EnumerateObject().Count() == 1, "Leaked non-authored data");
            Expect(
                document.RootElement.GetProperty("SlopTest").GetProperty("a").GetString() == "None",
                "Cleared slot not serialized"
            );
        }
        // ResetToDefaults uses DefDatabase; keep only this tiny authored fixture.
        DefDatabase<KeyBindingDef>.Add(own);
        KeyPrefs.KeyPrefsData.ResetToDefaults();
        BrowserKeyBindings.Save();
        KeyPrefs.KeyPrefsData.SetBinding(own, KeyPrefs.BindingSlot.A, KeyCode.G);
        BrowserKeyBindings.Apply(saved);
        Expect(
            KeyPrefs.KeyPrefsData.GetBoundKeyCode(own, KeyPrefs.BindingSlot.A) == KeyCode.F9
                && KeyPrefs.KeyPrefsData.GetBoundKeyCode(own, KeyPrefs.BindingSlot.B)
                    == KeyCode.None,
            "Native defaults must replace old customization on reload"
        );
        BrowserKeyBindings.Persist = null;
        DefDatabase<KeyBindingDef>.Clear();
        CheckFullscreenAdmission();
        CheckPatch(args[0]);
        Console.WriteLine(
            "Key bindings: installed slots/reset, authored allowlist, malformed input, save hook passed."
        );
    }

    static void CheckFullscreenAdmission()
    {
        var definition = new KeyBindingDef
        {
            defName = "SlopToggleFullscreen",
            defaultKeyCodeA = KeyCode.F11,
        };
        KeyPrefs.KeyPrefsData = new KeyPrefsData();
        KeyPrefs.KeyPrefsData.keyPrefs.Add(
            definition,
            new KeyBindingData(KeyCode.F11, KeyCode.None)
        );
        BrowserKeyBindings.Configure([definition]);
        Expect(BrowserKeyBindings.FullscreenKey("F11"), "Default fullscreen admission lost");
        Expect(
            !BrowserKeyBindings.FullscreenKey("A")
                && !BrowserKeyBindings.FullscreenKey("None")
                && !BrowserKeyBindings.FullscreenKey("unknown"),
            "Ordinary text or empty binding admitted"
        );
        KeyPrefs.KeyPrefsData.SetBinding(definition, KeyPrefs.BindingSlot.A, KeyCode.G);
        KeyPrefs.KeyPrefsData.SetBinding(definition, KeyPrefs.BindingSlot.B, KeyCode.F8);
        Expect(
            BrowserKeyBindings.FullscreenKey("G")
                && BrowserKeyBindings.FullscreenKey("F8")
                && !BrowserKeyBindings.FullscreenKey("F11"),
            "Live rebound slots require a save/reload"
        );
        KeyPrefs.KeyPrefsData.SetBinding(definition, KeyPrefs.BindingSlot.A, KeyCode.None);
        Expect(
            !BrowserKeyBindings.FullscreenKey("G") && BrowserKeyBindings.FullscreenKey("F8"),
            "Cleared slot remained active"
        );
        DefDatabase<KeyBindingDef>.Add(definition);
        KeyPrefs.KeyPrefsData.ResetToDefaults();
        Expect(
            BrowserKeyBindings.FullscreenKey("F11") && !BrowserKeyBindings.FullscreenKey("F8"),
            "Reset admission stayed stale"
        );
        DefDatabase<KeyBindingDef>.Clear();
        BrowserKeyBindings.Configure([]);
        Expect(!BrowserKeyBindings.FullscreenKey("F11"), "Unconfigured definition admitted");
    }

    static void CheckPatch(string gamePath)
    {
        string root = Path.Combine(Path.GetTempPath(), "slopworld-keys-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.Copy(
                typeof(BrowserKeyBindings).Assembly.Location,
                Path.Combine(root, "Adapters.dll")
            );
            using var game = AssemblyDefinition.ReadAssembly(
                Path.Combine(gamePath, "Assembly-CSharp.dll")
            );
            var save = game
                .MainModule.GetType("Verse.KeyPrefs")
                .Methods.Single(m => m.Name == "Save");
            int count = save.Body.Instructions.Count;
            var returns = save.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray();
            BrowserKeyBindingsPatch.Apply(game, root);
            Expect(
                save.Body.Instructions.Count == count + returns.Length
                    && returns.All(i =>
                        i.OpCode == OpCodes.Call
                        && i.Operand is MethodReference m
                        && m.DeclaringType.FullName == "BrowserAdapters.BrowserKeyBindings"
                        && i.Next.OpCode == OpCodes.Ret
                    ),
                "Native return targets must execute save sink"
            );
            Expect(
                save.Body.Instructions.Any(i =>
                    i.Operand is MethodReference m
                    && m.DeclaringType.FullName == "System.Xml.Linq.XDocument"
                    && m.Name == "Save"
                ),
                "Native XML save must remain"
            );
            bool rejected = false;
            try
            {
                BrowserKeyBindingsPatch.Apply(game, root);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            Expect(rejected, "Duplicate staging must fail");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
