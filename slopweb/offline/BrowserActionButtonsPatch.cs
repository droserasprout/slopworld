// Retain the native caption bounds and the mod's action-only backdrop policy.
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BrowserActionButtonsPatch
{
    internal static void Apply(AssemblyDefinition game, AssemblyDefinition mod, List<string> hooks)
    {
        var target = game
            .MainModule.GetType("Verse.Command")
            .Methods.Single(m => m.Name == "GizmoOnGUIInt");
        var replacement = mod
            .MainModule.GetType("SlopWorld.Patch_SlopCommandCaptionBackground")
            .Methods.Single(m => m.Name == "DrawBackground");
        var call = target.Body.Instructions.Single(i =>
            i.OpCode == OpCodes.Call
            && i.Operand is MethodReference m
            && m.DeclaringType.FullName == "UnityEngine.GUI"
            && m.Name == "DrawTexture"
            && m.Parameters.Count == 2
            && i.Previous.OpCode == OpCodes.Ldsfld
            && i.Previous.Operand is FieldReference field
            && field.DeclaringType.FullName == "Verse.TexUI"
            && field.Name == "GrayTextBG"
            && i.Previous.Previous.OpCode == OpCodes.Dup
        );
        replacement.DeclaringType.IsPublic = true;
        replacement.IsPublic = true;
        // Keep incoming branches aimed at the extra argument as well as the call.
        call.OpCode = OpCodes.Ldarg_0;
        call.Operand = null;
        target
            .Body.GetILProcessor()
            .InsertAfter(
                call,
                Instruction.Create(OpCodes.Call, game.MainModule.ImportReference(replacement))
            );
        foreach (var instruction in target.Body.Instructions)
            if (instruction.OpCode.OperandType == OperandType.ShortInlineBrTarget)
                instruction.OpCode = (OpCode)
                    typeof(OpCodes)
                        .GetFields()
                        .Single(f =>
                            f.FieldType == typeof(OpCode)
                            && ((OpCode)f.GetValue(null)!).Name == instruction.OpCode.Name[..^2]
                        )
                        .GetValue(null)!;
        hooks.Add(target.FullName + " caption <- " + replacement.FullName);
    }
}
