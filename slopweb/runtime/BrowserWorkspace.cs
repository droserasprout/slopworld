using System.Collections;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;

public partial class Probe
{
    [JSExport]
    public static void WorkspaceRasterizer(
        [JSMarshalAs<JSType.Function<JSType.String, JSType.String>>] Func<string, string> rasterize
    ) =>
        Type("Adapters", "BrowserAdapters.BrowserFontBank")
            .GetField("Rasterize")!
            .SetValue(null, rasterize);

    [JSExport]
    public static void WorkspaceFontsDispose() =>
        Invoke("Adapters", "BrowserAdapters.BrowserFontBank", "DisposeFonts");

    [JSExport]
    public static bool WorkspaceObserveLabels(bool enabled) =>
        (bool)Invoke("Adapters", "BrowserAdapters.BrowserGui", "ConfigureObserveLabels", enabled)!;

    [JSExport]
    public static string WorkspaceLabels() =>
        (string)Invoke("Adapters", "BrowserAdapters.BrowserGui", "Labels")!;

    [JSExport]
    public static bool WorkspaceInputFlushBeforeRepaint(bool enabled) =>
        (bool)
            Invoke(
                "Adapters",
                "BrowserAdapters.WorkspaceDrawing",
                "ConfigureInputFlushBeforeRepaint",
                enabled
            )!;

    [JSExport]
    public static void WorkspaceWake([JSMarshalAs<JSType.Function>] Action wake) =>
        Type("Adapters", "BrowserAdapters.BrowserActivity").GetField("Wake")!.SetValue(null, wake);

    [JSExport]
    public static void WorkspaceClearWake() =>
        Type("Adapters", "BrowserAdapters.BrowserActivity").GetField("Wake")!.SetValue(null, null);

    [JSExport]
    public static bool WorkspacePacketCache(bool enabled) =>
        (bool)Invoke("Adapters", "BrowserAdapters.BrowserGui", "ConfigurePacketCache", enabled)!;

    [JSExport]
    public static bool WorkspaceCommandValueCache(bool enabled) =>
        (bool)
            Invoke(
                "Adapters",
                "BrowserAdapters.BrowserGui",
                "ConfigureCommandValueCache",
                enabled
            )!;

    [JSExport]
    public static bool WorkspaceQuadCache(bool enabled) =>
        (bool)Invoke("Adapters", "BrowserAdapters.BrowserGui", "ConfigureQuadCache", enabled)!;

    [JSExport]
    public static bool WorkspaceDiscardedDrawing(bool skip) =>
        (bool)
            Invoke(
                "Adapters",
                "BrowserAdapters.BrowserGuiInput",
                "ConfigureDiscardedDrawing",
                skip
            )!;

    [JSExport]
    public static string WorkspaceDynamicFonts(
        [JSMarshalAs<JSType.Function<JSType.String>>] Action<string> changed
    )
    {
        Type("Adapters", "BrowserAdapters.BrowserPreferences")
            .GetField("Fonts")!
            .SetValue(null, changed);
        var settings = Type("SlopWorld", "SlopWorld.Settings").GetProperty("S")!.GetValue(null)!;
        return (string)
            Invoke("Adapters", "BrowserAdapters.BrowserPreferences", "FontSelection", settings)!;
    }

    [JSExport]
    public static bool WorkspaceFontCapacity(int bytes) =>
        (bool)Invoke("Adapters", "BrowserAdapters.BrowserFontBank", "Capacity", bytes)!;

    [JSExport]
    public static void WorkspaceFontHold(string json)
    {
        var settings = Type("SlopWorld", "SlopWorld.Settings").GetProperty("S")!.GetValue(null)!;
        Invoke("Adapters", "BrowserAdapters.BrowserPreferences", "HoldFonts", settings, json);
        WorkspaceFontsReady();
    }

    [JSExport]
    public static void WorkspaceFontRestore(string json)
    {
        var settings = Type("SlopWorld", "SlopWorld.Settings").GetProperty("S")!.GetValue(null)!;
        Invoke("Adapters", "BrowserAdapters.BrowserPreferences", "Apply", settings, json);
        WorkspaceFontsReady();
        Invoke("Adapters", "BrowserAdapters.BrowserPreferences", "Write", settings);
    }

