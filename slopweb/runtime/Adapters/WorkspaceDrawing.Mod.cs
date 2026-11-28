using System.Text.Json;
using SlopWorld;
using UnityEngine;
using Verse;

namespace BrowserAdapters;

public static partial class WorkspaceDrawing
{
    static void BindModServices()
    {
        shortcuts = Patch_WorkspaceShortcuts.Prefix;
        applyLayout = Sidebar.ApplyPendingLayoutChange;
        flushTerminalInput = WorkspaceWindow.FlushTerminalInput;
        drawUiBubble = UiBubble.DrawAfterWindows;
        terminalDeadline = WorkspaceWindow.NextUpdateDelay;
        cursorDeadline = GameCursor.NextUpdateDelay;
    }

    public static string Snapshot()
    {
        var window = Find.WindowStack.WindowOfType<WorkspaceWindow>();
        var panel = window?.SelectedTerminal;
        return JsonSerializer.Serialize(
            new
            {
                game = WorkspaceScene.Status(),
                frame = Time.frameCount,
                screenWidth = Screen.width,
                screenHeight = Screen.height,
                uiWidth = UI.screenWidth,
                uiHeight = UI.screenHeight,
                uiScale = Prefs.UIScale,
                elapsed = Time.unscaledTime,
                windowOpen = window != null,
                doors = BrowserWorkspaceDoors.Snapshot(),
                sidebar = BrowserSidebar.Snapshot(),
                underlying = BrowserUnderlyingWorkspace.Snapshot(),
                session = window?.SessionName,
                terminalDrawn = panel?.DrewScreen,
                terminalGeometry = TerminalPanel.GeometrySnapshot(panel),
                terminalScroll = TerminalPanel.ScrollSnapshot(panel),
                terminalCache = TerminalPanel.CacheSnapshot(panel, BrowserGui.OutsideTargetTrace),
                errors = RuntimeServices.Errors,
            }
        );
    }
}
