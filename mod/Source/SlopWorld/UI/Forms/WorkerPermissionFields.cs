using System;
using Verse;

namespace SlopWorld
{
    static class WorkerPermissionFields
    {
        public static void Draw(Listing_Standard l, Wire.AgentPermissions value)
        {
            UiLayout.SectionHeading(l, "Permissions");
            UiLayout.Note(
                l,
                "Own workers means direct children. Project agents excludes host terminals."
            );
            Scope(l, "Send follow-ups", value.Followup, next => value.Followup = next);
            Scope(l, "Cancel tasks", value.Cancel, next => value.Cancel = next);
            Scope(l, "Type in terminals", value.Terminal, next => value.Terminal = next);
            Scope(l, "Stop or restart agents", value.Lifecycle, next => value.Lifecycle = next);
            Scope(l, "Delete agents", value.Delete, next => value.Delete = next);
            UiControls.SelectRow(
                l,
                "Manage worktrees",
                value.Worktrees ? "Project worktrees" : "Deny",
                new[]
                {
                    new SelectorOption("Deny", () => value.Worktrees = false),
                    new SelectorOption("Project worktrees", () => value.Worktrees = true),
                },
                out _
            );
        }

        static void Scope(Listing_Standard l, string label, string value, Action<string> set)
        {
            string selected =
                value == "own_workers" ? "Own workers"
                : value == "project_agents" ? "Project agents"
                : "Deny";
            UiControls.SelectRow(
                l,
                label,
                selected,
                new[]
                {
                    new SelectorOption("Deny", () => set("deny")),
                    new SelectorOption("Own workers", () => set("own_workers")),
                    new SelectorOption("Project agents", () => set("project_agents")),
                },
                out _
            );
        }
    }
}
