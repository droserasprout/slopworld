using HarmonyLib;
using Verse;
using Verse.Sound;

namespace SlopWorld
{
    public static class TemperatureSounds
    {
        public static bool AlternateMode => TemperatureUnit.IsAlternate(Settings.TemperatureUnit);

        public static SoundDef Replacement(SoundDef original)
        {
            var replacement = ModDefOf.SlopWorld_UIAlternate;
            return AlternateMode && replacement != null && original != replacement ? replacement : original;
        }
    }

    // UI one-shots are camera sounds without a map restriction. Map sounds using the same
    // API pass their map explicitly, so they retain their normal audio in alternate mode.
    [HarmonyPatch(typeof(SoundStarter), nameof(SoundStarter.PlayOneShotOnCamera))]
    public static class Patch_UiTemperatureSounds
    {
        static void Prefix(ref SoundDef soundDef, Map onlyThisMap)
        {
            if (onlyThisMap == null) soundDef = TemperatureSounds.Replacement(soundDef);
        }
    }
}
