using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace SlopWorld
{
    // Gate only frame capture after game/input updates. The native outer finally
    // still closes every update, including iterations which produce no GPU frame.
    internal static class RenderLoopRewrite
    {
        internal static IEnumerable<CodeInstruction> Rewrite(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator,
            MethodInfo poll,
            MethodInfo pump,
            ConstructorInfo timing,
            FieldInfo update,
            MethodInfo admit,
            MethodInfo throttle,
            MethodInfo finish
        )
        {
            var code = instructions.Select(i => new CodeInstruction(i)).ToList();
            int timingIndex = code.FindIndex(i => Equals(i.operand, timing));
            int start = timingIndex - 5;
            int updateIndex = code.FindIndex(i => i.LoadsField(update));
            int pollIndex = code.FindIndex(i => i.Calls(poll));
            int end = code.FindLastIndex(i => i.opcode == OpCodes.Ret);
            if (
                timingIndex < 5
                || code.Count(i => Equals(i.operand, timing)) != 1
                || code[timingIndex].opcode != OpCodes.Call
                || (code[start].opcode != OpCodes.Ldloca && code[start].opcode != OpCodes.Ldloca_S)
                || code[start + 1].opcode != OpCodes.Call
                || code[start + 4].opcode != OpCodes.Conv_R8
                || updateIndex <= pollIndex
                || updateIndex >= start
                || pollIndex < 0
                || code.Count(i => i.Calls(poll)) != 1
                || !code.Any(i => i.Calls(throttle))
                || end <= timingIndex
                || code[start].blocks.Count != 0
            )
                throw new InvalidOperationException("Unexpected RimWorld 1.7 render loop IL");

            var done = generator.DefineLabel();
            var render = generator.DefineLabel();
            code[end].labels.Add(done);
            for (int index = 0; index < code.Count; index++)
            {
                var instruction = code[index];
                if (index == start)
                {
                    var instance = new CodeInstruction(OpCodes.Ldarg_0);
                    instance.MoveLabelsFrom(instruction);
                    instruction.labels.Add(render);
                    yield return instance;
                    yield return new CodeInstruction(OpCodes.Call, admit);
                    yield return new CodeInstruction(OpCodes.Brtrue, render);
                    yield return new CodeInstruction(OpCodes.Leave, done);
                }
                if (instruction.Calls(poll))
                {
                    var instance = new CodeInstruction(OpCodes.Ldarg_0);
                    instance.MoveLabelsFrom(instruction);
                    instance.MoveBlocksFrom(instruction);
                    yield return instance;
                    instruction.operand = pump;
                }
                else if (instruction.Calls(throttle))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = finish;
                }
                yield return instruction;
            }
        }
    }
}
