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

    static readonly Func<bool> Shown = Visibility
        .GetProperty("Shown")!
        .GetMethod!.CreateDelegate<Func<bool>>();
    static readonly Func<bool> Hidden = Visibility
        .GetProperty("Hidden")!
        .GetMethod!.CreateDelegate<Func<bool>>();
    static readonly Func<ISelectable?> SelectedObject = Selection
        .GetProperty("CurrentObject")!
        .GetMethod!.CreateDelegate<Func<ISelectable?>>();
    static readonly Func<string?> Session = Selection
        .GetProperty("Current")!
        .GetMethod!.CreateDelegate<Func<string?>>();
    static readonly Func<Rect> AddBar = Owner("Sidebar")
        .GetProperty("AddBar")!
        .GetMethod!.CreateDelegate<Func<Rect>>();
    static readonly Func<float> Gap = Owner("UiTheme")
        .GetProperty("GapM")!
        .GetMethod!.CreateDelegate<Func<float>>();
    static readonly float ActionHeight = (float)
        Owner("SessionGizmoInput").GetField("ActionRowHeight")!.GetRawConstantValue()!;

    // Geometry is a mod-owned value type. Cache its members once; do not bind
    // its current value, which changes as windows and viewport sizes change.
    static readonly System.Reflection.PropertyInfo Geometry = Layout.GetProperty("Current")!;
    static readonly System.Reflection.FieldInfo Content = Geometry.PropertyType.GetField(
        "Content"
    )!;

    static bool ChromeVisible => Shown() && !Hidden();
    static ISelectable? Selected => SelectedObject();

    static (float x, float y) GridOrigin()
    {
        var content = (Rect)Content.GetValue(Geometry.GetValue(null))!;
        return (content.x + Gap(), AddBar().y - ActionHeight);
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
            session = Session(),
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