    [JSExport]
    public static void WorkspaceFontsReady()
    {
        Invoke("Adapters", "BrowserAdapters.BrowserFontBank", "RetryRasterization");
        Invoke("Adapters", "BrowserAdapters.BrowserGui", "FontsChanged");
        Invoke("SlopWorld", "SlopWorld.InstalledFonts", "Rescan");
        Invoke("SlopWorld", "SlopWorld.UiFont", "Apply");
        Invoke("SlopWorld", "SlopWorld.TerminalFont", "Invalidate");
        Invoke("Adapters", "BrowserAdapters.BrowserActivity", "Notify");
    }

    [JSExport]
    public static void WorkspaceFontCatalog(string json) =>
        Invoke("Adapters", "BrowserAdapters.BrowserFontBank", "InstallCatalog", json);

    [JSExport]
    public static void WorkspaceFontChains(string json) =>
        Invoke("Adapters", "BrowserAdapters.BrowserFontBank", "InstallChains", json);

    [JSExport]
    public static string WorkspaceFontSnapshot() =>
        (string)Invoke("Adapters", "BrowserAdapters.BrowserFontBank", "Snapshot")!;

    [JSExport]
    public static int WorkspaceFont(byte[] font, string bake, int defaultIndex) =>
        (int)
            Invoke(
                "Adapters",
                "BrowserAdapters.BrowserFontBank",
                "Install",
                font,
                bake,
                defaultIndex
            )!;

    [JSExport]
    public static string WorkspaceSidebarRows() =>
        (string)Invoke("Adapters", "BrowserAdapters.WorkspaceDrawing", "SidebarRows")!;

    [JSExport]
    public static string WorkspaceSessionOrder() =>
        JsonSerializer.Serialize(Invoke("SlopWorld", "SlopWorld.Sidebar", "Sessions"));

    // Select an existing stopped session locally for Entry drawing regression
    // checks. This never stops, restarts or sends input to a daemon session.
    [JSExport]
    public static string WorkspaceObserveStoppedSession()
    {
        Invoke("Adapters", "BrowserAdapters.WorkspaceDrawing", "RequireEntry");
        var hub = Type("SlopWorld", "SlopWorld.SessionHub").GetField("Instance")!.GetValue(null)!;
        var sessions = (IEnumerable)hub.GetType().GetProperty("Sessions")!.GetValue(hub)!;
        var stopped = sessions
            .Cast<object>()
            .FirstOrDefault(session =>
                (bool)session.GetType().GetProperty("Gone")!.GetValue(session)!
            );
        if (stopped == null)
            throw new InvalidOperationException(
                "Entry regression needs an existing stopped session"
            );
        string name = (string)stopped.GetType().GetField("Name")!.GetValue(stopped)!;
        Invoke("SlopWorld", "SlopWorld.WorkspaceWindow", "Open", name);
        return name;
    }

