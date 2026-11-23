using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace SlopWorld
{
    // Wayland focus entry reports keys already held. Waylith passes them through
    // OnKeyDown, then InputState loses IsSynthesized and the native modifiers.
    [HarmonyPatch]
    static class Patch_NativeTabInput
    {
        static readonly Type NativeEvent = AccessTools.TypeByName("Waylith.Windowing.KeyEvent");
        static readonly int Alt = Modifier("Alt");
        static readonly int Control = Modifier("Control");
        static bool _ignoreTabRepeats;

        static int Modifier(string name) =>
            EnumValue(AccessTools.TypeByName("Waylith.Windowing.Modifiers"), name);

        static int EnumValue(Type type, string name) =>
            Convert.ToInt32(Enum.Parse(type, name), CultureInfo.InvariantCulture);

        static MethodBase TargetMethod() =>
            AccessTools.Method(
                AccessTools.TypeByName("Waylith.Input.MainWindowHandler"),
                "Waylith.Windowing.IWindowHandler.OnKeyDown"
            );

        static bool IgnoreTab(bool synthesized, bool repeat, int modifiers)
        {
            if (!repeat)
                _ignoreTabRepeats = synthesized || (modifiers & (Alt | Control)) == Alt;
            return _ignoreTabRepeats;
        }

        // KeyEvent is passed by readonly reference and contains a span. Read its
        // properties directly in the handler rather than boxing the event.
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator
        )
        {
            var original = new List<CodeInstruction>(instructions);
            var proceed = generator.DefineLabel();
            original[0].labels.Add(proceed);
            var physical = AccessTools.PropertyGetter(NativeEvent, "Physical");
            yield return new CodeInstruction(OpCodes.Ldarg_2);
            yield return new CodeInstruction(OpCodes.Call, physical);
            yield return new CodeInstruction(OpCodes.Ldc_I4, EnumValue(physical.ReturnType, "Tab"));
            yield return new CodeInstruction(OpCodes.Bne_Un, proceed);
            foreach (var property in new[] { "IsSynthesized", "IsRepeat", "Modifiers" })
            {
                yield return new CodeInstruction(OpCodes.Ldarg_2);
                yield return new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.PropertyGetter(NativeEvent, property)
                );
            }
            yield return CodeInstruction.Call(typeof(Patch_NativeTabInput), nameof(IgnoreTab));
            yield return new CodeInstruction(OpCodes.Brfalse, proceed);
            yield return new CodeInstruction(OpCodes.Ldc_I4_1);
            yield return new CodeInstruction(OpCodes.Ret);
            foreach (var instruction in original)
                yield return instruction;
        }
    }
}
