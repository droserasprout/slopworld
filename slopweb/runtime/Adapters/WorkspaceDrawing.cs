using System.Text.Json;
using UnityEngine;
using UnityStubs.Gui;
using Verse;

namespace BrowserAdapters;

// The actual mod Window retains UI ownership. This renderer checkpoint runs only
// after installed Entry/UI initialization and browser GUI installation.
public static partial class WorkspaceDrawing
{
    static float viewportScale;
    static bool customScale;

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
        // Follow display density until a native scale control selects a different
        // value. Resizing the backing canvas must not undo that selection.
        if (viewportScale != 0 && Prefs.UIScale != viewportScale)
            customScale = true;
        if (!customScale)
            Prefs.UIScale = scale;
        viewportScale = scale;
        BrowserFontBank.ConfigureDensity(Prefs.UIScale);
        WorkspaceScene.ConfigureViewport(width, height);
    }

    static Action? shortcuts;
    static Action? applyLayout;
    static Action? flushTerminalInput;
    static Action? drawUiBubble;
    static Func<float>? terminalDeadline;
    static Func<float>? cursorDeadline;

    public static float NextUpdateDelay() =>
        Math.Min(terminalDeadline?.Invoke() ?? 0f, cursorDeadline?.Invoke() ?? 0f);

    static float timeOrigin,
        lastTime;

    public static void Initialize()
    {
        RequireEntry();
        BindModServices();
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

    public static void Update()
    {
        RequireEntry();
        BrowserWorkspaceDoors.Update();
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

    public static string Frame(JsonElement batch, double retainedPacket, bool commandsOnly)
    {
        return BrowserGuiInput.DispatchBatch(
            batch,
            (BrowserGui)GuiBackend.Current,
            () =>
            {
                // Input can change scale between passes in this same batch.
                // Retire old glyph atlases before measuring layout or repainting;
                // the bank's resolution revision also invalidates cached text/rows.
                BrowserFontBank.ConfigureDensity(Prefs.UIScale);
                applyLayout?.Invoke();
                UI.ApplyUIScale();
                shortcuts?.Invoke();
                if (Event.current.type != EventType.Used)
                    GUI.DispatchInputToWindows();
                BrowserUnderlyingWorkspace.Draw();
                // UIRoot normally pumps tooltips before WindowStackOnGUI. Keep
                // that order: native tips enqueue ImmediateWindows, whose deferred
                // callbacks must join this pass before ProcessPendingWindows.
                TooltipHandler.DoTooltipGUI();
                Find.WindowStack.WindowStackOnGUI();
                GUI.ProcessPendingWindows();
                // Offline WASM installs selected hooks instead of running Harmony. Use
                // the same framework presenter as native, once after the window pass.
                drawUiBubble?.Invoke();
                // The Entry workspace owns the foreground. Draw native rejection
                // messages after deferred windows so their backgrounds cannot cover them.
                Messages.MessagesDoGUI();
            },
            flushTerminalInput,
            retainedPacket,
            commandsOnly
        );
    }
}
