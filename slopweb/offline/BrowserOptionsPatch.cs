// Wire the mod's existing settings row/page owners into staged Dialog_Options.
// Desktop window sizing and runtime Harmony discovery remain outside this boundary.
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BrowserOptionsPatch
{
    internal static void Apply(AssemblyDefinition game, AssemblyDefinition mod, List<string> hooks)
    {
        var dialog = game.MainModule.GetType("RimWorld.Dialog_Options");
        MethodDefinition Expose(string typeName, string methodName)
        {
            var type = mod.MainModule.GetType("SlopWorld.ModOptions/" + typeName);
            var method = type.Methods.Single(m => m.Name == methodName);
            type.IsNestedPublic = true;
            method.IsPublic = true;
            return method;
        }
        foreach (
            var (targetName, patchName) in new[]
            {
                ("DoCategoryRow", "Patch_OptionsRow"),
                ("DoOptions", "Patch_OptionsPage"),
            }
        )
        {
            var target = dialog.Methods.Single(m => m.Name == targetName);
            var prefix = Expose(patchName, "Prefix");
            if (
                target.ReturnType.FullName != "System.Void"
                || prefix.ReturnType.FullName != "System.Boolean"
            )
                throw new InvalidOperationException("Options hook return contract changed");
            var il = target.Body.GetILProcessor();
            var first = target.Body.Instructions[0];
            foreach (var parameter in prefix.Parameters)
            {
                if (parameter.Name == "__instance")
                {
                    if (target.IsStatic || parameter.ParameterType.FullName != dialog.FullName)
                        throw new InvalidOperationException("Options instance contract changed");
                    il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
                }
                else
                {
                    var argument = target.Parameters.Single(p =>
                        p.Name == parameter.Name
                        && p.ParameterType.FullName == parameter.ParameterType.FullName
                    );
                    il.InsertBefore(first, il.Create(OpCodes.Ldarg, argument));
                }
            }
            il.InsertBefore(
                first,
                il.Create(OpCodes.Call, game.MainModule.ImportReference(prefix))
            );
            il.InsertBefore(first, il.Create(OpCodes.Brtrue, first));
            il.InsertBefore(first, il.Create(OpCodes.Ret));
            hooks.Add(target.FullName + " <- " + prefix.FullName);
        }
        var contents = dialog.Methods.Single(m => m.Name == "DoWindowContents");
        var footer = Expose("Patch_OptionsOk", "FooterButton");
        var call = contents.Body.Instructions.Single(i =>
            i.Operand is MethodReference m
            && m.DeclaringType.FullName == "Verse.Widgets"
            && m.Name == "ButtonText"
        );
        var original = (MethodReference)call.Operand;
        if (
            original.ReturnType.FullName != footer.ReturnType.FullName
            || !original
                .Parameters.Select(p => p.ParameterType.FullName)
                .SequenceEqual(footer.Parameters.Select(p => p.ParameterType.FullName))
        )
            throw new InvalidOperationException("Options footer contract changed");
        call.Operand = game.MainModule.ImportReference(footer);
        hooks.Add(contents.FullName + " footer <- " + footer.FullName);
    }
}
