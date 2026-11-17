using System.Runtime.Loader;
using EcoFixtureGame;
using HarmonyLib;
using Mono.Cecil;
using SlopWorld;

static class EcoPrefixFixture
{
    [EcoPrefix]
    [HarmonyPatch(typeof(Selector), nameof(Selector.HandleMapClicks))]
    static class Selection
    {
        [HarmonyPriority(Priority.Last)]
        static bool Prefix(Selector __instance) => __instance.Allow;
    }

    [EcoPrefix]
    [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.HandleMapClicks))]
    static class MapClicks
    {
        static bool Prefix() => false;
    }

    [EcoPrefix]
    [HarmonyPatch(typeof(Dialogs), nameof(Dialogs.Add))]
    static class Dialog
    {
        static void Prefix(int window) => Dialogs.Value = window * 2;
    }

    [HarmonyPatch(typeof(Dialogs), nameof(Dialogs.Add))]
    static class DesktopOnly
    {
        static bool Prefix() => false;
    }

    public static void Check()
    {
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(AppContext.BaseDirectory);
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));
        var options = new ReaderParameters { AssemblyResolver = resolver };
        using var game = AssemblyDefinition.ReadAssembly(
            typeof(Selector).Assembly.Location,
            options
        );
        using var mod = AssemblyDefinition.ReadAssembly(
            typeof(EcoPrefixFixture).Assembly.Location,
            options
        );
        var hooks = new List<string>();
        void Reject(string expected)
        {
            try
            {
                EcoPrefixes.Apply(game, mod, hooks);
            }
            catch (InvalidOperationException e)
                when (e.Message.Contains(expected, StringComparison.Ordinal))
            {
                return;
            }
            throw new Exception("Expected Eco patch rejection: " + expected);
        }

        var selector = game.MainModule.GetType(typeof(Selector).FullName);
        var click = selector.Methods.Single(m => m.Name == "HandleMapClicks");
        var overload = new MethodDefinition(click.Name, click.Attributes, click.ReturnType);
        selector.Methods.Add(overload);
        Reject("ambiguous target");
        selector.Methods.Remove(overload);
        var selection = mod
            .MainModule.GetTypes()
            .Single(t => t.FullName == "EcoPrefixFixture/Selection");
        var prefix = selection.Methods.Single();
        prefix.Parameters[0].Name = "__result";
        Reject("__result");
        prefix.Parameters[0].Name = "__instance";
        var instanceType = prefix.Parameters[0].ParameterType;
        prefix.Parameters[0].ParameterType = new ByReferenceType(instanceType);
        Reject("argument __instance");
        prefix.Parameters[0].ParameterType = instanceType;
        click.ReturnType = game.MainModule.TypeSystem.Boolean;
        Reject("void target");
        click.ReturnType = game.MainModule.TypeSystem.Void;
        prefix.IsStatic = false;
        Reject("unsupported Prefix signature");
        prefix.IsStatic = true;
        var prepare = new MethodDefinition("Prepare", prefix.Attributes, prefix.ReturnType);
        selection.Methods.Add(prepare);
        Reject("single Prefix");
        selection.Methods.Remove(prepare);
        var desktop = mod
            .MainModule.GetTypes()
            .Single(t => t.FullName == "EcoPrefixFixture/DesktopOnly");
        var marker = selection.CustomAttributes.Single(a =>
            a.AttributeType.Name == "EcoPrefixAttribute"
        );
        desktop.CustomAttributes.Add(marker);
        Reject("Multiple Eco prefixes");
        desktop.CustomAttributes.Remove(marker);

        EcoPrefixes.Apply(game, mod, hooks);
        if (
            hooks.Count != 3
            || !prefix.IsPublic
            || !selection.IsNestedPublic
            || !selection.DeclaringType.IsPublic
        )
            throw new Exception("Selected hooks and their declaring types must be callable");
        Reject("already installed");
        using var gameBytes = new MemoryStream();
        using var modBytes = new MemoryStream();
        game.Write(gameBytes);
        mod.Write(modBytes);
        gameBytes.Position = modBytes.Position = 0;
        var context = new AssemblyLoadContext("woven Eco fixture", isCollectible: true);
        try
        {
            var woven = context.LoadFromStream(gameBytes);
            context.LoadFromStream(modBytes);
            var type = woven.GetType(typeof(Selector).FullName, true);
            var instance = Activator.CreateInstance(type);
            var method = type.GetMethod(click.Name);
            method.Invoke(instance, null);
            if ((int)type.GetField("Clicks").GetValue(instance) != 0)
                throw new Exception("False prefix must skip the original");
            type.GetField("Allow").SetValue(instance, true);
            method.Invoke(instance, null);
            if ((int)type.GetField("Clicks").GetValue(instance) != 1)
                throw new Exception("True prefix must continue into the original");
            var map = woven.GetType(typeof(MapInterface).FullName, true);
            map.GetMethod(click.Name).Invoke(null, null);
            if ((int)map.GetField("Clicks").GetValue(null) != 0)
                throw new Exception("Same-name static target must call its own prefix");
            var dialogs = woven.GetType(typeof(Dialogs).FullName, true);
            dialogs.GetMethod("Add").Invoke(null, [42, 5]);
            if ((int)dialogs.GetField("Value").GetValue(null) != 15)
                throw new Exception("Named argument must reach the prefix before the original");
        }
        finally
        {
            context.Unload();
        }
    }
}
