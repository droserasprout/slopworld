using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// An agent is a process wearing a colonist, so it gets a machine head: a
    /// plated skull with a lens where the face would be. The texture is a plain
    /// three-rotation head set, which means the vanilla render tree draws it with
    /// no help from us.
    /// </summary>
    public static class RobotHead
    {
        /// <summary>Fits the head, and takes off the hair that would otherwise grow
        /// through it. Cheap to call on every reconcile: it only ever touches a
        /// pawn once, and covers colonies saved before the head existed.</summary>
        public static void Apply(Pawn pawn)
        {
            if (pawn?.story == null) return;
            if (pawn.story.headType == SlopDefOf.SlopRobotHead) return;

            pawn.story.headType = SlopDefOf.SlopRobotHead;
            pawn.story.hairDef = HairDefOf.Bald;
            if (pawn.style != null) pawn.style.beardDef = BeardDefOf.NoBeard;

            // Drops the render tree, the portrait and the pawn's atlas frames, all
            // of which are still holding the face it used to have.
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        }
    }

    /// <summary>Heads are drawn through the skin shader and tinted to the pawn's
    /// skin colour, which would hand us a suntanned robot. Ours arrives painted,
    /// so it takes the plain cutout shader and no tint.</summary>
    [HarmonyPatch(typeof(HeadTypeDef), nameof(HeadTypeDef.GetGraphic))]
    public static class Patch_HeadTypeDef_GetGraphic
    {
        static bool Prefix(HeadTypeDef __instance, ref Graphic_Multi __result)
        {
            if (__instance != SlopDefOf.SlopRobotHead) return true;

            __result = (Graphic_Multi)GraphicDatabase.Get<Graphic_Multi>(
                __instance.graphicPath, ShaderDatabase.Cutout, Vector2.one, Color.white);
            return false;
        }
    }
}
