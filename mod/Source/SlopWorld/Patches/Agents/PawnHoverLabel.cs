using HarmonyLib;
using Verse;

namespace SlopWorld
{
    // Keep the native tooltip's health, equipment, identity and priority; omit story and gender labels.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetTooltip))]
    public static class Patch_PawnHoverLabel
    {
        static void Postfix(Pawn __instance, ref TipSignal __result)
        {
            if (!AgentColony.IsAgent(__instance) && !PlayerPawn.IsPlayer(__instance)) return;

            var name = __instance.Name.ToStringShort;
            var health = HealthUtility.GetGeneralConditionLabel(__instance);
            var equipment = __instance.equipment?.Primary;
            __result.text = equipment != null
                ? "PawnTooltipWithPrimaryEquipNoDesc".Translate(name, equipment.LabelCap, health).ToString()
                : "PawnTooltipNoDescNoPrimaryEquip".Translate(name, health).ToString();
        }
    }
}
