using UnityEngine;

namespace SlopWorld
{
    // Waylith delivers one accumulated wheel event per frame, in three units per
    // notch. Reader panes retain one step per notch, not one step per frame.
    internal static class TerminalWheelSteps
    {
        internal static int Count(float delta, bool reader)
        {
            float steps = Mathf.Abs(delta) / (reader ? Event.scrollWheelDeltaPerTick : 1f);
            // Preserve the existing fine-gesture response and other applications' speed.
            // Mouse report counts are bytes on the daemon boundary.
            return Mathf.RoundToInt(Mathf.Clamp(steps, 1f, reader ? byte.MaxValue : 5f));
        }
    }
}
