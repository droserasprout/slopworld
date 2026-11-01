using Verse;

namespace SlopWorld
{
    // Independent permissions for portable policy and existing relationships.
    static class WorkerPermissionFields
    {
        public static void Draw(
            Listing_Standard l,
            string heading,
            Wire.WorkerAccess value,
            bool locked = false
        )
        {
            UiLayout.SectionHeading(l, heading);
            UiLayout.Note(l, "The parent can always read progress, results, and diagnostics.");
            value.Followup = UiControls.Checkbox(
                l,
                "Send follow-ups",
                value.Followup,
                locked: locked
            );
            value.Cancel = UiControls.Checkbox(l, "Cancel tasks", value.Cancel, locked: locked);
            value.Terminal = UiControls.Checkbox(
                l,
                "Type in terminal",
                value.Terminal,
                locked: locked
            );
            value.Lifecycle = UiControls.Checkbox(
                l,
                "Stop or restart workers",
                value.Lifecycle,
                locked: locked
            );
        }
    }
}
