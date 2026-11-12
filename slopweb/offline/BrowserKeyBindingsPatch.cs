// Keep the installed XML save and all callers; publish only authored bindings to
// the browser sink after the native save has settled in the ephemeral WASM FS.
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BrowserKeyBindingsPatch
{
    internal static void Apply(AssemblyDefinition game, string root)
    {
        using var adapters = AssemblyDefinition.ReadAssembly(Path.Combine(root, "Adapters.dll"));
        var helper = adapters
            .MainModule.GetType("BrowserAdapters.BrowserKeyBindings")
            .Methods.Single(m => m.Name == "Save");
        var save = game.MainModule.GetType("Verse.KeyPrefs").Methods.Single(m => m.Name == "Save");
        if (
            !save.IsStatic
            || save.Parameters.Count != 0
            || save.ReturnType.FullName != "System.Void"
            || !save.HasBody
            || !helper.IsStatic
            || helper.Parameters.Count != 0
            || helper.ReturnType.FullName != "System.Void"
        )
            throw new InvalidOperationException("Browser key save boundary changed");
        if (
            save.Body.Instructions.Any(i =>
                i.Operand is MethodReference m
                && m.DeclaringType.FullName == helper.DeclaringType.FullName
            )
        )
            throw new InvalidOperationException("Duplicate browser key save hook");
        // Replace each return itself so branches/leave targets execute the hook.
        foreach (
            var instruction in save.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray()
        )
        {
            instruction.OpCode = OpCodes.Call;
            instruction.Operand = game.MainModule.ImportReference(helper);
            save.Body.GetILProcessor().InsertAfter(instruction, Instruction.Create(OpCodes.Ret));
        }
    }
}
