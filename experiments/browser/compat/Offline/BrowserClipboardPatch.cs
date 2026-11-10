using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BrowserClipboardPatch
{
    internal static void Redirect(
        TypeDefinition owner,
        TypeDefinition adapter,
        string name,
        string replacement
    )
    {
        var method = owner.Methods.Single(m => m.Name == name);
        var target = adapter.Methods.Single(m => m.Name == replacement);
        if (
            !method.IsStatic
            || !target.IsStatic
            || method.ReturnType.FullName != target.ReturnType.FullName
            || !method
                .Parameters.Select(p => p.ParameterType.FullName)
                .SequenceEqual(target.Parameters.Select(p => p.ParameterType.FullName))
        )
            throw new InvalidOperationException("Clipboard boundary changed: " + method.FullName);
        method.Body = new MethodBody(method);
        var il = method.Body.GetILProcessor();
        foreach (var parameter in method.Parameters)
            il.Emit(OpCodes.Ldarg, parameter);
        il.Emit(OpCodes.Call, owner.Module.ImportReference(target));
        il.Emit(OpCodes.Ret);
    }

    internal static void Apply(AssemblyDefinition mod, string root)
    {
        using var adapters = AssemblyDefinition.ReadAssembly(Path.Combine(root, "Adapters.dll"));
        var adapter = adapters.MainModule.GetType("BrowserAdapters.BrowserClipboard");
        var clipboard = mod.MainModule.GetType("SlopWorld.DesktopClipboard");
        Redirect(clipboard, adapter, "get_UsesX11", "UsesX11");
        foreach (
            var name in new[]
            {
                "ReadLocalPaste",
                "ReadLocal",
                "Copy",
                "CopyPrimary",
                "CopyFromApplication",
            }
        )
            Redirect(clipboard, adapter, name, name);
    }
}
