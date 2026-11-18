using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace BrowserAdapters;

// Entry-only sidebar dispatch. The mod still owns row grouping, rendering, menus,
// selection and filters; no pawn identities or colony are synthesized.
public static class BrowserSidebar
{
    static readonly List<ColonistBar.Entry> entries = new();
    static readonly List<Vector2> locations = new();
    static string hostHome = "";

    // Read native UI-space geometry for diagnostic pointer targeting. Layout and
    // hit testing remain in Sidebar, including side and open-files preferences.
    public static object Snapshot()
    {
        var sidebar = AppDomain
            .CurrentDomain.GetAssemblies()
            .Single(a => a.GetName().Name == "SlopWorld")
            .GetType("SlopWorld.Sidebar")!;
        float[] Box(string name)
        {
            var rect = (Rect)sidebar.GetProperty(name)!.GetValue(null)!;
            return [rect.x, rect.y, rect.width, rect.height];
        }
        return new
        {
            panel = Box("Panel"),
            body = Box("Body"),
            add = Box("AddBar"),
        };
    }

    public static void ConfigureHome(string home)
    {
        if (home.Length > 4096 || !home.StartsWith('/') || home.Contains('\0'))
            throw new ArgumentException("Invalid host display home directory", nameof(home));
        hostHome = home;
    }

    // Only the native sidebar's cwd-label formatter calls this boundary. It
    // conveys no browser filesystem access and does not change process HOME.
    public static string HomeFolder(Environment.SpecialFolder folder) =>
        folder == Environment.SpecialFolder.UserProfile
            ? hostHome
            : throw new InvalidOperationException("Unexpected sidebar folder request");

    public static Selector? MapSelection() =>
        Current.ProgramState != ProgramState.Playing ? null : Find.Selector;

    // WorkspaceLayout owns available space and the user's hidden-sidebar choice.
    // An independent minimum here leaves a reserved but blank sidebar on resize.
    public static bool Visible() => UI.screenWidth > 0 && UI.screenHeight > 0;

    public static void Draw(bool interactive)
    {
        if (Event.current.type == EventType.Layout)
            return;
        var mod = AppDomain
            .CurrentDomain.GetAssemblies()
            .Single(a => a.GetName().Name == "SlopWorld");
        var strip = mod.GetType("SlopWorld.ColonistBarStrip")!;
        var sidebar = mod.GetType("SlopWorld.Sidebar")!;
        var drawing = strip.GetProperty("Drawing")!;
        var blocked = strip.GetProperty("Blocked")!;
        if ((bool)drawing.GetValue(null)!)
            return;
        drawing.SetValue(null, true);
        blocked.SetValue(null, !interactive);
        try
        {
            sidebar.GetMethod("Place")!.Invoke(null, new object[] { entries, locations, 0 });
            sidebar.GetMethod("DrawBack")!.Invoke(null, null);
            sidebar.GetMethod("DrawFront")!.Invoke(null, null);
        }
        finally
        {
            sidebar.GetMethod("EndDraw")!.Invoke(null, null);
            drawing.SetValue(null, false);
            blocked.SetValue(null, false);
        }
    }
}
