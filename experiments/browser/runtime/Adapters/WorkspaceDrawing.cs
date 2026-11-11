using System.Collections;
using System.Reflection;
using System.Text.Json;
using UnityEngine;
using UnityStubs.Gui;
using Verse;

namespace BrowserAdapters;

// The actual mod Window retains UI ownership. This renderer checkpoint runs only
// after installed Entry/UI initialization and browser GUI installation.
public static class WorkspaceDrawing
{
    public static string SidebarRows()
    {
        var mod = AppDomain
            .CurrentDomain.GetAssemblies()
            .Single(a => a.GetName().Name == "SlopWorld");
        var sidebar = mod.GetType("SlopWorld.Sidebar")!;
        var layout = sidebar
            .GetField("Layout", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;
        const BindingFlags fields =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var rows = (IEnumerable)layout.GetType().GetField("Rows", fields)!.GetValue(layout)!;
        return JsonSerializer.Serialize(
            rows.Cast<object>()
                .Select(row =>
                {
                    var type = row.GetType();
                    var line = (Rect)type.GetField("Line", fields)!.GetValue(row)!;
                    var text = (Rect)type.GetField("Text", fields)!.GetValue(row)!;
                    return new
                    {
                        session = type.GetField("Session", fields)!.GetValue(row),
                        ghost = type.GetField("Ghost", fields)!.GetValue(row),
                        hasPawn = type.GetField("Pawn", fields)!.GetValue(row) != null,
                        rowHeight = line.height,
                        textHeight = text.height,
                    };
                })
        );
    }

    public static void ConfigureViewport(int width, int height, float scale)
    {
        RequireEntry();
        if (
            width < 320
            || width > 7680
            || height < 200
            || height > 4320
            || !float.IsFinite(scale)
            || scale < .5f
            || scale > 4f
        )
            throw new ArgumentOutOfRangeException(nameof(scale), "Invalid workspace viewport");
        Prefs.UIScale = scale;
        BrowserFontBank.ConfigureDensity(scale);
        GameSession.ConfigureViewport(width, height);
    }

    static Action? shortcuts;
    static Action? applyLayout;
    static float timeOrigin,
        lastTime;

    public static void Initialize()
    {
        RequireEntry();
        BrowserPortraits.Bind();
        var mod = AppDomain
            .CurrentDomain.GetAssemblies()
            .Single(a => a.GetName().Name == "SlopWorld");
        shortcuts = mod.GetType("SlopWorld.Patch_WorkspaceShortcuts")!
            .GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static)!
            .CreateDelegate<Action>();
        applyLayout = mod.GetType("SlopWorld.Sidebar")!
            .GetMethod("ApplyPendingLayoutChange", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<Action>();
        timeOrigin = lastTime = Time.unscaledTime;
        // Steam is explicitly unavailable in this browser host. A hidden desktop
        // warning would capture input after F12 closes the full-screen workspace.
        foreach (var dialog in Find.WindowStack.Windows.OfType<Dialog_MessageBox>().ToArray())
            if (dialog.text == "SteamClientMissing".Translate().ToString())
                dialog.Close(false);
    }

    public static void BeginFrame(double elapsed)
    {
        RequireEntry();
        float now = timeOrigin + (float)elapsed;
        // EngineApplication and UnityRuntime own separate clocks. Advance the
        // compatibility clock so layout snapshots, input timing and fades can age,
        // without invoking UnityWorld.Update or any colony/simulation callback.
        Waylith.UnityCompat.UnityRuntime.Current!.BeginFrame(
            Math.Clamp(now - lastTime, 0f, 1f / 3),
            now
        );
        lastTime = now;
    }

    public static string Snapshot()
    {
        var window = Find.WindowStack.Windows.FirstOrDefault(w =>
            w.GetType().FullName == "SlopWorld.WorkspaceWindow"
        );
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        var panel = window?.GetType().GetProperty("_terminal", privateInstance)!.GetValue(window);
        object? PanelField(string name) =>
            panel?.GetType().GetField(name, privateInstance)!.GetValue(panel);
        var bounds = panel?.GetType().GetProperty("Bounds")!.GetValue(panel);
        var font = panel?.GetType().Assembly.GetType("SlopWorld.TerminalFont");
        var terminalState = PanelField("_state");
        object? Shape(string name) =>
            terminalState?.GetType().GetField(name)!.GetValue(terminalState);
        var cache = PanelField("_cache") as RenderTexture;
        var cacheKey = PanelField("_cacheKey");
        return JsonSerializer.Serialize(
            new
            {
                game = JsonDocument.Parse(GameSession.Snapshot()).RootElement,
                frame = Time.frameCount,
                screenWidth = Screen.width,
                screenHeight = Screen.height,
                uiWidth = UI.screenWidth,
                uiHeight = UI.screenHeight,
                uiScale = Prefs.UIScale,
                elapsed = Time.unscaledTime,
                windowOpen = window != null,
                doors = BrowserWorkspaceDoors.Snapshot(),
                underlying = BrowserUnderlyingWorkspace.Snapshot(),
                session = window
                    ?.GetType()
                    .GetProperty("SessionName", privateInstance)!
                    .GetValue(window),
                terminalDrawn = panel?.GetType().GetProperty("DrewScreen")!.GetValue(panel),
                terminalGeometry = new
                {
                    bounds = bounds == null
                        ? null
                        : new[] { "X", "Y", "Width", "Height" }
                            .Select(n => bounds.GetType().GetField(n)!.GetValue(bounds))
                            .ToArray(),
                    cellHeight = font?.GetProperty("CellH")!.GetValue(null),
                    rows = Shape("Rows"),
                    cols = Shape("Cols"),
                },
                terminalScroll = new
                {
                    offset = PanelField("_scrollOff"),
                    pixels = PanelField("_historyLastPixels"),
                    maximumPixels = PanelField("_historyMax"),
                    wanted = PanelField("_wantedScrollOff"),
                    sent = PanelField("_sentScrollOff"),
                    pending = PanelField("_scrollPending"),
                    requests = (
                        PanelField("_historyRequests") as System.Collections.IDictionary
                    )?.Count,
                    viewReady = PanelField("_historyViewReady"),
                },
                terminalCache = new
                {
                    disabled = PanelField("_noCache"),
                    exists = cache != null,
                    created = cache != null && cache.IsCreated(),
                    session = cacheKey?.GetType().GetField("Session")!.GetValue(cacheKey),
                    revision = PanelField("_cacheContentRevision"),
                    outsideTargetTrace = BrowserGui.OutsideTargetTrace,
                },
                errors = RuntimeServices.Errors,
            }
        );
    }

    public static void Update()
    {
        RequireEntry();
        Find.WindowStack.WindowsUpdate();
        Messages.Update();
    }

    public static void RequireEntry()
    {
        if (Current.ProgramState != ProgramState.Entry || Current.Game != null)
            throw new InvalidOperationException(
                "The browser workspace requires Entry without a colony"
            );
    }

    public static string Frame(string batch) =>
        BrowserGuiInput.Dispatch(
            batch,
            (BrowserGui)GuiBackend.Current,
            () =>
            {
                applyLayout?.Invoke();
                UI.ApplyUIScale();
                shortcuts?.Invoke();
                if (Event.current.type != EventType.Used)
                    GUI.DispatchInputToWindows();
                BrowserUnderlyingWorkspace.Draw();
                Find.WindowStack.WindowStackOnGUI();
                GUI.ProcessPendingWindows();
                // The Entry workspace owns the foreground. Draw native rejection
                // messages after deferred windows so their backgrounds cannot cover them.
                Messages.MessagesDoGUI();
            }
        );

    public static string Repaint(object owner)
    {
        var window = (Window)owner;
        GUIUtility.ResetControlIds();
        Event.current = new Event { type = EventType.Repaint, rawType = EventType.Repaint };
        UI.ApplyUIScale();
        window.DoWindowContents(new Rect(0, 0, UI.screenWidth, UI.screenHeight));
        return ((BrowserGui)GuiBackend.Current).Drain();
    }
}
