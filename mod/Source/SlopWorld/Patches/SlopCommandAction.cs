using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public sealed class SlopCommandAction : Command_Action
    {
        const float IconBox = 36f;

        readonly SlopWidgets.Btn _kind;

        public SlopCommandAction(SlopWidgets.Btn kind)
        {
            _kind = kind;
        }

        public override Texture2D BGTexture => BaseContent.ClearTex;

        public override Texture2D BGTextureShrunk => BaseContent.ClearTex;

        protected override GizmoResult GizmoOnGUIInt(Rect butRect, GizmoRenderParms parms)
        {
            return base.GizmoOnGUIInt(butRect, parms);
        }

        public override void DrawIcon(Rect rect, Material buttonMat, GizmoRenderParms parms)
        {
            bool over = !Disabled && Mouse.IsOver(rect);
            SlopWidgets.ActionButtonBackground(rect, _kind, !Disabled, over,
                over && Input.GetMouseButton(0));

            // The icon PNGs share a canvas, not a visual ink box: terminal/edit are nearly
            // full-size while close is deliberately a much smaller glyph. Draw into a
            // compact common box, then compensate for each source glyph's ink bounds.
            Rect iconRect = new Rect(rect.center.x - IconBox / 2f,
                rect.center.y - IconBox / 2f, IconBox, IconBox);
            float oldScale = iconDrawScale;
            iconDrawScale *= ActionIconScale(icon);
            try
            {
                base.DrawIcon(iconRect, buttonMat, parms);
            }
            finally
            {
                iconDrawScale = oldScale;
            }
        }

        static float ActionIconScale(Texture tex)
        {
            // These values equalize the visible bounds of the six action glyphs at roughly
            // 23px. They are intentionally local to these gizmos; the same icons elsewhere
            // retain their existing inline sizing.
            if (tex == Icons.Terminal) return 0.92f;
            if (tex == Icons.Stop) return 1.05f;
            if (tex == Icons.Play) return 1.05f;
            if (tex == Icons.Edit) return 0.91f;
            if (tex == Icons.Add) return 0.97f;
            if (tex == Icons.Cross) return 1.42f;
            return 1f;
        }
    }

    [HarmonyPatch(typeof(Command), "GizmoOnGUIInt")]
    static class Patch_SlopCommandShortcutLabel
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
                                                        ILGenerator generator)
        {
            var code = instructions.ToList();
            var label = AccessTools.Method(typeof(Widgets), nameof(Widgets.Label),
                new[] { typeof(Rect), typeof(string) });
            var getY = AccessTools.Method(typeof(Rect), "get_y");
            var setY = AccessTools.Method(typeof(Rect), "set_y");

            for (int i = 3; i < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Call || !Equals(code[i].operand, label)
                    || !IsLocalLoad(code[i - 3], 5))
                    continue;

                // Vanilla puts the shortcut label at y + 3. Move just SlopCommandAction's
                // label two pixels higher, leaving every other command's layout untouched.
                var skip = generator.DefineLabel();
                code[i - 3].labels.Add(skip);
                code.InsertRange(i - 3, new[]
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Isinst, typeof(SlopCommandAction)),
                    new CodeInstruction(OpCodes.Brfalse_S, skip),
                    new CodeInstruction(OpCodes.Ldloca_S, code[i - 3].operand),
                    new CodeInstruction(OpCodes.Dup),
                    new CodeInstruction(OpCodes.Call, getY),
                    new CodeInstruction(OpCodes.Ldc_R4, 2f),
                    new CodeInstruction(OpCodes.Sub),
                    new CodeInstruction(OpCodes.Call, setY),
                });
                return code;
            }

            Log.Error("[SlopWorld] Command.GizmoOnGUIInt changed; action shortcut label keeps vanilla placement");
            return code;
        }

        static bool IsLocalLoad(CodeInstruction instruction, int local) =>
            instruction.opcode == OpCodes.Ldloc_0 && local == 0
            || instruction.opcode == OpCodes.Ldloc_1 && local == 1
            || instruction.opcode == OpCodes.Ldloc_2 && local == 2
            || instruction.opcode == OpCodes.Ldloc_3 && local == 3
            || (instruction.opcode == OpCodes.Ldloc_S || instruction.opcode == OpCodes.Ldloc)
                && LocalOperandIndex(instruction.operand) == local;

        static int LocalOperandIndex(object operand)
        {
            if (operand is LocalBuilder local) return local.LocalIndex;
            if (operand is byte b) return b;
            if (operand is short s) return s;
            if (operand is int i) return i;
            return -1;
        }
    }
}
