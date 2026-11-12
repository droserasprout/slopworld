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
        SteamCallbackFixture.Check();
        CheckWindowPatch();
        string saved = "";
        BrowserPreferences.Persist = json => saved = json;
        var settings = new ModSettings
        {
            uiScheme = "slopworld-cool",
            fontSize = 19,
            sidebarHidden = true,
            sidebarShowHidden = true,
            sidebarShowGitignored = true,
            cursorColor = "#abcdef",
            smoothScrolling = false,
            statusbarUsage = false,
            statusbarSummaryPosition = StatusbarSummaryMode.Center,
            statusbarClockPosition = StatusbarClockMode.Hidden,
            statusbarPlanner = false,
            statusbarJukebox = false,
            statusbarGM = false,
            statusbarAgentIndicators = false,
            timeZone = "UTC",
            timeFormat = TimeFormat.TwelveHour,
            dateFormat = DateFormat.DayFirst,
            ecoDim = 0.65f,
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
        AppearanceSettings.S = restored;
        // Exercise the real startup export with every preference emitted by the
        // persistence owner, plus host metadata and an unrelated field.
        var imported = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(saved)!;
        imported["hostHome"] = JsonSerializer.SerializeToElement("/home/example");
        imported["commandPaletteHistory"] = JsonSerializer.SerializeToElement("private-command");
        Probe.WorkspaceAppearance(JsonSerializer.Serialize(imported));
        Expect(Probe.HostHome == "/home/example", "Host display home was not imported");
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
        Expect(
            restored.sidebarShowHidden
                && restored.sidebarShowGitignored
                && restored.cursorColor == "#abcdef"
                && !restored.smoothScrolling
                && !restored.statusbarUsage
                && restored.statusbarSummaryPosition == StatusbarSummaryMode.Center
                && restored.statusbarClockPosition == StatusbarClockMode.Hidden
                && !restored.statusbarPlanner
                && !restored.statusbarJukebox
                && !restored.statusbarGM
                && !restored.statusbarAgentIndicators
                && restored.timeZone == "UTC"
                && restored.timeFormat == TimeFormat.TwelveHour
                && restored.dateFormat == DateFormat.DayFirst
                && BrowserPreferences.BackgroundDimming(restored) == 0.65f,
            "Extended presentation preferences did not round trip"
        );
        foreach (string invalid in new[] { "-0.1", "0.81", "1e99", "null", "\"dark\"" })
        {
            BrowserPreferences.Apply(restored, "{\"ecoDim\":" + invalid + "}");
            Expect(restored.ecoDim == 0.65f, "Malformed dimming changed the backdrop");
        }
        foreach (float dim in new[] { 0f, 0.8f })
        {
            settings.ecoDim = dim;
            BrowserPreferences.Write(settings);
            BrowserPreferences.Apply(restored, saved);
            Expect(BrowserPreferences.BackgroundDimming(restored) == dim, "Dimming limit lost");
        }
        restored.ecoDim = float.NaN;
        Expect(BrowserPreferences.BackgroundDimming(restored) == 0.3f, "Invalid dimming escaped");
        restored.ecoDim = 10;
        Expect(BrowserPreferences.BackgroundDimming(restored) == 0.8f, "Dimming was not bounded");
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
        int fontChanges = 0;
        BrowserPreferences.Fonts = _ => fontChanges++;
        BrowserPreferences.Changed(settings);
        BrowserPreferences.Changed(settings);
        settings.uiScheme = "other";
        BrowserPreferences.Changed(settings);
        Expect(fontChanges == 1, "Unrelated settings repeated font work");
        settings.fontSize++;
        BrowserPreferences.Changed(settings);
        Expect(fontChanges == 2, "Dirty font change was not published before persistence");
        BrowserPreferences.Fonts = null;
        CheckPatch();
        Console.WriteLine(
            "Preferences: allowlist round trip, malformed input, native debounce and staged save sink passed."
        );
    }

    static void CheckWindowPatch()
    {
        string root = Path.Combine(Path.GetTempPath(), "slopworld-window-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.Copy(typeof(BrowserWindow).Assembly.Location, Path.Combine(root, "Adapters.dll"));
            using var mod = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("TestMod", new Version(1, 0)),
                "TestMod",
                ModuleKind.Dll
            );
            var module = mod.MainModule;
            var capability = new TypeDefinition(
                "SlopWorld",
                "ClientPlatform",
                TypeAttributes.Public,
                module.TypeSystem.Object
            );
            module.Types.Add(capability);
            var getter = new MethodDefinition(
                "get_IsBrowser",
                MethodAttributes.Public | MethodAttributes.Static,
                module.TypeSystem.Boolean
            );
            getter.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
            getter.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            capability.Methods.Add(getter);
            var window = new TypeDefinition(
                "SlopWorld",
                "LinuxGameWindow",
                TypeAttributes.Public,
                module.TypeSystem.Object
            );
            module.Types.Add(window);
            var setter = new MethodDefinition(
                "Set",
                MethodAttributes.Public | MethodAttributes.Static,
                module.TypeSystem.Void
            );
            setter.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
            setter.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            window.Methods.Add(setter);
            BrowserWindowPatch.Apply(mod, root);
            Expect(
                getter.Body.Instructions[0].OpCode == OpCodes.Ldc_I4_1,
                "Browser capability not enabled"
            );
            Expect(
                setter.Body.Instructions.Count == 3
                    && setter.Body.Instructions[0].OpCode == OpCodes.Ldarg
                    && setter.Body.Instructions[1].Operand is MethodReference call
                    && call.DeclaringType.FullName == "BrowserAdapters.BrowserWindow"
                    && call.Name == "Set",
                "Fullscreen intent not redirected"
            );
            setter.Parameters.Clear();
            bool rejected = false;
            try
            {
                BrowserWindowPatch.Apply(mod, root);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            Expect(rejected, "Changed window setter signature accepted");
            bool? requested = null;
            BrowserWindow.Request = value => requested = value;
            BrowserWindow.Set(true);
            Expect(requested == true, "Fullscreen request lost");
            BrowserWindow.Set(false);
            Expect(requested == false, "Exit request lost");
            BrowserWindow.Request = null;
            BrowserWindow.Set(true);
            Console.WriteLine(
                "Window: staged capability, validated setter redirection and intent delivery passed."
            );
        }
        finally
        {
            Directory.Delete(root, true);
        }
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
            var dirty = new MethodDefinition(
                "MarkDirty",
                MethodAttributes.Public,
                module.TypeSystem.Void
            );
            dirty.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            settings.Methods.Add(dirty);
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
            Expect(
                dirty.Body.Instructions.Count == 3
                    && dirty.Body.Instructions[1].Operand is MethodReference changed
                    && changed.Name == "Changed",
                "Dirty event did not notify font selection"
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
