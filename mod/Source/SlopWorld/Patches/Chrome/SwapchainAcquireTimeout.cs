using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace SlopWorld
{
    // A hidden fullscreen swapchain can stop supplying images. Bound acquisition so the render
    // queue can drain and the main thread can keep dispatching the window events that restore it.
    static class SwapchainAcquireTimeout
    {
        internal const int Nanoseconds = 16_000_000;

        internal static IEnumerable<CodeInstruction> Rewrite(
            IEnumerable<CodeInstruction> instructions
        )
        {
            var code = instructions.ToList();
            var calls = code.Select((instruction, index) => (instruction, index))
                .Where(item =>
                    item.instruction.operand is MethodInfo method
                    && method.DeclaringType?.FullName == "Brutal.VulkanApi.VkDeviceExtensions"
                    && method.Name == "AcquireNextImageKHR"
                )
                .ToList();
            var waits = code.Select((instruction, index) => (instruction, index))
                .Where(item =>
                    item.instruction.opcode == OpCodes.Ldc_I4_M1
                    && item.index + 1 < code.Count
                    && code[item.index + 1].opcode == OpCodes.Conv_I
                )
                .ToList();
            // Waylith 5013 retries timed-out acquisitions until a separate deadline.
            // Keep the 1 ms resize attempt and return the first timeout to the engine.
            if (calls.Count == 1 && waits.Count == 0)
            {
                int call = calls[0].index;
                int normal = code.FindIndex(i => i.LoadsConstant(100_000_000));
                int resize = code.FindIndex(i => i.LoadsConstant(1_000_000));
                int retry = code.FindIndex(
                    call,
                    i =>
                        (i.opcode == OpCodes.Blt_S || i.opcode == OpCodes.Blt)
                        && i.operand is Label target
                        && code.Take(call).Any(c => c.labels.Contains(target))
                );
                if (
                    normal < 0
                    || resize <= normal
                    || resize + 1 >= call
                    || code[normal + 1].opcode != OpCodes.Conv_I
                    || code[resize + 1].opcode != OpCodes.Conv_I
                    || retry < 0
                )
                    throw new InvalidOperationException(
                        "Unexpected Waylith acquisition retry loop"
                    );

                code[normal].operand = Nanoseconds;
                // Consume the timestamp and deadline, then fall through to native timeout cleanup.
                code[retry].opcode = OpCodes.Pop;
                code[retry].operand = null;
                code.Insert(retry + 1, new CodeInstruction(OpCodes.Pop));
                return code;
            }
            if (calls.Count != 1 || waits.Count != 1 || waits[0].index >= calls[0].index)
                throw new InvalidOperationException(
                    "Unexpected RimWorld 1.7 swapchain acquisition IL"
                );

            // Preserve the native-width conversion and instruction labels/exception boundaries.
            // Vulkan TIMEOUT follows RenderContext's existing skipped-frame path: it does not
            // reset the submission fence or submit a frame without an acquired image.
            waits[0].instruction.opcode = OpCodes.Ldc_I4;
            waits[0].instruction.operand = Nanoseconds;
            return code;
        }
    }

    [HarmonyPatch]
    static class Patch_SwapchainAcquireTimeout
    {
        static bool Prepare() => OperatingSystem.IsLinux();

        static MethodBase TargetMethod() =>
            AccessTools.Method(
                AccessTools.TypeByName("Waylith.Graphics.WindowRenderContext"),
                "GetNextFrameIndex"
            );

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            SwapchainAcquireTimeout.Rewrite(instructions);
    }
}
