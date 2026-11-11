using Mono.Cecil;
using Mono.Cecil.Cil;

// Preserve the native cursor pipeline through its final Unity presentation call.
internal static class BrowserCursorPatch
{
    internal static void Apply(AssemblyDefinition mod, string root)
    {
        using var adapters = AssemblyDefinition.ReadAssembly(Path.Combine(root, "Adapters.dll"));
        var adapter = adapters.MainModule.GetType("BrowserAdapters.BrowserCursor");
        BrowserClipboardPatch.Redirect(
            mod.MainModule.GetType("SlopWorld.TextureReadback"),
            adapter,
            "ReadBack",
            "ReadBack"
        );
        var show = mod
            .MainModule.GetType("SlopWorld.CursorTextures")
            .Methods.Single(m => m.Name == "Show");
        var calls = show
            .Body.Instructions.Where(i =>
                i.OpCode == OpCodes.Call
                && i.Operand is MethodReference m
                && m.DeclaringType.FullName == "UnityEngine.Cursor"
                && m.Name == "SetCursor"
            )
            .ToArray();
        if (calls.Length != 2)
            throw new InvalidOperationException("Native cursor presentation boundary changed");
        var replacement = adapter.Methods.Single(m => m.Name == "SetCursor");
        foreach (var call in calls)
        {
            var original = (MethodReference)call.Operand;
            if (
                original.ReturnType.FullName != replacement.ReturnType.FullName
                || !original
                    .Parameters.Select(p => p.ParameterType.FullName)
                    .SequenceEqual(replacement.Parameters.Select(p => p.ParameterType.FullName))
            )
                throw new InvalidOperationException("Native cursor signature changed");
            call.Operand = mod.MainModule.ImportReference(replacement);
        }
    }
}
