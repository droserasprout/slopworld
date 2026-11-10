using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SlopWorld
{
    // Intercept the loop's close decision while updates and drawing can still run.
    // ExecuteQuitLifecycle is too late: the render thread has already stopped.
    [HarmonyPatch]
    static class Patch_NativeWindowClose
    {
        static readonly Type Engine = AccessTools.TypeByName("Waylith.EngineApplication");
        static readonly MethodInfo GetWindow = AccessTools.PropertyGetter(Engine, "Window");
        static readonly Type Window = GetWindow.ReturnType;
        static readonly MethodInfo GetClose = AccessTools.PropertyGetter(Window, "ShouldClose");
        static readonly MethodInfo SetClose = AccessTools.PropertySetter(Window, "ShouldClose");
        static readonly MethodInfo ShutdownReason = AccessTools.PropertyGetter(
            Engine,
            "ShutdownRequestedReason"
        );

        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(Engine, "RunFrames");
            yield return AccessTools.Method(Engine, "RunFrame");
        }

        internal static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions
        ) =>
            NativeCloseDispatch.Rewrite(
                instructions,
                GetClose,
                AccessTools.Method(typeof(Patch_NativeWindowClose), nameof(ShouldClose))
            );

        static bool ShouldClose(bool close, object engine)
        {
            // Keep the ordinary frame path free of reflection.
            if (!close)
                return false;
            bool stopping = !string.IsNullOrEmpty((string)ShutdownReason.Invoke(engine, null));
            if (!QuitInterceptor.DeferNativeClose(close, stopping))
                return true;
            SetClose.Invoke(GetWindow.Invoke(engine, null), new object[] { false });
            return false;
        }
    }
}
