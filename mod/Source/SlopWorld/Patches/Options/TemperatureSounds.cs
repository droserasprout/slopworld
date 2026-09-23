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

    // Replace camera sounds without a map restriction in alternate mode.
    // Sounds with an explicit map retain their normal audio.
    [HarmonyPatch(typeof(SoundStarter), nameof(SoundStarter.PlayOneShotOnCamera))]
    public static class Patch_UiTemperatureSounds
    {
        static void Prefix(ref SoundDef soundDef, Map onlyThisMap)
        {
            if (onlyThisMap == null) soundDef = TemperatureSounds.Replacement(soundDef);
        }
    }
}
