using UnityEngine;
using Verse;

namespace BrowserAdapters;

// Entry has no MapInterface to dispatch these owners. Keep the native chrome and
// session commands, but never invoke map gizmo selection, world drawing or ticks.
// Cached background playback belongs to the browser presenter, behind this GUI.
public static class BrowserUnderlyingWorkspace
{
    static Type Owner(string name) =>
        AppDomain
            .CurrentDomain.GetAssemblies()
            .Single(a => a.GetName().Name == "SlopWorld")
            .GetType("SlopWorld." + name, true)!;

    static readonly Type Window = Owner("WorkspaceWindow");
    static readonly Type Selection = Owner("SessionSelectable");
    static readonly Type Layout = Owner("WorkspaceLayout");
    static readonly Type Visibility = Owner("WorkspaceVisibility");
    static readonly Action<bool> TopBar = Owner("TopBar")
        .GetMethod("Draw")!
        .CreateDelegate<Action<bool>>();

    public static bool BackgroundVisible()
    {
        WorkspaceDrawing.RequireEntry();
        return !Find.WindowStack.Windows.Any(Window.IsInstanceOfType);
    }

    static bool ChromeVisible =>
        (bool)Visibility.GetProperty("Shown")!.GetValue(null)!
        && !(bool)Visibility.GetProperty("Hidden")!.GetValue(null)!;

    static ISelectable? Selected =>
        (ISelectable?)Selection.GetProperty("CurrentObject")!.GetValue(null);

    static (float x, float y) GridOrigin()
    {
        var geometry = Layout.GetProperty("Current")!.GetValue(null)!;
        var content = (Rect)geometry.GetType().GetField("Content")!.GetValue(geometry)!;
        var add = (Rect)Owner("Sidebar").GetProperty("AddBar")!.GetValue(null)!;
        var gap = (float)Owner("UiTheme").GetProperty("GapM")!.GetValue(null)!;
        var height = (float)
            Owner("SessionGizmoInput").GetField("ActionRowHeight")!.GetRawConstantValue()!;
        return (content.x + gap, add.y - height);
    }

    public static object Snapshot()
    {
        bool visible = BackgroundVisible();
        var actions = visible && ChromeVisible ? Selected?.GetGizmos().ToArray() ?? [] : [];
        var origin = GridOrigin();
        float width = UI.screenWidth - 147f - origin.x;
        return new
        {
            visible,
            chromeVisible = visible && ChromeVisible,
            interactive = visible && Find.WindowStack.GetsInput(null),
            session = Selection.GetProperty("Current")!.GetValue(null),
            actions = actions.OfType<Command>().Select(command => command.defaultLabel).ToArray(),
            firstActionRect = actions.Length == 0
                ? null
                : new[] { origin.x, origin.y, actions[0].GetWidth(width), 75f },
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
        TopBar(interactive);
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

        var origin = GridOrigin();
        // Installed DrawGizmoGrid retains grouping, wrapping, tooltips, hotkeys and
        // ProcessInput. Native Harmony's grid placement patch is not installed in
        // Entry: translate its bottom anchor to the shared sidebar geometry here.
        // BeginGroup translates mouse coordinates with drawing and restores both.
        float nativeTop = UI.screenHeight - 35f - 14f - 75f;
        float offset = origin.y - nativeTop;
        bool enabled = GUI.enabled;
        GUI.BeginGroup(new Rect(0f, offset, UI.screenWidth, UI.screenHeight - offset));
        try
        {
            GUI.enabled = enabled && interactive;
            GizmoGridDrawer.DrawGizmoGrid(selected.GetGizmos(), origin.x, out _);
        }
        finally
        {
            GUI.enabled = enabled;
            GUI.EndGroup();
        }
    }
}
