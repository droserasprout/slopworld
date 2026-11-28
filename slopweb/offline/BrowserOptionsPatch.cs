// Wire the mod's overloaded settings filters and footer into staged Dialog_Options.
// Desktop window sizing and runtime Harmony discovery remain outside this boundary.
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BrowserOptionsPatch
{
    internal static void Apply(AssemblyDefinition game, AssemblyDefinition mod, List<string> hooks)
    {
        ApplyRowFilters(game, mod, hooks);
        var dialog = game.MainModule.GetType("RimWorld.Dialog_Options");
        var contents = dialog.Methods.Single(m => m.Name == "DoWindowContents");
        var owner = mod.MainModule.GetType("SlopWorld.ModOptions/Patch_OptionsOk");
        var footer = owner.Methods.Single(m => m.Name == "FooterButton");
        owner.IsNestedPublic = true;
        footer.IsPublic = true;
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

    // Invoke the same translated-label policy as Harmony, before listing layout advances.
    private static void ApplyRowFilters(
        AssemblyDefinition game,
        AssemblyDefinition mod,
        List<string> hooks
    )
    {
        var listing = game.MainModule.GetType("Verse.Listing_Standard");
        foreach (
            var (name, patch) in new[]
            {
                ("CheckboxLabeled", "Patch_OptionCheckbox"),
                ("ButtonTextLabeledPct", "Patch_OptionButton"),
                ("Label", "Patch_OptionLabel"),
                ("SliderLabeled", "Patch_OptionSlider"),
            }
        )
        {
            var prefix = mod
                .MainModule.GetType("SlopWorld.StripOptions/" + patch)
                .Methods.Single(m => m.Name == "Prefix");
            var target = listing.Methods.Single(m =>
                m.Name == name
                && (name != "CheckboxLabeled" || m.Parameters.Count == 5)
                && (
                    name != "Label"
                    || m.Parameters.Count == 3
                        && m.Parameters[0].ParameterType.FullName == "Verse.TaggedString"
                )
            );
            prefix.IsPublic = true;
            var il = target.Body.GetILProcessor();
            var first = target.Body.Instructions[0];
            VariableDefinition result = null;
            if (target.ReturnType.FullName != "System.Void")
            {
                result = new VariableDefinition(target.ReturnType);
                target.Body.Variables.Add(result);
                target.Body.InitLocals = true;
            }
            foreach (var parameter in prefix.Parameters)
            {
                if (parameter.Name == "__result")
                {
                    if (
                        result == null
                        || parameter.ParameterType.FullName != target.ReturnType.FullName + "&"
                    )
                        throw new InvalidOperationException("Options result contract changed");
                    il.InsertBefore(first, il.Create(OpCodes.Ldloca, result));
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
            if (result != null)
                il.InsertBefore(first, il.Create(OpCodes.Ldloc, result));
            il.InsertBefore(first, il.Create(OpCodes.Ret));
            hooks.Add(target.FullName + " <- " + prefix.FullName);
        }
    }
}
