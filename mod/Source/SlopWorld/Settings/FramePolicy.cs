using System;

namespace SlopWorld
{
    // Apply Unity's VSync and target-FPS values together. Unfocused windows always use 15 FPS.
    public sealed class FramePolicy
    {
        public const string Sync = "sync", Limit = "limit";
        public static readonly int[] Presets = { 15, 30, 60, 120, 144, 240 };

        // Values from older settings files that used "game" now use the VSync policy.
        public static string Normalize(string mode) => mode == Limit ? Limit : Sync;

        public static int Clamp(int fps)
        {
            int closest = Presets[0];
            int distance = Math.Abs(fps - closest);
            for (int i = 1; i < Presets.Length; i++)
            {
                int candidateDistance = Math.Abs(fps - Presets[i]);
                if (candidateDistance < distance)
                {
                    closest = Presets[i];
                    distance = candidateDistance;
                }
            }
            return closest;
        }

        public bool Follow(bool focused, string mode, int fps, ref int target, ref int sync)
        {
            mode = Normalize(mode);
            int nextTarget = !focused ? 15 : mode == Sync ? -1 : Clamp(fps);
            int nextSync = focused && mode == Sync ? 1 : 0;
            bool changed = target != nextTarget || sync != nextSync;
            target = nextTarget;
            sync = nextSync;
            return changed;
        }
    }
}
