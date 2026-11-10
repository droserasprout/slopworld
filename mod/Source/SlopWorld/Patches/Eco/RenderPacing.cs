using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using Waylith;

namespace SlopWorld
{
    // Main-thread event/update scheduling and nonblocking admission to the native
    // render queue. FramePolicy still owns settings; RenderSchedule owns deadlines.
    internal static class RenderPacing
    {
        static readonly ConditionalWeakTable<EngineApplication, RenderSchedule> Schedules = new();
        static readonly Type Glfw = AccessTools.TypeByName("Brutal.GlfwApi.Glfw");
        static readonly Action Poll = AccessTools.MethodDelegate<Action>(
            AccessTools.Method(Glfw, "PollEvents")
        );
        static readonly Action<double> Wait = AccessTools.MethodDelegate<Action<double>>(
            AccessTools.Method(Glfw, "WaitEventsTimeout")
        );
        static readonly FieldInfo RenderThread = AccessTools.Field(
            typeof(EngineApplication),
            "_renderThread"
        );
        static readonly AccessTools.FieldRef<object, int> PendingFrames =
            AccessTools.FieldRefAccess<object, int>(
                AccessTools.Field(RenderThread.FieldType, "_pendingFrames")
            );

        static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        static RenderSchedule Schedule(EngineApplication app)
        {
            var schedule = Schedules.GetValue(app, _ => new RenderSchedule());
            schedule.Configure(EngineWindow.TargetFrameRateOverride ?? Application.targetFrameRate);
            return schedule;
        }

        static bool QueueAvailable(EngineApplication app)
        {
            var thread = RenderThread.GetValue(app);
            // The native producer allowed one outstanding frame before starting
            // capture. Only this main thread enqueues, so the count can only fall
            // between this check and Enqueue. Keep its two-frame maximum without
            // blocking input on VSync or a slow GPU.
            return thread != null && Volatile.Read(ref PendingFrames(thread)) <= 1;
        }

        internal static void PumpEvents(EngineApplication app)
        {
            var schedule = Schedule(app);
            double wait = schedule.WaitSeconds(Now, QueueAvailable(app));
            // WaitEventsTimeout dispatches callbacks. It must run at the original
            // PollEvents site, AFTER both native input states have begun their frame;
            // waiting at the end would discard newly received keys on the next reset.
            if (wait > 0)
                Wait(wait);
            else
                Poll();
            schedule.Polled(Now);
        }

        internal static bool AdmitFrame(EngineApplication app) =>
            Schedule(app).TryRender(Now, QueueAvailable(app));

        internal static void FinishIteration(EngineApplication app, bool presenting)
        {
            if (!presenting)
                Schedule(app).SurfaceUnavailable();
        }
    }

    [HarmonyPatch]
    static class Patch_RenderPacing
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(EngineApplication), "RunFrame");

        internal static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator
        ) =>
            RenderLoopRewrite.Rewrite(
                instructions,
                generator,
                AccessTools.Method(AccessTools.TypeByName("Brutal.GlfwApi.Glfw"), "PollEvents"),
                AccessTools.Method(typeof(RenderPacing), nameof(RenderPacing.PumpEvents)),
                AccessTools.Constructor(
                    AccessTools.TypeByName("Waylith.Rendering.FrameTiming"),
                    new[] { typeof(int), typeof(float), typeof(double) }
                ),
                AccessTools.Field(typeof(EngineApplication), "OnUpdate"),
                AccessTools.Method(typeof(RenderPacing), nameof(RenderPacing.AdmitFrame)),
                AccessTools.Method(typeof(EngineApplication), "ThrottleToTargetFrameRate"),
                AccessTools.Method(typeof(RenderPacing), nameof(RenderPacing.FinishIteration))
            );
    }
}
