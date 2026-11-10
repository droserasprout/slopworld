using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using NUnit.Framework;

namespace Brutal.VulkanApi
{
    static class VkDeviceExtensions
    {
        public static int LastTimeout;
        public static bool Available;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int AcquireNextImageKHR(nint timeout)
        {
            LastTimeout = (int)timeout;
            return Available ? 0 : 2; // VK_SUCCESS / VK_TIMEOUT
        }
    }
}

namespace SlopWorld.Tests
{
    [TestFixture]
    public class SwapchainAcquireTimeoutTests
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        static int Acquire()
        {
            // Keep the same infinite native-width timeout sequence as the game's target.
            return Brutal.VulkanApi.VkDeviceExtensions.AcquireNextImageKHR((nint)(-1));
        }

        [Test]
        public void PatchedAcquireReturnsTimeoutForUnavailableImagesAndResumesWhenAvailable()
        {
            var harmony = new Harmony("SlopWorld.Tests.SwapchainAcquire");
            var acquire = AccessTools.Method(typeof(SwapchainAcquireTimeoutTests), nameof(Acquire));
            bool available = Brutal.VulkanApi.VkDeviceExtensions.Available;
            int timeout = Brutal.VulkanApi.VkDeviceExtensions.LastTimeout;
            try
            {
                harmony.Patch(
                    acquire,
                    transpiler: new HarmonyMethod(
                        AccessTools.Method(
                            typeof(SwapchainAcquireTimeout),
                            nameof(SwapchainAcquireTimeout.Rewrite)
                        )
                    )
                );
                for (int i = 0; i < 3; i++)
                {
                    Brutal.VulkanApi.VkDeviceExtensions.Available = false;
                    Assert.That(Acquire(), Is.EqualTo(2));
                    Assert.That(
                        Brutal.VulkanApi.VkDeviceExtensions.LastTimeout,
                        Is.EqualTo(SwapchainAcquireTimeout.Nanoseconds)
                    );
                    Brutal.VulkanApi.VkDeviceExtensions.Available = true;
                    Assert.That(Acquire(), Is.Zero);
                }
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
                Brutal.VulkanApi.VkDeviceExtensions.Available = available;
                Brutal.VulkanApi.VkDeviceExtensions.LastTimeout = timeout;
            }
        }

        [Test]
        public void RewritePreservesLabelsNativeConversionAndOtherInstructions()
        {
            var label = new DynamicMethod("label", typeof(void), Type.EmptyTypes)
                .GetILGenerator()
                .DefineLabel();
            var wait = new CodeInstruction(OpCodes.Ldc_I4_M1);
            wait.labels.Add(label);
            var conversion = new CodeInstruction(OpCodes.Conv_I);
            var call = new CodeInstruction(
                OpCodes.Call,
                AccessTools.Method(
                    typeof(Brutal.VulkanApi.VkDeviceExtensions),
                    "AcquireNextImageKHR"
                )
            );
            var negative = new CodeInstruction(OpCodes.Ldc_I4_M1);
            var code = SwapchainAcquireTimeout
                .Rewrite(new[] { wait, conversion, call, negative })
                .ToArray();
            Assert.That(code[0].opcode, Is.EqualTo(OpCodes.Ldc_I4));
            Assert.That(code[0].operand, Is.EqualTo(SwapchainAcquireTimeout.Nanoseconds));
            Assert.That(code[0].labels, Does.Contain(label));
            Assert.That(code[1], Is.SameAs(conversion));
            Assert.That(code[2], Is.SameAs(call));
            Assert.That(negative.opcode, Is.EqualTo(OpCodes.Ldc_I4_M1));
        }

        [Test]
        public void UnexpectedTargetFailsBeforeChangingAnyInstructions()
        {
            var wait = new CodeInstruction(OpCodes.Ldc_I4_M1);
            Assert.Throws<InvalidOperationException>(() =>
                SwapchainAcquireTimeout.Rewrite(new[] { wait, new CodeInstruction(OpCodes.Conv_I) })
            );
            Assert.That(wait.opcode, Is.EqualTo(OpCodes.Ldc_I4_M1));
        }
    }
}
