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
            ConstructorInfo timing,
            FieldInfo update,
            MethodInfo admit,
            MethodInfo nextPresent,
            MethodInfo throttle,
            MethodInfo finish
        )
        {
            var code = instructions.Select(i => new CodeInstruction(i)).ToList();
            int timingIndex = code.FindIndex(i => Equals(i.operand, timing));
            // Replay-aware engines append this.ReplayFrame to the timing arguments.
            // Keep admission before the entire constructor stack is populated.
            int start = timingIndex - 7;
            int updateIndex = code.FindIndex(i => i.LoadsField(update));
            int end = code.FindLastIndex(i => i.opcode == OpCodes.Ret);
            if (
                timing == null
                || timing.GetParameters().Length != 4
                || timingIndex < 7
                || code.Count(i => Equals(i.operand, timing)) != 1
                || code[timingIndex].opcode != OpCodes.Call
                || (code[start].opcode != OpCodes.Ldloca && code[start].opcode != OpCodes.Ldloca_S)
                || code[start + 1].opcode != OpCodes.Call
                || code[start + 4].opcode != OpCodes.Conv_R8
                || code[start + 5].opcode != OpCodes.Ldarg_0
                || code[start + 6].operand is not MethodInfo replayGetter
                || replayGetter.Name != "get_ReplayFrame"
                || replayGetter.ReturnType != typeof(bool)
                || replayGetter.GetParameters().Length != 0
                || !code[start + 6].Calls(replayGetter)
                || updateIndex < 0
                || updateIndex >= start
                || nextPresent == null
                || nextPresent.IsStatic
                || nextPresent.ReturnType != typeof(long)
                || nextPresent.GetParameters().Length != 0
                || code.Count(i => i.Calls(nextPresent)) != 1
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
                if (instruction.Calls(nextPresent))
                {
                    // RenderSchedule owns admission. Native deadlines advance on every
                    // service iteration, including skipped captures, and would push both
                    // game time and queued presentation arbitrarily into the future.
                    // Zero selects the engine's wall clock and immediate presentation.
                    instruction.opcode = OpCodes.Pop;
                    instruction.operand = null;
                    yield return instruction;
                    yield return new CodeInstruction(OpCodes.Ldc_I8, 0L);
                    continue;
                }
                if (instruction.Calls(throttle))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = finish;
                }
                yield return instruction;
            }
        }
    }
}
