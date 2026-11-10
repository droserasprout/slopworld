using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BrowserSidebarPatch
{
    internal static void Apply(AssemblyDefinition mod, string root)
    {
        using var adapters = AssemblyDefinition.ReadAssembly(Path.Combine(root, "Adapters.dll"));
        var adapter = adapters.MainModule.GetType("BrowserAdapters.BrowserSidebar");
        var strip = mod.MainModule.GetType("SlopWorld.ColonistBarStrip");
        foreach (var (name, replacement) in new[] { ("Draw", "Draw"), ("get_BarShown", "Visible") })
        {
            var method = strip.Methods.Single(m => m.Name == name);
            var impl = adapter.Methods.Single(m => m.Name == replacement);
            if (
                method.Parameters.Count != impl.Parameters.Count
                || method.ReturnType.FullName != impl.ReturnType.FullName
            )
                throw new InvalidOperationException("Sidebar boundary changed");
            method.Body = new MethodBody(method);
            var il = method.Body.GetILProcessor();
            foreach (var parameter in method.Parameters)
                il.Emit(OpCodes.Ldarg, parameter);
            il.Emit(OpCodes.Call, mod.MainModule.ImportReference(impl));
            il.Emit(OpCodes.Ret);
        }
        foreach (
            var (typeName, methodName) in new[]
            {
                ("SlopWorld.SessionSelectable", "set_Current"),
                ("SlopWorld.SessionGizmoSelection", "SyncFromMapSelection"),
                ("SlopWorld.WorkspaceHotkeys", "SelectedLive"),
            }
        )
        {
            var method = mod.MainModule.GetType(typeName).Methods.Single(m => m.Name == methodName);
            var getter = method.Body.Instructions.Single(i =>
                i.Operand is MethodReference m
                && m.DeclaringType.FullName == "Verse.Find"
                && m.Name == "get_Selector"
            );
            getter.Operand = mod.MainModule.ImportReference(
                adapter.Methods.Single(m => m.Name == "MapSelection")
            );
        }
        // Admit ordinary agent metadata to the existing session-only row path.
        // This modifies a single eligibility read, not SessionInfo or its wire data.
        var bucket = mod
            .MainModule.GetType("SlopWorld.Sidebar")
            .Methods.Single(m => m.Name == "BucketRuntimeSessions");
        var site = bucket.Body.Instructions.Single(i =>
            i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference f && f.Name == "Ephemeral"
        );
        site.OpCode = OpCodes.Pop;
        site.Operand = null;
        bucket.Body.GetILProcessor().InsertAfter(site, Instruction.Create(OpCodes.Ldc_I4_1));
    }
}
