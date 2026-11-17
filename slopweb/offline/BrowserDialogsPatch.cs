// Reuse the desktop dialog layer policy before native WindowStack inserts/sorts.
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BrowserDialogsPatch
{
    internal static void Apply(AssemblyDefinition game, AssemblyDefinition mod, List<string> hooks)
    {
        var add = game.MainModule.GetType("Verse.WindowStack").Methods.Single(m => m.Name == "Add");
        var prefix = mod
            .MainModule.GetType("SlopWorld.Patch_DialogsOverChrome")
            .Methods.Single(m => m.Name == "Prefix");
        if (
            add.IsStatic
            || !add.HasBody
            || add.ReturnType.FullName != "System.Void"
            || add.Parameters.Count != 1
            || add.Parameters[0].ParameterType.FullName != "Verse.Window"
            || !prefix.IsStatic
            || prefix.ReturnType.FullName != "System.Void"
            || prefix.Parameters.Count != 1
            || prefix.Parameters[0].ParameterType.FullName != "Verse.Window"
        )
            throw new InvalidOperationException("Dialog layer boundary changed");
        prefix.IsPublic = true;
        var first = add.Body.Instructions[0];
        var il = add.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(first, il.Create(OpCodes.Call, game.MainModule.ImportReference(prefix)));
        hooks.Add(add.FullName + " <- " + prefix.FullName);
    }
}
