using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The same independent permissions edit daemon limits, portable policy and existing relationships.
    static class WorkerPermissionFields
    {
        public static void Draw(
            Listing_Standard l,
            string heading,
            Wire.WorkerAccess value,
            Wire.WorkerAccess ceiling = null
        )
        {
            UiLayout.SectionHeading(l, heading);
            UiLayout.Note(
                l,
                "Read progress, results, and diagnostics — always available to the parent."
            );
            value.Followup = Field(
                l,
                "Send follow-up instructions",
                value.Followup,
                ceiling == null || ceiling.Followup
            );
            value.Cancel = Field(
                l,
                "Cancel tasks",
                value.Cancel,
                ceiling == null || ceiling.Cancel
            );
            value.Terminal = Field(
                l,
                "Type in the terminal",
                value.Terminal,
                ceiling == null || ceiling.Terminal
            );
            value.Lifecycle = Field(
                l,
                "Stop and restart workers",
                value.Lifecycle,
                ceiling == null || ceiling.Lifecycle
            );
            if (
                ceiling != null
                && (!ceiling.Followup || !ceiling.Cancel || !ceiling.Terminal || !ceiling.Lifecycle)
            )
                UiLayout.Note(l, "Disabled permissions are not allowed by daemon settings.");
        }

        static bool Field(Listing_Standard l, string label, bool value, bool allowed)
        {
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && allowed;
            try
            {
                return UiControls.Checkbox(l, label, value && allowed) && allowed;
            }
            finally
            {
                GUI.enabled = enabled;
            }
        }
    }
}
