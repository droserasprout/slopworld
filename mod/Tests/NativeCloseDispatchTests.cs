using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class NativeCloseDispatchTests
    {
        sealed class Window
        {
            public bool Close;
            public bool ShouldClose
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                get => Close;
            }
        }

        sealed class Engine
        {
            internal readonly Window Window = new Window();
            internal bool Accepted;
            internal int Requests;

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal bool LoopExitDecision() => Window.ShouldClose;
        }

        static bool Guard(bool close, object instance)
        {
            var engine = (Engine)instance;
            if (!close || engine.Accepted)
                return close;
            engine.Requests++;
            engine.Window.Close = false;
            return false;
        }

        static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> code) =>
            NativeCloseDispatch.Rewrite(
                code,
                AccessTools.PropertyGetter(typeof(Window), nameof(Window.ShouldClose)),
                AccessTools.Method(typeof(NativeCloseDispatchTests), nameof(Guard))
            );

        [Test]
        public void PatchedLoopKeepsUpdatingUntilCloseIsAcceptedAndUsesItsOwnEngine()
        {
            var harmony = new Harmony("SlopWorld.Tests.NativeClose");
            try
            {
                harmony.Patch(
                    AccessTools.Method(typeof(Engine), nameof(Engine.LoopExitDecision)),
                    transpiler: new HarmonyMethod(
                        AccessTools.Method(typeof(NativeCloseDispatchTests), nameof(Rewrite))
                    )
                );
                var engine = new Engine();
                var other = new Engine();
                Assert.That(engine.LoopExitDecision(), Is.False);
                engine.Window.Close = true;
                Assert.That(engine.LoopExitDecision(), Is.False);
                Assert.That(engine.Window.Close, Is.False);
                Assert.That(engine.Requests, Is.EqualTo(1));
                Assert.That(other.Requests, Is.Zero);
                Assert.That(engine.LoopExitDecision(), Is.False);
                engine.Window.Close = true;
                Assert.That(engine.LoopExitDecision(), Is.False);
                engine.Accepted = true;
                engine.Window.Close = true;
                Assert.That(engine.LoopExitDecision(), Is.True);
                Assert.That(engine.Window.Close, Is.True);
                Assert.That(engine.Requests, Is.EqualTo(2));
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        [Test]
        public void MissingCloseFlagReadRejectsUnsupportedLoop()
        {
            Assert.Throws<InvalidOperationException>(() =>
                Rewrite(Array.Empty<CodeInstruction>()).ToArray()
            );
        }
    }
}
