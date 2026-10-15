using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public sealed class UiCommandAction : Command_Action
    {
        const float IconBox = 36f;
        const float HeightReduction = 9f;
        const float IconOffsetY = 3f;

        readonly UiTheme.Btn _kind;

        public UiCommandAction(UiTheme.Btn kind)
        {
            _kind = kind;
        }

        public override Texture2D BGTexture => BaseContent.ClearTex;

        public override Texture2D BGTextureShrunk => BaseContent.ClearTex;

        internal void DrawCaptionBackground(Rect rect) =>
            UiButtons.ActionButtonBackground(rect, _kind, !Disabled, false, false);

        protected override GizmoResult GizmoOnGUIInt(Rect butRect, GizmoRenderParms parms)
        {
            // Keep the caption's bottom anchor and the grid's horizontal spacing.
            if (!parms.shrunk)
            {
                butRect.y += HeightReduction;
                butRect.height -= HeightReduction;
            }
            SessionGizmoInput.RememberActionRect(butRect);
            var result = base.GizmoOnGUIInt(butRect, parms);

            // The map's low-priority click pass runs after the window stack. Keep the
            // command's own GUI.Button processing above, then consume mouse events here as
            // a final guard for the map passes that run after the gizmo grid.
            var e = Event.current;
            if (
                e != null
                && (
                    e.rawType == EventType.MouseDown
                    || e.rawType == EventType.MouseUp
                    || e.rawType == EventType.MouseDrag
                )
                && (e.button == 0 || e.button == 1)
                && butRect.Contains(e.mousePosition)
            )
                e.Use();

            return result;
        }

        public override void DrawIcon(Rect rect, Material buttonMat, GizmoRenderParms parms)
        {
            bool over = !Disabled && Mouse.IsOver(rect);
            UiButtons.ActionButtonBackground(
                rect,
                _kind,
                !Disabled,
                over,
                over && Input.GetMouseButton(0)
            );

            // The icon PNGs share a canvas, not a visual ink box: terminal/edit are nearly
            // full-size while close is deliberately a much smaller glyph. Draw into a
            // compact common box, then compensate for each source glyph's ink bounds.
            float iconY = rect.center.y;
            if (!parms.shrunk)
            {
                // Center in the face above the caption at the original icon size.
                // Shortcuts occupy the upper-left corner; reserving their full line
                // across the face needlessly shrinks and lowers the centered glyph.
                float captionHeight = Text.CalcHeight(LabelCap, rect.width + 0.1f);
                float captionTop = rect.yMax - captionHeight + 12f;
                iconY = (rect.y + captionTop) / 2f + IconOffsetY;
            }
            Rect iconRect = new Rect(
                rect.center.x - IconBox / 2f,
                iconY - IconBox / 2f,
                IconBox,
                IconBox
            );
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
            // 23px. They are intentionally local to these gizmos. The same icons elsewhere
            // retain their existing inline sizing.
            if (tex == Icons.Terminal)
                return 0.92f;
            if (tex == Icons.Stop)
                return 1.05f;
            if (tex == Icons.Play)
                return 1.05f;
            if (tex == Icons.Edit)
                return 0.91f;
            if (tex == Icons.Add)
                return 0.97f;
            if (tex == Icons.Cross)
                return 1.42f;
            return 1f;
        }
    }

    [HarmonyPatch(typeof(Command), "GizmoOnGUIInt")]
    static class Patch_SlopCommandCaptionBackground
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var background = AccessTools.Field(typeof(TexUI), nameof(TexUI.GrayTextBG));
            var draw = AccessTools.Method(
                typeof(GUI),
                nameof(GUI.DrawTexture),
                new[] { typeof(Rect), typeof(Texture) }
            );
            var replacement = AccessTools.Method(
                typeof(Patch_SlopCommandCaptionBackground),
                nameof(DrawBackground)
            );

            for (int i = 2; i < code.Count; i++)
            {
                // The bottom caption duplicates its freshly computed Rect for drawing
                // the backdrop and then the label. Keep those exact bounds; the top-right
                // label uses a Rect local and must retain its native background.
                if (
                    code[i - 2].opcode != OpCodes.Dup
                    || !code[i - 1].LoadsField(background)
                    || !code[i].Calls(draw)
                )
                    continue;

                var command = new CodeInstruction(OpCodes.Ldarg_0);
                command.labels.AddRange(code[i].labels);
                code[i].labels.Clear();
                code[i].operand = replacement;
                code.Insert(i, command);
                return code;
            }

            Log.Error(
                "[SlopWorld] Command.GizmoOnGUIInt changed. Action captions keep their native backdrop."
            );
            return code;
        }

        static void DrawBackground(Rect rect, Texture texture, Command command)
        {
            if (command is UiCommandAction action)
                action.DrawCaptionBackground(rect);
            else
                GUI.DrawTexture(rect, texture);
        }
    }

    [HarmonyPatch(typeof(Command), "GizmoOnGUIInt")]
    static class Patch_SlopCommandShortcutLabel
    {
        // The game loads this Rect local for the shortcut-label Widgets.Label call.
        const int ShortcutLabelRectLocal = 5;

        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator
        )
        {
            var code = instructions.ToList();
            var label = AccessTools.Method(
                typeof(Widgets),
                nameof(Widgets.Label),
                new[] { typeof(Rect), typeof(string) }
            );
            var getY = AccessTools.Method(typeof(Rect), "get_y");
            var setY = AccessTools.Method(typeof(Rect), "set_y");

            for (int i = 3; i < code.Count; i++)
            {
                if (
                    code[i].opcode != OpCodes.Call
                    || !Equals(code[i].operand, label)
                    || !IsLocalLoad(code[i - 3], ShortcutLabelRectLocal)
                )
                    continue;

                // Vanilla puts the shortcut label at y + 3. Move just UiCommandAction's
                // label two pixels higher, leaving every other command's layout untouched.
                var skip = generator.DefineLabel();
                code[i - 3].labels.Add(skip);
                code.InsertRange(
                    i - 3,
                    new[]
                    {
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Isinst, typeof(UiCommandAction)),
                        new CodeInstruction(OpCodes.Brfalse_S, skip),
                        new CodeInstruction(OpCodes.Ldloca_S, code[i - 3].operand),
                        new CodeInstruction(OpCodes.Dup),
                        new CodeInstruction(OpCodes.Call, getY),
                        new CodeInstruction(OpCodes.Ldc_R4, 2f),
                        new CodeInstruction(OpCodes.Sub),
                        new CodeInstruction(OpCodes.Call, setY),
                    }
                );
                return code;
            }

            Log.Error(
                "[SlopWorld] Command.GizmoOnGUIInt changed. Action shortcut labels keep vanilla placement."
            );
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
            if (operand is LocalBuilder local)
                return local.LocalIndex;
            if (operand is byte b)
                return b;
            if (operand is short s)
                return s;
            if (operand is int i)
                return i;
            return -1;
        }
    }
}
