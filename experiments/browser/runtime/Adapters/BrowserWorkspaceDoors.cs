// Entry-only UI definition binding. Native TopBar owns door geometry, drawing
// and actions; no building instance, map or simulation state is created here.
using System.Reflection;
using System.Xml;
using Verse;

namespace BrowserAdapters;

public static class BrowserWorkspaceDoors
{
    static bool installed;
    static Action? coreMenu;
    static Action? radioUpdate;

    public static bool Available(Map? map)
    {
        WorkspaceDrawing.RequireEntry();
        return installed;
    }

    public static void OpenCore()
    {
        WorkspaceDrawing.RequireEntry();
        coreMenu!();
    }

    public static void Update()
    {
        WorkspaceDrawing.RequireEntry();
        radioUpdate!();
    }

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
        var mod = AppDomain
            .CurrentDomain.GetAssemblies()
            .Single(a => a.GetName().Name == "SlopWorld");
        var defOf = mod.GetType("SlopWorld.ModDefOf")!;
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
            defOf.GetField(name)!.SetValue(null, definition);
        }
        defOf
            .GetField("Ship_ComputerCore")!
            .SetValue(null, DefDatabase<ThingDef>.GetNamed("Ship_ComputerCore"));
        coreMenu = mod.GetType("SlopWorld.CoreTip")!
            .GetMethod("OpenWorkspaceMenu")!
            .CreateDelegate<Action>();
        var radio = mod.GetType("SlopWorld.Radio")!;
        radio.GetProperty("LocalPlaybackSupported")!.SetValue(null, false);
        radioUpdate = radio.GetMethod("Update")!.CreateDelegate<Action>();
        installed = true;
    }
}
