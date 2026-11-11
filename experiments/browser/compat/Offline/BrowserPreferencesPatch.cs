// Replace only the profile's persistence sink. Native dirty timing and callers
// remain intact; browser writes never serialize the full TOML profile.
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
        write.Body = new MethodBody(write);
        var il = write.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, mod.MainModule.ImportReference(target));
        il.Emit(OpCodes.Ret);
    }
}
