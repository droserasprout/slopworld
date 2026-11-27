using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SlopWorld
{
    static class WorkerPermissionFields
    {
        public static void Draw(
            Listing_Standard l,
            Wire.AgentPermissions value,
            Wire.BoardCatalog catalog
        )
        {
            UiLayout.SectionHeading(l, "Permissions");
            UiLayout.Note(
                l,
                "Own workers means direct children. Project agents excludes host terminals."
            );
            BoardPermissions(l, value, catalog);
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

        static void BoardPermissions(
            Listing_Standard l,
            Wire.AgentPermissions value,
            Wire.BoardCatalog catalog
        )
        {
            string[] levels = { "Off", "Read", "Write", "Admin" };
            var selected =
                levels.FirstOrDefault(level =>
                    string.Equals(level, value.BoardAccess, StringComparison.OrdinalIgnoreCase)
                ) ?? "Write";
            UiControls.SelectRow(
                l,
                "Board access",
                selected,
                levels
                    .Select(level => new SelectorOption(
                        level,
                        () => value.BoardAccess = level.ToLowerInvariant()
                    ))
                    .ToArray(),
                out _
            );
            bool own = !value.HasOwnProjectBoard || value.OwnProjectBoard;
            var options = new List<(string Label, bool Enabled, Action Toggle)>
            {
                ("Agent’s project", own, () => value.OwnProjectBoard = !own),
            };
            var shown = new HashSet<string>();
            void Board(string id, string label)
            {
                if (string.IsNullOrEmpty(id) || !shown.Add(id))
                    return;
                bool current = value.VisibleBoards.Contains(id);
                options.Add(
                    (
                        label,
                        current,
                        () =>
                        {
                            if (current)
                                value.VisibleBoards.Remove(id);
                            else
                                value.VisibleBoards.Add(id);
                        }
                    )
                );
            }
            if (catalog != null)
            {
                foreach (var board in catalog.Boards)
                    Board(board.Id, board.Name + (board.Archived ? " (archived)" : ""));
            }
            foreach (var id in value.VisibleBoards)
                Board(id, id);
            var selectedBoards = options.Where(o => o.Enabled).Select(o => o.Label);
            UiControls.Select(
                l,
                "Visible boards",
                string.Join(", ", selectedBoards.DefaultIfEmpty("None")),
                options.Select(o => UiLayout.MenuToggle(o.Label, o.Enabled, o.Toggle)),
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
