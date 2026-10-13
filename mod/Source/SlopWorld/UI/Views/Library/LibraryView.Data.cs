using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using WorktreeEntry = SlopWorld.WorktreeInventory.WorktreeEntry;

namespace SlopWorld
{
    public static partial class LibraryView
    {
        static bool Runnable(LibraryItemInfo item) =>
            !Templates.ContainsKey(item) &&
            (item.Kind == LibraryItemKind.Prompt || item.Kind == LibraryItemKind.Shell);

        static List<string> ItemSummary(LibraryItemInfo item)
        {
            bool template = Templates.ContainsKey(item);
            bool runnable = Runnable(item);
            var lines = new List<string>
            {
                item.Name,
                template ? "Agent template" : KindName(item.Kind),
            };
            if (runnable)
            {
                lines.Add(item.Link == LibraryItemLink.Ask
                    ? "Choose a project when you run this item."
                    : "In: " + Where(item));
                lines.Add("Runs with: " + (item.Host ? "Host" :
                    string.IsNullOrEmpty(item.AgentTemplate) ? "Not configured" : item.AgentTemplate));
            }
            else if (item.Kind == LibraryItemKind.FileAction && !template)
                lines.Add("Host · " + EditLibraryItemDialog.FileActionModeLabel(item.Mode));
            else if (item.Kind == LibraryItemKind.Breadcrumb && !template &&
                !string.IsNullOrEmpty(item.Project))
                lines.Add("Project: " + item.Project);
            return lines;
        }

        static string ItemContent(LibraryItemInfo item) =>
            item.Kind == LibraryItemKind.FileAction && !Templates.ContainsKey(item)
                ? item.Command : item.Text;

        static RowDetails PrepareItemDetails(LibraryItemInfo item, LibraryRowTarget.Actions actions)
        {
            var lines = ItemSummary(item);
            string content = ItemContent(item);
            string tooltip = string.Join("\n", lines);
            if (!string.IsNullOrEmpty(content))
            {
                tooltip += "\n\n" + content;
                lines.Add(OneLine(content));
            }

            return new RowDetails
            {
                IsItem = true,
                Lines = lines,
                Tooltip = tooltip,
                PrimaryLabel = actions.PrimaryLabel,
                Primary = actions.Primary,
                SecondaryLabel = actions.SecondaryLabel,
                Secondary = actions.Secondary,
                More = actions.More,
            };
        }

        static RowDetails ActionDetails(List<string> lines, LibraryRowTarget.Actions actions)
        {
            var visibleLines = lines.Where(line => !string.IsNullOrEmpty(line)).ToList();
            return new RowDetails
            {
                Lines = visibleLines,
                Tooltip = string.Join("\n", visibleLines.ToArray()),
                PrimaryLabel = actions.PrimaryLabel,
                Primary = actions.Primary,
                SecondaryLabel = actions.SecondaryLabel,
                Secondary = actions.Secondary,
                More = actions.More,
            };
        }

        static Section EnsureSection(string key)
        {
            if (!SectionsByKey.TryGetValue(key, out var section))
                SectionsByKey[key] = section = new Section { Key = key };
            return section;
        }

        static void BuildSections(List<ProjectInfo> projects)
        {
            foreach (var section in SectionsByKey.Values) section.Rows.Clear();
            foreach (var item in _items)
                EnsureSection(GroupKey(item)).Rows.Add(RowFor(item));

            BuildMainCategories(projects);

            Sections.Clear();
            foreach (var key in Category.Order)
            {
                if (!SectionsByKey.TryGetValue(key, out var section) || section.Rows.Count == 0)
                    continue;
                if (section.Rows[0].Target is LibraryRowTarget.Item ||
                    section.Rows[0].Target is LibraryRowTarget.Template)
                    section.Rows.Sort((a, b) => string.CompareOrdinal(
                        a.Label, b.Label));
                Sections.Add(section);
            }
        }

        static Row FindSelectedRow()
        {
            if (!Selection.Reconcile(Sections.SelectMany(section => section.Rows).Select(row => row.Key)))
                return null;
            foreach (var section in Sections)
                foreach (var row in section.Rows)
                    if (row.Key == Selection.Key) return row;

            return null;
        }

        static void BuildMainCategories(List<ProjectInfo> projects)
        {
            string query = _query.Trim();
            BuildProjectRows(projects, query);
            BuildWorktreeRows(projects, query);
            BuildSandboxPresetRows(query);
            BuildAppPresetRows(query);
        }

