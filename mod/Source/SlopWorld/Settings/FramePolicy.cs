using System;

namespace SlopWorld
{
    // Select native presentation and target FPS. Unfocused windows always use 15 FPS.
    public sealed class FramePolicy
    {
        public const string Auto = "auto",
            Limit = "limit";
        public static readonly int[] Presets = { 15, 30, 60, 120, 144, 240 };

        // Missing or unknown settings use the monitor-matched FPS limit.
        public static string Normalize(string mode) => mode == Limit ? Limit : Auto;

        public static int AutoFps(double refreshRate) =>
            double.IsNaN(refreshRate)
            || double.IsInfinity(refreshRate)
            || refreshRate < 1
            || refreshRate > int.MaxValue
                ? 60
                : (int)Math.Round(refreshRate, MidpointRounding.AwayFromZero);

        public static int NearestPreset(int fps)
        {
            int closest = Presets[0];
            long distance = Math.Abs((long)fps - closest);
            for (int i = 1; i < Presets.Length; i++)
            {
                long candidateDistance = Math.Abs((long)fps - Presets[i]);
                if (candidateDistance < distance)
                {
                    closest = Presets[i];
                    distance = candidateDistance;
                }
            }
            return closest;
        }

        public bool Follow(
            bool focused,
            string mode,
            int fps,
            bool vsync,
            ref int target,
            ref int sync,
            double refreshRate = 60
        )
        {
            mode = Normalize(mode);
            int nextTarget =
                !focused ? 15
                : vsync ? -1
                : mode == Auto ? AutoFps(refreshRate)
                : NearestPreset(fps);
            // The FPS limit controls the background frame rate. Keep presentation mode tied to the
            // VSync toggle so focus changes do not recreate the Vulkan swapchain.
            int nextSync = vsync ? 1 : 0;
            bool changed = target != nextTarget || sync != nextSync;
            target = nextTarget;
            sync = nextSync;
            return changed;
        }
    }
}
