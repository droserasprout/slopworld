using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HarmonyLib;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class HarmonyRuntimeTests
    {
        [Test]
        public void BundledHarmonyPatchesAndRestoresManagedMethodsRepeatedly()
        {
            TestContext.Progress.WriteLine("CLR: " + RuntimeInformation.FrameworkDescription);
            var harmony = new Harmony("SlopWorld.Tests.Runtime");
            for (int i = 0; i < 2; i++)
            {
                Assert.That(Target(2), Is.EqualTo(3));
                try
                {
                    harmony.Patch(
                        AccessTools.Method(typeof(HarmonyRuntimeTests), nameof(Target)),
                        prefix: new HarmonyMethod(typeof(HarmonyRuntimeTests), nameof(Prefix)),
                        postfix: new HarmonyMethod(typeof(HarmonyRuntimeTests), nameof(Postfix))
                    );
                    Assert.That(Target(2), Is.EqualTo(12));
                }
                finally
                {
                    harmony.UnpatchAll(harmony.Id);
                }
                Assert.That(Target(2), Is.EqualTo(3));
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int Target(int value) => value + 1;

        static void Prefix(ref int value) => value += 3;

        static void Postfix(ref int __result) => __result *= 2;
    }
}
