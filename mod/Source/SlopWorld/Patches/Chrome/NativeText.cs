using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SlopWorld
{
    // GUIStyle drops richText on the way to the backend. Restore that policy for
    // both layout and painting, including labels, buttons, and image/text content.
    [HarmonyPatch]
    static class Patch_NativeTextStyle
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GUIStyle), "MeasureText");
            yield return AccessTools.Method(typeof(GUIStyle), "DrawTextIn");
        }

        internal static void Prefix(GUIStyle __instance, out NativeTextScope __state) =>
            __state = new NativeTextScope(__instance.richText);

        internal static void Finalizer(NativeTextScope __state) => __state.Dispose();
    }

    // Fields call the backend directly for drawing, selection, and cursor movement.
    // Their content is always literal, independent of the label style's policy.
    [HarmonyPatch]
    static class Patch_NativeTextField
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GUI), "TextFieldImpl");
            yield return AccessTools.Method(typeof(TextEditor), "IndexAtPosition");
        }

        internal static void Prefix(out NativeTextScope __state) =>
            __state = new NativeTextScope(false);

        internal static void Finalizer(NativeTextScope __state) => __state.Dispose();
    }

    [HarmonyPatch]
    static class Patch_NativeTextTags
    {
        static MethodBase TargetMethod()
        {
            var method = AccessTools.Method(
                AccessTools.TypeByName("RimWorldWaylith.Imgui.RichText"),
                "Parse",
                new[]
                {
                    typeof(string),
                    typeof(int),
                    typeof(int).MakeByRefType(),
                    typeof(Color).MakeByRefType(),
                    typeof(float).MakeByRefType(),
                }
            );
            // Harmony initializes a skipped method's result to default(Tag).
            if (
                method == null
                || !method.ReturnType.IsEnum
                || Enum.GetName(method.ReturnType, 0) != "None"
            )
                throw new InvalidOperationException("Unexpected RimWorld 1.7 rich-text parser");
            return method;
        }

        internal static bool Prefix(int __1, ref int __2, ref Color __3, ref float __4)
        {
            if (!NativeTextScope.Literal)
                return true;
            // Match Parse's ordinary no-tag outcome without touching the source.
            __2 = __1;
            __3 = default;
            __4 = 0f;
            return false;
        }
    }
}
