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
