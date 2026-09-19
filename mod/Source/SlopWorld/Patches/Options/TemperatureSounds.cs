using HarmonyLib;
using Verse;
using Verse.Sound;

namespace SlopWorld
{
    public static class TemperatureSounds
    {
        public static bool BirdMode => TemperatureUnit.IsGlazedBald(Settings.TemperatureUnit);

        public static SoundDef Replacement(SoundDef original)
        {
            var bird = ModDefOf.SlopWorld_UIBird;
            return BirdMode && bird != null && original != bird ? bird : original;
        }
    }

    // UI one-shots are camera sounds without a map restriction. Map sounds using the same
    // API pass their map explicitly, so they retain their normal audio in bird mode.
    [HarmonyPatch(typeof(SoundStarter), nameof(SoundStarter.PlayOneShotOnCamera))]
    public static class Patch_UiTemperatureSounds
    {
        static void Prefix(ref SoundDef soundDef, Map onlyThisMap)
        {
            if (onlyThisMap == null) soundDef = TemperatureSounds.Replacement(soundDef);
        }
    }
}
