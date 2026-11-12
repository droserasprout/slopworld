using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BrowserTerminalSizePatch
{
    internal static void Apply(AssemblyDefinition mod, string root)
    {
        using var adapters = AssemblyDefinition.ReadAssembly(Path.Combine(root, "Adapters.dll"));
        var allow = adapters
            .MainModule.GetType("BrowserAdapters.BrowserTerminalSize")
            .Methods.Single(m => m.Name == "Allow");
        var resize = mod
            .MainModule.GetType("SlopWorld.TerminalIO")
            .Methods.Single(m => m.Name == "Resize");
        if (
            resize.IsStatic
            || resize.ReturnType.FullName != "System.Void"
            || !resize
                .Parameters.Select(p => p.ParameterType.FullName)
                .SequenceEqual(new[] { "System.String", "System.Int32", "System.Int32" })
        )
            throw new InvalidOperationException("Terminal resize boundary changed");
        var first = resize.Body.Instructions[0];
        var il = resize.Body.GetILProcessor();
        foreach (var parameter in resize.Parameters)
            il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg, parameter));
        il.InsertBefore(
            first,
            Instruction.Create(OpCodes.Call, mod.MainModule.ImportReference(allow))
        );
        il.InsertBefore(first, Instruction.Create(OpCodes.Brtrue, first));
        il.InsertBefore(first, Instruction.Create(OpCodes.Ret));
    }
}
