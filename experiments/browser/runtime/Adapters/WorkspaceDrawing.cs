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
    static Action? shortcuts;
    static Action? applyLayout;
    static float timeOrigin,
        lastTime;

    public static void Initialize()
    {
        RequireEntry();
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
        return JsonSerializer.Serialize(
            new
            {
                game = JsonDocument.Parse(GameSession.Snapshot()).RootElement,
                frame = Time.frameCount,
                elapsed = Time.unscaledTime,
                windowOpen = window != null,
                session = window
                    ?.GetType()
                    .GetProperty("SessionName", privateInstance)!
                    .GetValue(window),
                terminalDrawn = panel?.GetType().GetProperty("DrewScreen")!.GetValue(panel),
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
