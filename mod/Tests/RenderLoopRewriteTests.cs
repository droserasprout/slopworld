using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class RenderLoopRewriteTests
    {
        static readonly List<string> Events = new();
        static bool _render;

        struct Timing
        {
            internal int Frame;

            internal Timing(int frame, float delta, double elapsed) => Frame = frame;
        }

        sealed class Loop
        {
            internal Action OnUpdate;
            internal bool Visible = true;
            internal bool FailCapture;
            static int FrameCount => 1;

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal void Run(float delta, float elapsed)
            {
                try
                {
                    Poll();
                    OnUpdate?.Invoke();
                    if (!Visible)
                    {
                        Events.Add("retire");
                        Throttle(false);
                        return;
                    }
                    var timing = new Timing(FrameCount, delta, (double)elapsed);
                    Capture(ref timing);
                    Throttle(true);
                }
                finally
                {
                    Events.Add("end");
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            void Capture(ref Timing timing)
            {
                Events.Add("render");
                if (FailCapture || timing.Frame != 1)
                    throw new InvalidOperationException("capture failed");
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            void Throttle(bool presenting) =>
                throw new InvalidOperationException("main loop throttled");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Poll() => throw new InvalidOperationException("old event poll");

        static void Pump(object loop) => Events.Add("poll");

        static bool Admit(object loop) => _render;

        static void Finish(object loop, bool presenting) => Events.Add("finish");

        static IEnumerable<CodeInstruction> Rewrite(
            IEnumerable<CodeInstruction> code,
            ILGenerator generator
        ) =>
            RenderLoopRewrite.Rewrite(
                code,
                generator,
                AccessTools.Method(typeof(RenderLoopRewriteTests), nameof(Poll)),
                AccessTools.Method(typeof(RenderLoopRewriteTests), nameof(Pump)),
                AccessTools.Constructor(
                    typeof(Timing),
                    new[] { typeof(int), typeof(float), typeof(double) }
                ),
                AccessTools.Field(typeof(Loop), nameof(Loop.OnUpdate)),
                AccessTools.Method(typeof(RenderLoopRewriteTests), nameof(Admit)),
                AccessTools.Method(typeof(Loop), "Throttle"),
                AccessTools.Method(typeof(RenderLoopRewriteTests), nameof(Finish))
            );

        [Test]
        public void SkippedRendersStillDispatchInputRunUpdatesAndCloseFrame()
        {
            var harmony = new Harmony("SlopWorld.Tests.RenderPacing");
            var target = AccessTools.Method(typeof(Loop), nameof(Loop.Run));
            try
            {
                harmony.Patch(
                    target,
                    transpiler: new HarmonyMethod(
                        AccessTools.Method(typeof(RenderLoopRewriteTests), nameof(Rewrite))
                    )
                );
                var loop = new Loop { OnUpdate = () => Events.Add("update") };
                for (int i = 0; i < 40; i++)
                {
                    Events.Clear();
                    _render = i % 8 == 0;
                    loop.Run(0.004f, i * 0.004f);
                    Assert.That(
                        Events,
                        Is.EqualTo(
                            _render
                                ? new[] { "poll", "update", "render", "finish", "end" }
                                : new[] { "poll", "update", "end" }
                        )
                    );
                }
                Events.Clear();
                _render = true;
                loop.Visible = false;
                loop.Run(0.004f, 1);
                Assert.That(
                    Events,
                    Is.EqualTo(new[] { "poll", "update", "retire", "finish", "end" })
                );

                Events.Clear();
                loop.Visible = true;
                loop.FailCapture = true;
                Assert.Throws<InvalidOperationException>(() => loop.Run(0.004f, 2));
                Assert.That(Events, Is.EqualTo(new[] { "poll", "update", "render", "end" }));
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
                Events.Clear();
            }
        }

        [Test]
        public void UnknownLoopFailsInsteadOfSkippingInputOrCleanup()
        {
            var generator = new DynamicMethod(
                "unknown",
                typeof(void),
                Type.EmptyTypes
            ).GetILGenerator();
            Assert.Throws<InvalidOperationException>(() =>
                Rewrite(Array.Empty<CodeInstruction>(), generator).ToArray()
            );
        }
    }
}
