// Load authored UI bindings after Core without admitting mod simulation defs.
// Verse retains XML/reference/default-binding ownership; the mod owns key policy.
using System.Reflection;
using System.Xml;
using RimWorld;
using Verse;

namespace BrowserAdapters;

public static class BrowserModKeys
{
    static bool installed;

    public static bool WorkspaceState(ProgramState state) =>
        state == ProgramState.Playing || (state == ProgramState.Entry && Current.Game == null);

    public static int Install()
    {
        WorkspaceDrawing.RequireEntry();
        if (installed)
            throw new InvalidOperationException("Workspace key definitions already installed");
        var pack =
            BrowserModContent.Current()
            ?? throw new InvalidOperationException("Mount mod content before keys");
        var xml = new XmlDocument();
        xml.Load(Path.Combine(pack.RootDir, "Defs", "KeyBindings.xml"));
        var definitions = new List<Def>();
        foreach (var node in xml.DocumentElement!.ChildNodes.OfType<XmlElement>())
        {
            Def def;
            switch (node.Name)
            {
                case "KeyBindingCategoryDef":
                    var category = DirectXmlToObject.ObjectFromXml<KeyBindingCategoryDef>(
                        node,
                        false
                    );
                    DefDatabase<KeyBindingCategoryDef>.Add(category);
                    def = category;
                    break;
                case "KeyBindingDef":
                    var binding = DirectXmlToObject.ObjectFromXml<KeyBindingDef>(node, false);
                    DefDatabase<KeyBindingDef>.Add(binding);
                    def = binding;
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unexpected workspace definition " + node.Name
                    );
            }
            def.modContentPack = pack;
            def.PostLoad();
            definitions.Add(def);
        }
        DirectXmlCrossRefLoader.ResolveAllWantedCrossReferences(FailMode.LogErrors);
        foreach (var definition in definitions)
            definition.ResolveReferences();
        KeyPrefs.Init();

        var mod = AppDomain
            .CurrentDomain.GetAssemblies()
            .Single(a => a.GetName().Name == "SlopWorld");
        var defOf = mod.GetType("SlopWorld.ModDefOf")!;
        // Native DefOf constructors distinguish binding from accidental early use.
        // Scope that guard around this explicit UI-only bind; no simulation fields
        // are initialized and Core's other DefOf owners are left alone.
        var guard = typeof(DefOfHelper).GetField(
            "bindingNow",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;
        var previous = guard.GetValue(null);
        guard.SetValue(null, true);
        try
        {
            foreach (
                var field in defOf
                    .GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(f => f.FieldType == typeof(KeyBindingDef))
            )
                field.SetValue(null, DefDatabase<KeyBindingDef>.GetNamed(field.Name));
        }
        finally
        {
            guard.SetValue(null, previous);
        }
        installed = true;
        return definitions.OfType<KeyBindingDef>().Count();
    }
}
