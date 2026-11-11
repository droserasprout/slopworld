// Preserve native window intent callers while replacing only the browser boundary.
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BrowserWindowPatch
{
    internal static void Apply(AssemblyDefinition mod, string root)
    {
        var browser = mod
            .MainModule.GetType("SlopWorld.ClientPlatform")
            .Methods.Single(m => m.Name == "get_IsBrowser");
        if (
            !browser.IsStatic
            || browser.Parameters.Count != 0
            || browser.ReturnType.FullName != "System.Boolean"
        )
            throw new InvalidOperationException("Browser capability contract changed");
        browser.Body = new MethodBody(browser);
        browser.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        browser.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        using var adapters = AssemblyDefinition.ReadAssembly(Path.Combine(root, "Adapters.dll"));
        BrowserClipboardPatch.Redirect(
            mod.MainModule.GetType("SlopWorld.LinuxGameWindow"),
            adapters.MainModule.GetType("BrowserAdapters.BrowserWindow"),
            "Set",
            "Set"
        );
    }
}
