// Own browser presentation persistence and event-driven font notifications.
// Native save timing remains intact; dirty events schedule font work separately.
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BrowserPreferencesPatch
{
    internal static void Apply(AssemblyDefinition mod, string root)
    {
        using var adapters = AssemblyDefinition.ReadAssembly(Path.Combine(root, "Adapters.dll"));
        var target = adapters
            .MainModule.GetType("BrowserAdapters.BrowserPreferences")
            .Methods.Single(m => m.Name == "Write");
        var write = mod
            .MainModule.GetType("SlopWorld.ProfileSettings")
            .Methods.Single(m => m.Name == "Write");
        if (
            !write.IsStatic
            || write.ReturnType.FullName != "System.Void"
            || write.Parameters.Count != 1
            || write.Parameters[0].ParameterType.FullName != "SlopWorld.ModSettings"
            || !target.IsStatic
            || target.ReturnType.FullName != "System.Void"
            || target.Parameters.Count != 1
            || target.Parameters[0].ParameterType.FullName != "System.Object"
        )
            throw new InvalidOperationException("Browser profile save boundary changed");
        var dirty = mod
            .MainModule.GetType("SlopWorld.ModSettings")
            .Methods.Single(m => m.Name == "MarkDirty");
        if (dirty.IsStatic || dirty.Parameters.Count != 0 || !dirty.HasBody)
            throw new InvalidOperationException("Browser settings change boundary changed");
        var dirtyIl = dirty.Body.GetILProcessor();
        var first = dirty.Body.Instructions[0];
        dirtyIl.InsertBefore(first, dirtyIl.Create(OpCodes.Ldarg_0));
        dirtyIl.InsertBefore(
            first,
            dirtyIl.Create(
                OpCodes.Call,
                mod.MainModule.ImportReference(
                    adapters
                        .MainModule.GetType("BrowserAdapters.BrowserPreferences")
                        .Methods.Single(m => m.Name == "Changed")
                )
            )
        );
        write.Body = new MethodBody(write);
        var il = write.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, mod.MainModule.ImportReference(target));
        il.Emit(OpCodes.Ret);
    }
}
