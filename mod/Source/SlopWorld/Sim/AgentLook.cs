using RimWorld;
using Verse;

namespace SlopWorld
{
    // Core-only outfits, kept away from headgear so the faceplate and hair remain the agent's
    // identity. The ordinary shirt and pants underneath coats make each entry a complete look,
    // while full-body garments stand alone.
    public static class AgentLook
    {
        static readonly string[][] Outfits =
        {
            new[] { "Apparel_Pants", "Apparel_BasicShirt" },
            new[] { "Apparel_Pants", "Apparel_CollarShirt" },
            new[] { "Apparel_TribalA" },
            new[] { "Apparel_Pants", "Apparel_BasicShirt", "Apparel_Parka" },
            new[] { "Apparel_Pants", "Apparel_CollarShirt", "Apparel_Duster" },
            new[] { "Apparel_Pants", "Apparel_BasicShirt", "Apparel_Jacket" },
            new[] { "Apparel_PlateArmor" },
            new[] { "Apparel_Pants", "Apparel_BasicShirt", "Apparel_FlakVest" },
            new[] { "Apparel_FlakPants", "Apparel_BasicShirt", "Apparel_FlakJacket" },
            new[] { "Apparel_PowerArmor" },
            new[] { "Apparel_ArmorRecon" },
            new[] { "Apparel_Pants", "Apparel_CollarShirt", "Apparel_Cape" },
            new[] { "Apparel_Robe" },
        };

        public static void Roll(Pawn pawn)
        {
            if (pawn?.apparel == null) return;

            pawn.apparel.DestroyAll();
            var outfit = Outfits.RandomElement();
            for (var i = 0; i < outfit.Length; i++)
            {
                var def = DefDatabase<ThingDef>.GetNamedSilentFail(outfit[i]);
                if (def == null) continue;

                var stuff = def.MadeFromStuff ? GenStuff.RandomStuffFor(def) : null;
                var apparel = ThingMaker.MakeThing(def, stuff) as Apparel;
                if (apparel != null) pawn.apparel.Wear(apparel, false);
            }

            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        }

        public static void Reroll(Pawn pawn)
        {
            RobotFace.Reroll(pawn);
            Roll(pawn);
        }
    }
}
