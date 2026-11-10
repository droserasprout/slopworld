// Keep the mod's shortcut policy and focus guards, admitting its Entry workspace
// alongside Playing. No global program-state value is altered.
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BrowserKeysPatch
{
    internal static void Apply(AssemblyDefinition mod, string root)
    {
        using var adapters = AssemblyDefinition.ReadAssembly(Path.Combine(root, "Adapters.dll"));
        var helper = adapters
            .MainModule.GetType("BrowserAdapters.BrowserModKeys")
            .Methods.Single(m => m.Name == "WorkspaceState");
        var prefix = mod
            .MainModule.GetType("SlopWorld.Patch_WorkspaceShortcuts")
            .Methods.Single(m => m.Name == "Prefix");
        var state = prefix.Body.Instructions.Single(i =>
            i.Operand is MethodReference m
            && m.DeclaringType.FullName == "Verse.Current"
            && m.Name == "get_ProgramState"
        );
        // Replace just the state comparison's operands with a predicate and true.
        // Guard the inspected enum constant so an upstream policy change fails staging.
        if (state.Next.OpCode != OpCodes.Ldc_I4_2)
            throw new InvalidOperationException("Workspace shortcut state guard changed");
        state.Next.OpCode = OpCodes.Ldc_I4_1;
        prefix
            .Body.GetILProcessor()
            .InsertAfter(
                state,
                Instruction.Create(OpCodes.Call, mod.MainModule.ImportReference(helper))
            );
        prefix.IsPublic = true;
    }
}
