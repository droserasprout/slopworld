using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    /// <summary>
    /// The two agent states worth catching from the top of the screen get an icon
    /// in the colonist bar: a red cross for one whose process is not running, and
    /// the game's own clock for one that is up and has nothing to do. Vanilla has
    /// an icon for asleep and one for idle, but nothing for a pawn that is simply
    /// on the floor - and its idle clock is no use to us either (see
    /// <see cref="Patch_AgentNeverIdle"/> below).
    ///
    /// Appended after whatever vanilla drew rather than replacing the row: an
    /// agent can still be on fire, and that icon has to stay.
    /// </summary>
    [HarmonyPatch(typeof(ColonistBarColonistDrawer), "DrawIcons")]
    public static class Patch_ColonistBarStateIcon
    {
        /// The drawer's own cap on one icon. Its BaseIconMaxSize is private, but
        /// the width the icons share is just PawnTextureSize.x, which is not.
        const float MaxSize = 20f;

        /// <summary>
        /// The list the drawer fills and then draws from. It is not cleared until
        /// the next pawn's turn, so reading it here says how many icons went out
        /// and where ours belongs. Private, hence the reflection; if it ever stops
        /// resolving, ours simply lands first in the row.
        /// </summary>
        static readonly FieldInfo IconsField =
            AccessTools.Field(typeof(ColonistBarColonistDrawer), "tmpIconsToDraw");

        // Both off vanilla's own bar. The medical cross's bed-rest meaning is beside
        // the point at this size: a cross is the one shape that still reads at 20px,
        // which the battle log's downed symbol was not. Resolved once and kept even
        // when null, so a missing texture is one error in the log rather than one
        // per colonist per frame.
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
            // Vanilla bails on a corpse before drawing anything, and so must we -
            // the list would still be holding the previous pawn's icons.
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

            // Mirrors the drawer's own sizing, so ours sits in the row at the size
            // the rest were given. With nothing else drawn the division vanilla
            // does is by zero and lands on the cap; Max keeps us off that edge.
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

    /// <summary>
    /// An agent is never idle in its own pawn's mind, so vanilla never draws the
    /// clock for one and the icon above is the only thing that does.
    ///
    /// Vanilla's idle means "no work queued", which every colonist here is, all the
    /// time: the board has no work. Left alone it would hang a clock on an agent
    /// that is flat out - and only after the first in-game day at that, since the
    /// bar gates the icon on DaysPassed >= 1, where the state that matters here
    /// turns over in seconds and a colony is often minutes old. So the daemon's
    /// word is what draws it, and the pawn's own idleness is answered false.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_MindState), nameof(Pawn_MindState.IsIdle), MethodType.Getter)]
    public static class Patch_AgentNeverIdle
    {
        static void Postfix(Pawn_MindState __instance, ref bool __result)
        {
            if (__result && AgentColony.IsAgent(__instance.pawn)) __result = false;
        }
    }
}
