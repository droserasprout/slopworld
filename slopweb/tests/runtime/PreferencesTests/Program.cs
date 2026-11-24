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
        JobsPatchFixture.Check();
        ScalePatchFixture.Check();
        EcoPrefixFixture.Check();
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
            radioMute = true,
            radioStopOnExit = false,
            displayMode = FramePolicy.Limit,
            foregroundFps = 144,
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
        Expect(!restored.radioStopOnExit, "Stop on exit did not survive reload");
        Expect(
            restored.displayMode == FramePolicy.Limit && restored.foregroundFps == 144,
            "Explicit frame limit did not survive reload"
        );
        Expect(restored.radioMute, "Paused soundtrack preference did not survive reload");
        BrowserPreferences.Apply(restored, "{\"radioMute\":false}");
        Expect(!restored.radioMute, "Resumed soundtrack preference did not survive reload");
        Settings.S = restored;
        // Exercise the real startup export with every preference emitted by the
        // persistence owner, plus host metadata and an unrelated field.
        var imported = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(saved)!;
        imported["hostHome"] = JsonSerializer.SerializeToElement("/home/example");
        imported["commandPaletteHistory"] = JsonSerializer.SerializeToElement("private-command");
        new WorkspaceRuntime().WorkspaceAppearance(JsonSerializer.Serialize(imported));
        Expect(BrowserSidebar.HostHome == "/home/example", "Host display home was not imported");
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
        ModSettings.Changed = BrowserPreferences.Changed;
        settings.MarkDirty();
        settings.MarkDirty();
        settings.uiScheme = "other";
        BrowserPreferences.Changed(settings);
        Expect(fontChanges == 1, "Unrelated settings repeated font work");
        settings.fontSize++;
        BrowserPreferences.Changed(settings);
        Expect(fontChanges == 2, "Dirty font change was not published before persistence");
        BrowserPreferences.Fonts = null;
        ModSettings.Changed = null;

        Console.WriteLine(
            "Preferences: allowlist round trip, malformed input, native debounce and change notification passed."
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
            var application = new TypeDefinition(
                "UnityEngine",
                "Application",
                TypeAttributes.Public,
                module.TypeSystem.Object
            );
            module.Types.Add(application);
            var open = new MethodDefinition(
                "OpenURL",
                MethodAttributes.Public | MethodAttributes.Static,
                module.TypeSystem.Void
            );
            open.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            open.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            application.Methods.Add(open);
            using var adapters = AssemblyDefinition.ReadAssembly(
                Path.Combine(root, "Adapters.dll")
            );
            // The first game redirect is valid, but the next required type is
            // absent. The offline owner must leave the entire original DLL intact.
            var game = Path.Combine(root, "game");
            Directory.CreateDirectory(game);
            var gameDll = Path.Combine(game, "UnityStubs.dll");
            mod.Write(gameDll);
            var original = File.ReadAllBytes(gameDll);
            using (var resolver = new DefaultAssemblyResolver())
            {
                try
                {
                    BrowserGuiPatch.Apply(root, resolver);
                    throw new Exception("Partial GUI adaptation accepted");
                }
                catch (InvalidOperationException error)
                    when (error.Message.Contains("UnityEngine.GUIUtility", StringComparison.Ordinal)
                    ) { }
            }
            Expect(
                original.SequenceEqual(File.ReadAllBytes(gameDll)),
                "Failed adaptation published a partial assembly"
            );
            OfflineRedirect.Redirect(
                application,
                adapters.MainModule.GetType("BrowserAdapters.BrowserWindow"),
                "OpenURL",
                "OpenUrl",
                "System.Void",
                "System.String"
            );
            Expect(
                open.Body.Instructions[1].Operand is MethodReference urlCall
                    && urlCall.DeclaringType.FullName == "BrowserAdapters.BrowserWindow"
                    && urlCall.Name == "OpenUrl",
                "Unity URL intent not redirected"
            );
            string? requestedUrl = null;
            BrowserWindow.UrlRequest = value => requestedUrl = value;
            BrowserWindow.OpenUrl("https://example.com/path");
            Expect(requestedUrl == "https://example.com/path", "URL request lost");
            BrowserWindow.UrlRequest = null;
            BrowserWindow.OpenUrl("https://example.com/closed");
            bool Rejected()
            {
                try
                {
                    OfflineRedirect.Redirect(
                        application,
                        adapters.MainModule.GetType("BrowserAdapters.BrowserWindow"),
                        "OpenURL",
                        "OpenUrl",
                        "System.Void",
                        "System.String"
                    );
                    return false;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }
            Expect(Rejected(), "Duplicate game redirect accepted");
            open.Parameters.Clear();
            Expect(Rejected(), "Changed game URL signature accepted");
            bool? requested = null;
            BrowserWindow.Request = value => requested = value;
            BrowserWindow.Set(true);
            Expect(requested == true, "Fullscreen request lost");
            BrowserWindow.Set(false);
            Expect(requested == false, "Exit request lost");
            BrowserWindow.Request = null;
            BrowserWindow.Set(true);
            Console.WriteLine("Window: validated game URL redirection and intent delivery passed.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
