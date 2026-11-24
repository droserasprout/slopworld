// Entry-only UI definition binding. Native TopBar owns door geometry, drawing
// and actions; no building instance, map or simulation state is created here.
using System.Xml;
using SlopWorld;
using Verse;

namespace BrowserAdapters;

public static class BrowserWorkspaceDoors
{
    static bool installed;

    public static bool Available(Map? map)
    {
        WorkspaceDrawing.RequireEntry();
        return installed;
    }

    public static void OpenCore()
    {
        WorkspaceDrawing.RequireEntry();
        CoreTip.OpenWorkspaceMenu();
    }

    public static void Update()
    {
        WorkspaceDrawing.RequireEntry();
        Radio.Update();
    }

    public static object Snapshot()
    {
        var doors = TopBar.CurrentDoors;
        static object Door(bool visible, UnityEngine.Rect rect) =>
            new { visible, rect = new[] { rect.x, rect.y, rect.width, rect.height } };
        return new
        {
            planner = Door(doors.HasPlanner, doors.Planner),
            config = Door(doors.HasConfig, doors.Config),
            core = Door(doors.HasCore, doors.Core),
            jukebox = Door(doors.HasJukebox, doors.Jukebox),
            view = WorkspaceWindow.Showing?.GetType().Name,
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
        // Only authored presentation definitions are loaded. Core's genuine
        // computer definition is already available from Core startup.
        foreach (
            var (file, name) in new[]
            {
                ("PlannerBench.xml", "SlopPlannerBench"),
                ("Jukebox.xml", "SlopJukebox"),
            }
        )
        {
            var xml = new XmlDocument();
            xml.Load(Path.Combine(pack.RootDir, "Defs", file));
            var node = xml.DocumentElement!.ChildNodes.OfType<XmlElement>().Single();
            var definition = DirectXmlToObject.ObjectFromXml<ThingDef>(node, false);
            if (definition.defName != name)
                throw new InvalidDataException("Unexpected workspace door definition");
            definition.modContentPack = pack;
            DefDatabase<ThingDef>.Add(definition);
            definition.PostLoad();
            DirectXmlCrossRefLoader.ResolveAllWantedCrossReferences(FailMode.LogErrors);
            definition.ResolveReferences();
            if (name == "SlopPlannerBench")
                ModDefOf.SlopPlannerBench = definition;
            else
                ModDefOf.SlopJukebox = definition;
        }
        ModDefOf.Ship_ComputerCore = DefDatabase<ThingDef>.GetNamed("Ship_ComputerCore");
        Radio.ObserveDaemonPlayback();
        installed = true;
    }
}
