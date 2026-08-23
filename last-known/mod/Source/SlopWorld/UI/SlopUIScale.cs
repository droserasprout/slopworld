using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Exposes continuous Prefs.UIScale from 0.5x to 4x after removing vanilla's discrete ladder and low-resolution reset.
    public static class SlopUIScale
    {
        public const float Min = 0.5f;
        public const float Max = 4f;

        // The slider snaps fine; the palette's zoom walks the coarse grid, which is vanilla's
        // own spacing over the range anyone steps through by hand.
        const float Fine = 0.05f;
        const float Coarse = 0.25f;

        static bool _unsaved;

        public static float Current => Prefs.UIScale;

        public static string Readout(float scale) => scale.ToString("0.##") + "x";

        // Live at once - Verse.UI re-derives the scaled screen every OnGUI and the window
        // stack re-lays itself out when it changes. The write to Prefs.xml waits for Flush,
        // a drag being one save per frame otherwise.
        public static void Set(float scale)
        {
            scale = Mathf.Clamp(Mathf.Round(scale / Fine) * Fine, Min, Max);
            if (Mathf.Approximately(scale, Prefs.UIScale)) return;

            Prefs.UIScale = scale;
            GenUI.ClearLabelWidthCache();
            _unsaved = true;
        }

        // One rung either way, snapped to the coarse grid so repeated presses walk
        // 1x, 1.25x, 1.5x rather than carrying whatever offset the slider was left on.
        public static void Zoom(int dir)
        {
            float at = Prefs.UIScale;
            float grid = Mathf.Round(at / Coarse) * Coarse;
            Set(dir > 0
                ? (grid > at + 0.001f ? grid : grid + Coarse)
                : (grid < at - 0.001f ? grid : grid - Coarse));
            Flush();
        }

        // The deferred write, taken as soon as no hand is on the knob. From the Appearance
        // page each frame, and from Zoom, which has no page behind it to come back to.
        public static void Flush()
        {
            if (!_unsaved || Input.GetMouseButton(0)) return;
            _unsaved = false;
            Prefs.Save();
        }
    }
}