        static void BuildProjectRows(List<ProjectInfo> projects, string query)
        {
            if (!MainCategoryEnabled(Category.Projects)) return;
            foreach (var project in projects)
                if (MatchesAny(query, Category.Projects, project.Name, project.Dir))
                    EnsureSection(Category.Projects).Rows.Add(RowFor(project));
        }

        static void BuildWorktreeRows(List<ProjectInfo> projects, string query)
        {
            if (!MainCategoryEnabled(Category.Worktrees)) return;
            foreach (var project in projects)
            {
                if (!Worktrees.WorktreesByProject.TryGetValue(project.Name, out var worktrees)) continue;
                foreach (var entry in worktrees)
                    if (MatchesAny(query, Category.Worktrees, entry.Project,
                        TreeName(entry), entry.Tree.Path, entry.Tree.Branch))
                        EnsureSection(Category.Worktrees).Rows.Add(RowFor(entry));
            }
        }

        static void BuildSandboxPresetRows(string query)
        {
            if (!MainCategoryEnabled(Category.SandboxPresets)) return;
            foreach (var preset in SessionHub.Instance.Presets
                .Where(preset => preset.Source != "system")
                .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase))
                if (MatchesAny(query, Category.SandboxPresets, preset.Name, preset.Description))
                    EnsureSection(Category.SandboxPresets).Rows.Add(RowFor(preset));
        }

        static void BuildAppPresetRows(string query)
        {
            if (!MainCategoryEnabled(Category.AppPresets)) return;
            foreach (var command in SessionHub.Instance.Commands
                .Where(command => command.Source != "system")
                .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase))
                if (MatchesAny(query, Category.AppPresets, command.Name, command.Description, command.Cmd))
                    EnsureSection(Category.AppPresets).Rows.Add(RowFor(command));
        }

        static bool MatchesAny(string query, string first, string second,
            string third = null, string fourth = null, string fifth = null) =>
            query.Length == 0 || Matches(query, first) || Matches(query, second) ||
            Matches(query, third) || Matches(query, fourth) || Matches(query, fifth);

        static bool Matches(string query, string value) =>
            (value ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        static bool MainCategoryEnabled(string key) => _kind.Length == 0 || _kind == key;

        static string PresetLabel(string name, string source) =>
            name + (source == "override" ? "  (override)" : "");

        static List<string> SandboxDetails(PresetInfo preset)
        {
            var details = new List<string>
            {
                preset.Name,
                "Sandbox preset · " + SourceName(preset.Source),
            };
            if (!string.IsNullOrEmpty(preset.Description)) details.Add(preset.Description);
            if (preset.Requires.Count > 0)
                details.Add("Requires: " + string.Join(", ", preset.Requires.ToArray()));
            if (preset.Gives.Count > 0)
                details.Add("Provides: " + string.Join(", ", preset.Gives.ToArray()));
            return details;
        }

        static List<string> AppPresetDetails(CommandInfo command)
        {
            var details = new List<string>
            {
                command.Name,
                "App preset · " + SourceName(command.Source),
            };
            if (!string.IsNullOrEmpty(command.Description)) details.Add(command.Description);
            details.Add("Kind: " + (string.IsNullOrEmpty(command.Kind) ? "agent" : command.Kind));
            if (!string.IsNullOrEmpty(command.Cmd)) details.Add("Command: " + command.Cmd);
            if (command.Sandbox.Count > 0)
                details.Add("Sandbox: " + string.Join(", ", command.Sandbox.ToArray()));
            return details;
        }

        static string SourceName(string source) => source == "override" ? "user override" : "user";

        static List<string> WorktreeDetails(WorktreeEntry entry)
        {
            var tree = entry.Tree;
            var details = new List<string>
            {
                tree.Id == "main" ? "main" : tree.Name,
                "Worktree · " + entry.Project,
                "Phase: " + (string.IsNullOrEmpty(tree.Phase) ? "unknown" : tree.Phase),
            };
            if (!string.IsNullOrEmpty(tree.Branch)) details.Add("Branch: " + tree.Branch);
            else details.Add("Branch: detached");
            if (!string.IsNullOrEmpty(tree.Path)) details.Add(tree.Path);
            if (!string.IsNullOrEmpty(tree.Head)) details.Add("HEAD: " + tree.Head);
            if (tree.Attachments.Count > 0)
                details.Add("Attached: " + string.Join(", ", tree.Attachments.ToArray()));
            if (!string.IsNullOrEmpty(tree.Error)) details.Add(tree.Error);
            return details;
        }

    }
}
