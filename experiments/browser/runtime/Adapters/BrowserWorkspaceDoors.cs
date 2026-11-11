// Entry-only UI definition binding. Native TopBar owns door geometry, drawing
// and actions; no building instance, map or simulation state is created here.
using System.Reflection;
using System.Xml;
using Verse;

namespace BrowserAdapters;

public static class BrowserWorkspaceDoors
{
    static bool installed;

    public static object Snapshot()
    {
        var mod = AppDomain
            .CurrentDomain.GetAssemblies()
            .Single(a => a.GetName().Name == "SlopWorld");
        var top = mod.GetType("SlopWorld.TopBar")!;
        var doors = top.GetField("_doors", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        var layout = doors.GetType();
        object Door(string name)
        {
            var rect = (UnityEngine.Rect)layout.GetField(name)!.GetValue(doors)!;
            return new
            {
                visible = (bool)layout.GetField("Has" + name)!.GetValue(doors)!,
                rect = new[] { rect.x, rect.y, rect.width, rect.height },
            };
        }
        return new
        {
            planner = Door("Planner"),
            config = Door("Config"),
            core = Door("Core"),
            jukebox = Door("Jukebox"),
            view = mod.GetType("SlopWorld.WorkspaceWindow")!
                .GetProperty(
                    "Showing",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                )!
                .GetValue(null)
                ?.GetType()
                .Name,
        };
    }

    public static void Install()
    {
        WorkspaceDrawing.RequireEntry();
        if (installed)
            throw new InvalidOperationException("Workspace doors already installed");
        var pack =
            BrowserModContent.Current()
            ?? throw new InvalidOperationException("Mount mod content before doors");
        var xml = new XmlDocument();
        xml.Load(Path.Combine(pack.RootDir, "Defs", "PlannerBench.xml"));
        var node = xml.DocumentElement!.ChildNodes.OfType<XmlElement>().Single();
        var planner = DirectXmlToObject.ObjectFromXml<ThingDef>(node, false);
        if (planner.defName != "SlopPlannerBench")
            throw new InvalidDataException("Unexpected workspace door definition");
        planner.modContentPack = pack;
        DefDatabase<ThingDef>.Add(planner);
        planner.PostLoad();
        DirectXmlCrossRefLoader.ResolveAllWantedCrossReferences(FailMode.LogErrors);
        planner.ResolveReferences();
        var mod = AppDomain
            .CurrentDomain.GetAssemblies()
            .Single(a => a.GetName().Name == "SlopWorld");
        // BrowserModKeys has already initialized the native DefOf owner under its
        // binding guard. Bind only the presentation dependency used by Planner.
        mod.GetType("SlopWorld.ModDefOf")!.GetField("SlopPlannerBench")!.SetValue(null, planner);
        installed = true;
    }
}