    [JSExport]
    public static void WorkspaceAppearance(string json)
    {
        // Only presentation preferences cross this boundary. Endpoint, credentials,
        // commands and the host profile file itself never enter the browser.
        string[] allowed =
        [
            "sidebarHidden",
            "sidebarWidth",
            "sidebarOpenFilesVisible",
            "sidebarFilesOpenFraction",
            "sidebarSide",
            "uiDensity",
            "foldedProjects",
            "sidebarTab",
            "sidebarAgentStatus",
            "sidebarFilter",
            "sidebarWorktrees",
            "fontSize",
            "fontName",
            "uiFontSize",
            "uiFontName",
            "uiScheme",
            "theme",
            "grandmaMode",
            "cursor",
            "cursorGrayscale",
            "visualEffects",
        ];
        var settings = Type("SlopWorld", "SlopWorld.Settings").GetProperty("S")!.GetValue(null)!;
        using var document = JsonDocument.Parse(json);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Name == "hostHome")
            {
                Invoke(
                    "Adapters",
                    "BrowserAdapters.BrowserSidebar",
                    "ConfigureHome",
                    property.Value.GetString()!
                );
                continue;
            }
            if (!allowed.Contains(property.Name))
                throw new InvalidOperationException("Unsupported browser appearance preference");
            var field = settings.GetType().GetField(property.Name)!;
            field.SetValue(
                settings,
                JsonSerializer.Deserialize(property.Value.GetRawText(), field.FieldType)
            );
        }
    }

    [JSExport]
    public static bool WorkspaceFullscreenKey(string key) =>
        (bool)Invoke("Adapters", "BrowserAdapters.BrowserKeyBindings", "FullscreenKey", key)!;

    [JSExport]
    public static void WorkspaceWindow(
        [JSMarshalAs<JSType.Function<JSType.Boolean>>] Action<bool> request
    ) =>
        Type("Adapters", "BrowserAdapters.BrowserWindow")
            .GetField("Request")!
            .SetValue(null, request);

    [JSExport]
    public static void WorkspaceFullscreenState(bool fullscreen) =>
        Invoke("Adapters", "BrowserAdapters.BrowserWindow", "UpdateState", fullscreen);

    [JSExport]
    public static void WorkspacePreferences(
        string json,
        [JSMarshalAs<JSType.Function<JSType.String>>] Action<string> persist
    )
    {
        var settings = Type("SlopWorld", "SlopWorld.Settings").GetProperty("S")!.GetValue(null)!;
        Invoke("Adapters", "BrowserAdapters.BrowserPreferences", "Apply", settings, json);
        Type("Adapters", "BrowserAdapters.BrowserPreferences")
            .GetField("Persist")!
            .SetValue(null, persist);
    }

    [JSExport]
    public static void WorkspaceKeyBindings(
        string json,
        [JSMarshalAs<JSType.Function<JSType.String>>] Action<string> persist
    )
    {
        Invoke("Adapters", "BrowserAdapters.BrowserKeyBindings", "Apply", json);
        Type("Adapters", "BrowserAdapters.BrowserKeyBindings")
            .GetField("Persist")!
            .SetValue(null, persist);
    }

    [JSExport]
    public static void WorkspaceSavePreferences()
    {
        var settings = Type("SlopWorld", "SlopWorld.Settings").GetProperty("S")!.GetValue(null)!;
        Invoke("Adapters", "BrowserAdapters.BrowserPreferences", "Write", settings);
    }

    [JSExport]
    public static string WorkspaceSelect(string name)
    {
        Invoke("Adapters", "BrowserAdapters.WorkspaceDrawing", "RequireEntry");
        var type = Type("SlopWorld", "SlopWorld.SessionHub");
        var hub = type.GetField("Instance")!.GetValue(null)!;
        bool exists = ((IEnumerable)type.GetProperty("Sessions")!.GetValue(hub)!)
            .Cast<object>()
            .Any(session =>
                (string)session.GetType().GetField("Name")!.GetValue(session)! == name
                && (bool)session.GetType().GetField("Alive")!.GetValue(session)!
            );
        if (!exists)
            throw new InvalidOperationException("Requested workspace session is not live");
        var window = Invoke("SlopWorld", "SlopWorld.WorkspaceWindow", "Open", name)!;
        return (string)Invoke("Adapters", "BrowserAdapters.WorkspaceDrawing", "Repaint", window)!;
    }

    [JSExport]
    public static void ConfigureWorkspaceViewport(int width, int height, float scale) =>
        Invoke(
            "Adapters",
            "BrowserAdapters.WorkspaceDrawing",
            "ConfigureViewport",
            width,
            height,
            scale
        );

    [JSExport]
    public static void WorkspaceProfile(bool enabled) =>
        Type("Adapters", "BrowserAdapters.BrowserGuiInput")
            .GetField("Profile")!
            .SetValue(null, enabled);

    [JSExport]
    public static string ClipboardTake() =>
        (string)Invoke("Adapters", "BrowserAdapters.BrowserClipboard", "Take")!;

    [JSExport]
    public static void ClipboardComplete(int id, string text, string error) =>
        Invoke("Adapters", "BrowserAdapters.BrowserClipboard", "Complete", id, text, error);

    [JSExport]
    public static void ClipboardCompleteImage(int id, string base64, string error) =>
        Invoke("Adapters", "BrowserAdapters.BrowserClipboard", "CompleteImage", id, base64, error);

    [JSExport]
    public static string WorkspaceState() =>
        (string)Invoke("Adapters", "BrowserAdapters.WorkspaceDrawing", "Snapshot")!;

    [JSExport]
    public static string WorkspaceAudio() =>
        (string)Invoke("Adapters", "BrowserAdapters.BrowserAudio", "Snapshot")!;

    [JSExport]
    public static void WorkspaceAudioPlaying(bool playing) =>
        Invoke("Adapters", "BrowserAdapters.BrowserAudio", "Playing", playing);

    [JSExport]
    public static void WorkspaceAudioEnded() =>
        Invoke("Adapters", "BrowserAdapters.BrowserAudio", "Ended");

    [JSExport]
    public static bool WorkspaceBackgroundVisible() =>
        (bool)
            Invoke("Adapters", "BrowserAdapters.BrowserUnderlyingWorkspace", "BackgroundVisible")!;

    [JSExport]
    public static float WorkspaceBackgroundDimming()
    {
        var settings = Type("SlopWorld", "SlopWorld.Settings").GetProperty("S")!.GetValue(null)!;
        return (float)
            Invoke(
                "Adapters",
                "BrowserAdapters.BrowserPreferences",
                "BackgroundDimming",
                settings
            )!;
    }

    // Observe the catalog without calling SidebarScopes.Update: diagnostics must
    // not accidentally reconcile the very stale state being investigated.
    [JSExport]
    public static string WorkspaceBrowseState()
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic;
        var hub = Type("SlopWorld", "SlopWorld.SessionHub").GetField("Instance")!.GetValue(null)!;
        var catalog = Type("SlopWorld", "SlopWorld.SidebarScopes")
            .GetField("Catalog", flags)!
            .GetValue(null)!;
        var type = catalog.GetType();
        var projects = (IList)hub.GetType().GetProperty("Projects")!.GetValue(hub)!;
        return JsonSerializer.Serialize(
            new
            {
                online = hub.GetType().GetProperty("Online")!.GetValue(hub),
                projects = projects.Count,
                projectRevision = hub.GetType().GetProperty("ProjectsRevision")!.GetValue(hub),
                catalogProjectRevision = type.GetField("_projects", flags)!.GetValue(catalog),
                catalogs = (
                    (IDictionary)type.GetField("_catalogs", flags)!.GetValue(catalog)!
                ).Count,
                scopes = ((IDictionary)type.GetField("_scopes", flags)!.GetValue(catalog)!).Count,
                updating = type.GetField("_updating", flags)!.GetValue(catalog),
                emptyReason = type.GetMethod("EmptyReason")!.Invoke(catalog, null),
            }
        );
    }

    // Read-only test observation of the actual mod receive buffer. Input checks
    // still go through DOM events, the native terminal controls, and host tmux.
    [JSExport]
    public static bool WorkspaceTerminalContains(string name, string expected)
    {
        var hub = Type("SlopWorld", "SlopWorld.SessionHub").GetField("Instance")!.GetValue(null)!;
        var screen = hub.GetType().GetMethod("Screen")!.Invoke(hub, new object[] { name });
        return screen != null
            && ((string[])screen.GetType().GetField("Lines")!.GetValue(screen)!).Any(line =>
                line.Contains(expected, StringComparison.Ordinal)
            );
    }

    [JSExport]
    public static int InstallModContent(byte[] archive) =>
        (int)Invoke("Adapters", "BrowserAdapters.BrowserModContent", "Install", archive)!;

    [JSExport]
    public static string WorkspaceUpdate(string batch, double elapsed)
    {
        if (entryEngine == null)
            throw new InvalidOperationException("Workspace engine is stopped");
        Invoke("Waylith", "Waylith.EngineApplication", "BeginFrame", 1f / 60, (float)elapsed);
        try
        {
            Invoke("Adapters", "BrowserAdapters.WorkspaceDrawing", "BeginFrame", elapsed);
            Invoke("Adapters", "BrowserAdapters.BrowserCursor", "Update", batch);
            Invoke(
                "SlopWorld",
                "SlopWorld.DaemonClient",
                "PumpCompletions",
                (Action<Exception>)(e => throw e)
            );
            var hub = Type("SlopWorld", "SlopWorld.SessionHub")
                .GetField("Instance")!
                .GetValue(null)!;
            hub.GetType().GetMethod("Update")!.Invoke(hub, null);
            Invoke("Adapters", "BrowserAdapters.WorkspaceDrawing", "Update");
            var frame = (string)
                Invoke("Adapters", "BrowserAdapters.WorkspaceDrawing", "Frame", batch)!;
            // Full ModBootstrap is absent in Entry. Preserve its native dirty-save pump.
            var settings = Type("SlopWorld", "SlopWorld.Settings")
                .GetProperty("S")!
                .GetValue(null)!;
            Invoke("SlopWorld", "SlopWorld.ProfileSettings", "FlushIfDue", settings);
            return frame;
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(e.ToString());
        }
        finally
        {
            Invoke("Waylith", "Waylith.EngineApplication", "EndFrame");
        }
    }

    [JSExport]
    public static async Task<string> WorkspaceFrame(string origin, string requestedSession)
    {
        try
        {
            Invoke("Adapters", "BrowserAdapters.WorkspaceDrawing", "RequireEntry");
            Invoke("Adapters", "BrowserAdapters.BrowserTransport", "Configure", origin);
            Invoke("Adapters", "BrowserAdapters.BrowserGui", "Install");
            if (
                ((string[])Invoke("Adapters", "BrowserAdapters.BrowserFontBank", "Paths")!).Length
                > 0
            )
                Invoke("SlopWorld", "SlopWorld.UiFont", "Apply");
            Invoke("Adapters", "BrowserAdapters.BrowserModKeys", "Install");
            Invoke("Adapters", "BrowserAdapters.BrowserWorkspaceDoors", "Install");
            Invoke("Adapters", "BrowserAdapters.BrowserCursor", "Install");
            Invoke("Adapters", "BrowserAdapters.WorkspaceDrawing", "Initialize");
            Invoke("SlopWorld", "SlopWorld.StripOptions", "Hide");
            Invoke("SlopWorld", "SlopWorld.ModOptions", "Install");
            var type = Type("SlopWorld", "SlopWorld.SessionHub");
            var hub = type.GetField("Instance")!.GetValue(null)!;
            type.GetMethod("Connect")!.Invoke(hub, null);
            var until = DateTime.UtcNow.AddSeconds(15);
            object? session = null;
            while (DateTime.UtcNow < until)
            {
                Invoke(
                    "SlopWorld",
                    "SlopWorld.DaemonClient",
                    "PumpCompletions",
                    (Action<Exception>)(e => throw e)
                );
                type.GetMethod("Update")!.Invoke(hub, null);
                session = ((IEnumerable)type.GetProperty("Sessions")!.GetValue(hub)!)
                    .Cast<object>()
                    .FirstOrDefault(s =>
                        (bool)s.GetType().GetField("Alive")!.GetValue(s)!
                        && (
                            string.IsNullOrEmpty(requestedSession)
                            || (string)s.GetType().GetField("Name")!.GetValue(s)!
                                == requestedSession
                        )
                    );
                if (session != null)
                    break;
                // An authoritative empty catalog is a usable native workspace:
                // its sidebar and TopBar own session creation and connection UI.
                // A named reference still waits for that exact live session.
                if (
                    string.IsNullOrEmpty(requestedSession)
                    && (long)type.GetProperty("SessionsVersion")!.GetValue(hub)! > 0
                )
                    break;
                await Task.Delay(16);
            }
            if (session == null)
            {
                if (!string.IsNullOrEmpty(requestedSession))
                    throw new InvalidOperationException("No matching live session for workspace");
                // Keep the UI and normal update/reconnect pump available even if
                // the daemon was unavailable throughout the startup deadline.
                using var frame = JsonDocument.Parse(
                    (string)Invoke("Adapters", "BrowserAdapters.WorkspaceDrawing", "Frame", "[]")!
                );
                return frame.RootElement.GetProperty("commands").GetRawText();
            }
            var name = (string)session.GetType().GetField("Name")!.GetValue(session)!;
            var window = Invoke("SlopWorld", "SlopWorld.WorkspaceWindow", "Open", name)!;
            return (string)
                Invoke("Adapters", "BrowserAdapters.WorkspaceDrawing", "Repaint", window)!;
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(e.ToString());
        }
    }
}
