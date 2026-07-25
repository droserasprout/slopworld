using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// A down agent gets a red cross in the colonist bar. Vanilla has an icon for
    /// asleep (the Z) and one for idle, but nothing for a pawn that is simply on
    /// the floor - which is every agent whose process is not running, and the one
    /// state a viewer most needs to catch from the top of the screen.
    ///
    /// Appended after whatever vanilla drew rather than replacing the row: an
    /// agent can still be on fire, and that icon has to stay.
    /// </summary>
    [HarmonyPatch(typeof(ColonistBarColonistDrawer), "DrawIcons")]
    public static class Patch_ColonistBarDownIcon
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

        // The medical cross off vanilla's own bar, tinted red below. Its bed-rest
        // meaning is beside the point at this size: a cross is the one shape that
        // still reads at 20px, which the battle log's downed symbol was not.
        // Resolved once and kept even when null, so a missing texture is one error
        // in the log rather than one per colonist per frame.
        static Texture2D _tex;
        static bool _looked;

        static Texture2D Tex
        {
            get
            {
                if (_looked) return _tex;
                _looked = true;
                _tex = ContentFinder<Texture2D>.Get("UI/Icons/ColonistBar/MedicalRest", false);
                return _tex;
            }
        }

        static void Postfix(Rect rect, Pawn colonist)
        {
            // Vanilla bails on a corpse before drawing anything, and so must we -
            // the list would still be holding the previous pawn's icons.
            if (colonist == null || colonist.Dead) return;
            if (Tex == null) return;

            var session = AgentColony.Current?.SessionOf(colonist);
            if (session == null) return;
            if ((SessionHub.Instance.Get(session)?.State ?? AgentState.Down) != AgentState.Down)
                return;

            // Mirrors the drawer's own sizing, so ours sits in the row at the size
            // the rest were given. With nothing else drawn the division vanilla
            // does is by zero and lands on the cap; Max keeps us off that edge.
            int drawn = Drawn();
            float size = Mathf.Min(
                ColonistBarColonistDrawer.PawnTextureSize.x / Mathf.Max(drawn, 1), MaxSize)
                * Find.ColonistBar.Scale;

            var box = new Rect(rect.x + 1f + drawn * size, rect.yMax - size - 1f, size, size);

            var was = GUI.color;
            GUI.color = TerminalWindow.StateColor(AgentState.Down);
            GUI.DrawTexture(box, Tex);
            GUI.color = was;

            TooltipHandler.TipRegion(box, $"'{session}' is down: its process is not running.");
        }

        static int Drawn() => (IconsField?.GetValue(null) as ICollection)?.Count ?? 0;
    }
}
