using SlopWorld;
using UnityEngine;
using Verse;

namespace BrowserAdapters;

// Entry has no MapInterface to dispatch these owners. Keep the native chrome and
// session commands, but never invoke map gizmo selection, world drawing or ticks.
// Cached background playback belongs to the browser presenter, behind this GUI.
public static class BrowserUnderlyingWorkspace
{
    public static bool BackgroundVisible()
    {
        WorkspaceDrawing.RequireEntry();
        return !Find.WindowStack.Windows.OfType<WorkspaceWindow>().Any();
    }

    static bool ChromeVisible => WorkspaceVisibility.Shown && !WorkspaceVisibility.Hidden;
    static ISelectable? Selected => SessionSelectable.CurrentObject;

    public static object Snapshot()
    {
        bool visible = BackgroundVisible();
        var actions = visible && ChromeVisible ? Selected?.GetGizmos().ToArray() ?? [] : [];
        float left = SessionGizmoInput.GridLeft;
        float width = UI.screenWidth - 147f - left;
        return new
        {
            visible,
            chromeVisible = visible && ChromeVisible,
            interactive = visible && Find.WindowStack.GetsInput(null),
            session = SessionSelectable.Current,
            actions = actions.OfType<Command>().Select(command => command.defaultLabel).ToArray(),
            firstActionRect = actions.Length == 0
                ? null
                : new[]
                {
                    left,
                    SessionGizmoInput.GridTop,
                    actions[0].GetWidth(width),
                    SessionGizmoInput.ActionRowHeight,
                },
        };
    }

    public static void Draw()
    {
        if (!BackgroundVisible() || !ChromeVisible || Event.current.type == EventType.Layout)
            return;
        bool interactive = Find.WindowStack.GetsInput(null);
        // Deferred window input runs first. Modal windows must never leak keyboard
        // hotkeys into the underlying grid, even when their bounds miss the pointer.
        if (!interactive && Event.current.type != EventType.Repaint)
            return;
        TopBar.Draw(interactive);
        if (!BackgroundVisible() || Event.current.type == EventType.Used)
            return;
        BrowserSidebar.Draw(interactive);
        // A door or sidebar action can replace this layer during the input pass.
        if (
            !BackgroundVisible()
            || Event.current.type == EventType.Used
            || Selected is not { } selected
        )
            return;

        // Installed DrawGizmoGrid retains grouping, wrapping, tooltips, hotkeys and
        // ProcessInput. Native Harmony's grid placement patch is not installed in
        // Entry: translate its bottom anchor to the shared sidebar geometry here.
        // BeginGroup translates mouse coordinates with drawing and restores both.
        float nativeTop = UI.screenHeight - 35f - 14f - 75f;
        float offset = SessionGizmoInput.GridTop - nativeTop;
        bool enabled = GUI.enabled;
        GUI.BeginGroup(new Rect(0f, offset, UI.screenWidth, UI.screenHeight - offset));
        try
        {
            GUI.enabled = enabled && interactive;
            GizmoGridDrawer.DrawGizmoGrid(selected.GetGizmos(), SessionGizmoInput.GridLeft, out _);
        }
        finally
        {
            GUI.enabled = enabled;
            GUI.EndGroup();
        }
    }
}
