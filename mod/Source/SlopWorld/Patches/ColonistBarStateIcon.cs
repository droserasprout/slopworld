using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    // Vanilla has no icon for a pawn on the floor, and its idle one is no use here
    // (see Patch_AgentNeverIdle). Appended rather than replacing the row: an agent
    // can still be on fire.
    // The attribute only quiets the startup scan; Look() resolves on first draw.
    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(ColonistBarColonistDrawer), "DrawIcons")]
    public static class Patch_ColonistBarStateIcon
    {
        // Vanilla's own BaseIconMaxSize, which is private.
        const float MaxSize = 20f;

        // Not cleared until the next pawn's turn, so it still holds this pawn's
        // icons - which is how many went out and where ours belongs.
        static readonly FieldInfo IconsField =
            AccessTools.Field(typeof(ColonistBarColonistDrawer), "tmpIconsToDraw");

        // A bed-rest cross for Down: at 20px a cross is the one shape that still
        // reads, where the battle log's downed symbol did not.
        static Texture2D _down;
        static Texture2D _idle;
        static bool _looked;

        static void Look()
        {
            if (_looked) return;
            _looked = true;
            _down = ContentFinder<Texture2D>.Get("UI/Icons/ColonistBar/MedicalRest", false);
            _idle = ContentFinder<Texture2D>.Get("UI/Icons/ColonistBar/Idle", false);
        }

        static void Postfix(Rect rect, Pawn colonist)
        {
            // Vanilla bails on a corpse before drawing, so the list still holds the
            // previous pawn's icons.
            if (colonist == null || colonist.Dead) return;

            var session = AgentColony.Current?.SessionOf(colonist);
            if (session == null) return;

            var state = SessionHub.Instance.Get(session)?.State ?? AgentState.Down;

            Look();
            Texture2D tex;
            string tip;
            switch (state)
            {
                case AgentState.Down:
                    tex = _down;
                    tip = $"'{session}' is down: its process is not running.";
                    break;
                case AgentState.Idle:
                    tex = _idle;
                    tip = $"'{session}' is idle: nothing to answer, so it claudwatches.";
                    break;
                default:
                    return; // working and waiting say so in colour, over the pawn
            }
            if (tex == null) return;

            // The drawer's own sizing. Max is for the empty row, where vanilla's
            // division is by zero.
            int drawn = Drawn();
            float size = Mathf.Min(
                ColonistBarColonistDrawer.PawnTextureSize.x / Mathf.Max(drawn, 1), MaxSize)
                * Find.ColonistBar.Scale;

            var box = new Rect(rect.x + 1f + drawn * size, rect.yMax - size - 1f, size, size);

            var was = GUI.color;
            GUI.color = TerminalWindow.StateColor(state);
            GUI.DrawTexture(box, tex);
            GUI.color = was;

            TooltipHandler.TipRegion(box, tip);
        }

        static int Drawn() => (IconsField?.GetValue(null) as ICollection)?.Count ?? 0;
    }

    // Vanilla's idle means "no work queued", which every colonist here always is,
    // and the bar gates that icon on DaysPassed >= 1 anyway. The daemon's word is
    // the only thing that draws a clock.
    [HarmonyPatch(typeof(Pawn_MindState), nameof(Pawn_MindState.IsIdle), MethodType.Getter)]
    public static class Patch_AgentNeverIdle
    {
        static void Postfix(Pawn_MindState __instance, ref bool __result)
        {
            if (__result && AgentColony.IsAgent(__instance.pawn)) __result = false;
        }
    }
}
