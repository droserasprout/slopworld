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

    public static Selector? MapSelection() =>
        Current.ProgramState != ProgramState.Playing ? null : Find.Selector;

    public static bool Visible() => UI.screenWidth >= 800 && UI.screenHeight >= 500;

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
