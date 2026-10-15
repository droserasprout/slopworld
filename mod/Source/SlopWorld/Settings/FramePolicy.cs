using System;

namespace SlopWorld
{
    // Select native presentation and target FPS. Unfocused windows always use 15 FPS.
    public sealed class FramePolicy
    {
        public const string Auto = "auto",
            Sync = "sync",
            Limit = "limit";
        public static readonly int[] Presets = { 15, 30, 60, 120, 144, 240 };

        // Missing or unknown settings use the monitor-matched FPS cap.
        public static string Normalize(string mode) =>
            mode == Limit ? Limit
            : mode == Sync ? Sync
            : Auto;

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
            ref int target,
            ref int sync,
            double refreshRate = 60
        )
        {
            mode = Normalize(mode);
            int nextTarget =
                !focused ? 15
                : mode == Sync ? -1
                : mode == Auto ? AutoFps(refreshRate)
                : NearestPreset(fps);
            // The FPS cap handles background pacing. Keep presentation mode tied to the
            // selected policy so focus changes do not recreate the Vulkan swapchain.
            int nextSync = mode == Sync ? 1 : 0;
            bool changed = target != nextTarget || sync != nextSync;
            target = nextTarget;
            sync = nextSync;
            return changed;
        }
    }
}
