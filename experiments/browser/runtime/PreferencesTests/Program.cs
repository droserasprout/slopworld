using System.Text.Json;
using BrowserAdapters;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SlopWorld;

static class Check
{
    static void Expect(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    static void Main()
    {
        string saved = "";
        BrowserPreferences.Persist = json => saved = json;
        var settings = new ModSettings
        {
            uiScheme = "slopworld-cool",
            fontSize = 19,
            sidebarHidden = true,
            autoConnect = false,
            commandPaletteHistory = "secret-command",
            radio = "secret-station",
            stopSessionsOnExit = true,
        };
        BrowserPreferences.Write(settings);
        using (var snapshot = JsonDocument.Parse(saved))
        {
            foreach (
                var name in new[]
                {
                    "autoConnect",
                    "commandPaletteHistory",
                    "radio",
                    "stopSessionsOnExit",
                    "endpoint",
                    "token",
                    "hostHome",
                }
            )
                Expect(!snapshot.RootElement.TryGetProperty(name, out _), "Leaked " + name);
        }
        var restored = new ModSettings();
        BrowserPreferences.Apply(restored, saved);
        Expect(
            restored.uiScheme == settings.uiScheme
                && restored.fontSize == 19
                && restored.sidebarHidden,
            "Presentation did not round trip"
        );
        Expect(
            restored.autoConnect
                && restored.commandPaletteHistory == ""
                && !restored.stopSessionsOnExit,
            "Non-presentation settings changed"
        );
        BrowserPreferences.Apply(
            restored,
            """{"uiScheme":"custom","token":"secret","autoConnect":false,"commandPaletteHistory":"do-it","fontSize":"large","uiFontSize":999,"sidebarWidth":1e99,"sidebarFilesOpenFraction":-1,"cursorGrayscale":null} """
        );
        Expect(
            restored.uiScheme == "custom"
                && restored.fontSize == 19
                && restored.uiFontSize == 14
                && restored.sidebarWidth == 210
                && restored.sidebarFilesOpenFraction == .25f
                && restored.cursorGrayscale,
            "Malformed scalar changed settings"
        );
        Expect(
            restored.autoConnect && restored.commandPaletteHistory == "",
            "Imported forbidden preferences"
        );
        foreach (var invalid in new[] { "null", "[]", "{", "false", new string('x', 131073) })
            BrowserPreferences.Apply(restored, invalid);
        Expect(restored.uiScheme == "custom", "Malformed snapshot discarded valid state");
        int writes = 0;
        settings.MarkDirty();
        for (int frame = 0; frame < 119; frame++)
            settings.FlushIfDue(_ => writes++);
        Expect(writes == 0, "Native debounce changed");
        settings.FlushIfDue(_ =>
        {
            writes++;
            BrowserPreferences.Write(settings);
        });
        settings.FlushIfDue(_ => writes++);
        Expect(writes == 1, "Native dirty save did not flush once");
        CheckPatch();
        Console.WriteLine(
            "Preferences: allowlist round trip, malformed input, native debounce and staged save sink passed."
        );
    }

    static void CheckPatch()
    {
        string root = Path.Combine(Path.GetTempPath(), "slopworld-preferences-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.Copy(
                typeof(BrowserPreferences).Assembly.Location,
                Path.Combine(root, "Adapters.dll")
            );
            using var mod = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("TestMod", new Version(1, 0)),
                "TestMod",
                ModuleKind.Dll
            );
            var module = mod.MainModule;
            var settings = new TypeDefinition(
                "SlopWorld",
                "ModSettings",
                TypeAttributes.Public,
                module.TypeSystem.Object
            );
            module.Types.Add(settings);
            var profile = new TypeDefinition(
                "SlopWorld",
                "ProfileSettings",
                TypeAttributes.Public,
                module.TypeSystem.Object
            );
            module.Types.Add(profile);
            var write = new MethodDefinition(
                "Write",
                MethodAttributes.Public | MethodAttributes.Static,
                module.TypeSystem.Void
            );
            write.Parameters.Add(new ParameterDefinition(settings));
            write.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            profile.Methods.Add(write);
            BrowserPreferencesPatch.Apply(mod, root);
            Expect(
                write.Body.Instructions.Count == 3
                    && write.Body.Instructions[0].OpCode == OpCodes.Ldarg_0
                    && write.Body.Instructions[1].Operand is MethodReference call
                    && call.DeclaringType.FullName == "BrowserAdapters.BrowserPreferences"
                    && call.Name == "Write",
                "Native save was not redirected"
            );
            write.Parameters.Clear();
            bool rejected = false;
            try
            {
                BrowserPreferencesPatch.Apply(mod, root);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            Expect(rejected, "Changed save signature was accepted");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
