using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
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
        static readonly Action<EngineApplication, TimeSpan> WaitUntil = BindPlatformDeadline();

        // Bind the game-owned LoopMode value type once so service ticks allocate nothing.
        static Action<EngineApplication, TimeSpan> BindPlatformDeadline()
        {
            var app = Expression.Parameter(typeof(EngineApplication));
            var deadline = Expression.Parameter(typeof(TimeSpan));
            var pace = AccessTools.Method(typeof(EngineApplication), "PaceLoop");
            var wait = AccessTools.Method(pace.GetParameters()[0].ParameterType, "WaitUntil");
            return Expression
                .Lambda<Action<EngineApplication, TimeSpan>>(
                    Expression.Call(app, pace, Expression.Call(wait, deadline)),
                    app,
                    deadline
                )
                .Compile();
        }

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

        // Platform events wake the loop before the deadline. RunFrame clears the
        // previous loop mode on entry and resets input in its own finally block.
        static void PaceNext(
            EngineApplication app,
            RenderSchedule schedule,
            double now,
            bool available
        )
        {
            double wait = schedule.WaitSeconds(now, available);
            if (wait > 0)
                WaitUntil(app, TimeSpan.FromSeconds(now + wait));
        }

        internal static bool AdmitFrame(EngineApplication app)
        {
            var schedule = Schedule(app);
            double now = Now;
            bool available = QueueAvailable(app);
            schedule.Polled(now);
            bool render = schedule.TryRender(now, available);
            PaceNext(app, schedule, now, available);
            return render;
        }

        internal static void FinishIteration(EngineApplication app, bool presenting)
        {
            if (presenting)
                return;
            var schedule = Schedule(app);
            double now = Now;
            schedule.SurfaceUnavailable();
            schedule.Polled(now);
            PaceNext(app, schedule, now, false);
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
                AccessTools.Constructor(
                    AccessTools.TypeByName("Waylith.Rendering.FrameTiming"),
                    new[] { typeof(int), typeof(float), typeof(double), typeof(bool) }
                ),
                AccessTools.Field(typeof(EngineApplication), "OnUpdate"),
                AccessTools.Method(typeof(RenderPacing), nameof(RenderPacing.AdmitFrame)),
                AccessTools.Method(typeof(EngineApplication), "NextPresentTime"),
                AccessTools.Method(typeof(EngineApplication), "ThrottleToTargetFrameRate"),
                AccessTools.Method(typeof(RenderPacing), nameof(RenderPacing.FinishIteration))
            );
    }
}
