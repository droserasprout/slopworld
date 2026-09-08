using System;

namespace SlopWorld
{
    // A rate-limit window, or the extra-usage budget in money. The reset is a duration rather
    // than an instant, so the countdown stays honest when the socket dies.
    public class UsageWindow
    {
        public string Key = "";
        public string Label = "";
        // Percent of the window spent, 0-100. Always sent, money row included.
        public float Pct;
        // A unit this build does not know reads as a percentage.
        public string Unit = WireContract.UsageUnit.Pct;
        // -1 when the daemon sent no figure, which leaves the row a percentage.
        public float Amount = -1f;
        // What Amount is out of; -1 if unsaid.
        public float Limit = -1f;
        // Seconds to the reset as of Heard; -1 if the daemon did not say.
        public long ResetsIn = -1;

        // Both halves are required: a unit with no figure under it has nothing to spend.
        public bool IsMoney => Unit == WireContract.UsageUnit.Usd && Amount >= 0f;
    }
}
