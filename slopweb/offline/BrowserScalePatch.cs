using Mono.Cecil;
using Mono.Cecil.Cil;

// Browser density is already included in its viewport. Preserve native
// preference accessors while removing duplicate desktop scale inference.
internal static class BrowserScalePatch
{
    internal static void Apply(AssemblyDefinition game)
    {
        var module = game.MainModule;
        var resolution = module.GetType("RimWorld.ResolutionUtility");
        var resolve = resolution.Methods.SingleOrDefault(m => m.Name == "ResolveUIScale");
        if (resolve is not null)
        {
            if (
                !resolve.IsStatic
                || resolve.Parameters.Count != 1
                || resolve.Parameters[0].ParameterType.FullName != "System.Single"
                || resolve.ReturnType.FullName != "System.Single"
            )
                throw new InvalidOperationException("Browser UI scale boundary changed");
            resolve.Body = new MethodBody(resolve);
            resolve.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            resolve.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            return;
        }

        // 5011 applies desktop DPI in both UIScale accessors instead of ResolveUIScale.
        var dpi = resolution.Methods.Single(m => m.Name == "get_DpiScale");
        var prefs = module.GetType("Verse.Prefs");
        var scale = prefs.Properties.Single(p => p.Name == "UIScale");
        bool UsesDpi(MethodDefinition accessor) =>
            accessor is { IsStatic: true, HasBody: true }
            && accessor.Body.Instructions.Any(i =>
                i.OpCode == OpCodes.Call
                && i.Operand is MethodReference method
                && method.FullName == dpi.FullName
            );
        if (
            !dpi.IsStatic
            || dpi.Parameters.Count != 0
            || dpi.ReturnType.FullName != "System.Single"
            || scale.PropertyType.FullName != "System.Single"
            || !UsesDpi(scale.GetMethod)
            || !UsesDpi(scale.SetMethod)
        )
            throw new InvalidOperationException("Browser UI scale preference boundary changed");
        dpi.Body = new MethodBody(dpi);
        dpi.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_R4, 1f));
        dpi.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    }
}
