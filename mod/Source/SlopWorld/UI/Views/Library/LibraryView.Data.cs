using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using WorktreeEntry = SlopWorld.WorktreeInventory.WorktreeEntry;

namespace SlopWorld
{
    public static partial class LibraryView
    {
        static bool Runnable(LibraryItemInfo item) =>
            !Templates.ContainsKey(item) &&
            (item.Kind == LibraryItemKind.Prompt || item.Kind == LibraryItemKind.Shell);

        static Row PrepareItemRow(LibraryItemInfo item)
        {
            bool template = Templates.ContainsKey(item);
            return new Row
            {
                Item = item,
                Key = Identity(item),
                Label = item.Name,
                Icon = template ? Icons.Agents :
                    item.Kind == LibraryItemKind.Shell ? Icons.Terminal :
                    item.Kind == LibraryItemKind.FileAction ? Icons.Files :
                    item.Kind == LibraryItemKind.Breadcrumb ? Icons.Keyboard : Icons.Library,
            };
        }

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

        static RowDetails PrepareItemDetails(LibraryItemInfo item)
        {
            bool template = Templates.TryGetValue(item, out var definition);
            bool runnable = Runnable(item);
            string label = template ? "Create agent" : runnable
                ? (item.Link == LibraryItemLink.Ask ? "Choose a project" : "Run") : "Edit";
            var lines = ItemSummary(item);
            string content = ItemContent(item);
            string tooltip = string.Join("\n", lines);
            if (!string.IsNullOrEmpty(content))
            {
                tooltip += "\n\n" + content;
                lines.Add(OneLine(content));
            }

            Action primary;
            if (template)
            {
                var captured = definition;
                primary = () => TerminalWindow.OpenOverPane(EditSessionDialog.FromTemplate(captured));
            }
            else if (runnable)
                primary = () => Run(item);
            else
                primary = () => Edit(item);

            string secondaryLabel = null;
            Action secondary = null;
            if (template || runnable)
            {
                secondaryLabel = "Edit";
                secondary = () => Edit(item);
            }
            else if (item.Kind == LibraryItemKind.Breadcrumb)
            {
                secondaryLabel = "Copy text";
                secondary = () => GUIUtility.systemCopyBuffer = item.Text ?? "";
            }

            return new RowDetails
            {
                IsItem = true,
                Lines = lines,
                Tooltip = tooltip,
                PrimaryLabel = label,
                Primary = primary,
                SecondaryLabel = secondaryLabel,
                Secondary = secondary,
                More = () => Menu(item),
            };
        }

        static RowDetails PrepareDetails(Row row)
        {
            if (row.Item != null) return PrepareItemDetails(row.Item);

            switch (row.MainKind)
            {
                case MainRowKind.Project:
                    return PrepareProjectDetails((ProjectInfo)row.Model);
                case MainRowKind.Worktree:
                    return PrepareWorktreeDetails((WorktreeEntry)row.Model);
                case MainRowKind.SandboxPreset:
                    return PrepareSandboxPresetDetails((PresetInfo)row.Model);
                case MainRowKind.AppPreset:
                    return PrepareAppPresetDetails((CommandInfo)row.Model);
                default:
                    throw new ArgumentOutOfRangeException(nameof(row), row.MainKind, "Unknown library row kind.");
            }
        }

        static RowDetails PrepareProjectDetails(ProjectInfo project) => ActionDetails(
            new List<string> { project.Name, "Project", project.Dir, ProjectSummary.Of(project) },
            "Edit", () => TerminalWindow.OpenOverPane(new EditProjectDialog(project)),
            "Worktrees", () => TerminalWindow.OpenOverPane(EditProjectDialog.ForWorktrees(project)),
            () => ProjectMenu(project));

        static RowDetails PrepareWorktreeDetails(WorktreeEntry entry)
        {
            var tree = entry.Tree;
            return ActionDetails(WorktreeDetails(entry), "Terminal",
                () => SessionHub.Instance.SessionStore.RunHostShell(entry.Project,
                    name => TerminalWindow.Open(name), UiLayout.Fail, tree.Id),
                "Project", () => OpenProjectWorktrees(entry.Project),
                () => WorktreeMenu(entry));
        }

        static RowDetails PrepareSandboxPresetDetails(PresetInfo preset) => ActionDetails(
            SandboxDetails(preset), "Edit",
            () => ModOptions.OpenSandboxPreset(preset.Name),
            preset.Source == "override" ? "Reset" : "Remove",
            () => RemovePreset("sandbox_presets", preset), null);

        static RowDetails PrepareAppPresetDetails(CommandInfo command) => ActionDetails(
            AppPresetDetails(command), "Edit",
            () => ModOptions.OpenAppPreset(command.Name),
            command.Source == "override" ? "Reset" : "Remove",
            () => RemovePreset("app_presets", command), null);

        static RowDetails ActionDetails(List<string> lines, string primaryLabel,
            Action primary, string secondaryLabel, Action secondary, Action more)
        {
            var visibleLines = lines.Where(line => !string.IsNullOrEmpty(line)).ToList();
            return new RowDetails
            {
                Lines = visibleLines,
                Tooltip = string.Join("\n", visibleLines.ToArray()),
                PrimaryLabel = primaryLabel,
                Primary = primary,
                SecondaryLabel = secondaryLabel,
                Secondary = secondary,
                More = more,
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
                EnsureSection(GroupKey(item)).Rows.Add(Row.ForItem(item));

            BuildMainCategories(projects);

            Sections.Clear();
            foreach (var key in Category.Order)
            {
                if (!SectionsByKey.TryGetValue(key, out var section) || section.Rows.Count == 0)
                    continue;
                if (section.Rows[0].Item != null)
                    section.Rows.Sort((a, b) => string.CompareOrdinal(
                        a.Item?.Name ?? "", b.Item?.Name ?? ""));
                Sections.Add(section);
            }
        }

        static Row FindSelectedRow()
        {
            if (string.IsNullOrEmpty(_selection)) return null;
            foreach (var section in Sections)
                foreach (var row in section.Rows)
                    if (row.Key == _selection) return row;

            // Action selections are valid only while their category row remains visible.
            // Item identities survive filtering so the selection can return with its row.
            if (_selection.StartsWith("action:", StringComparison.Ordinal)) _selection = null;
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
            {
                var captured = project;
                AddMainRow(Category.Projects, "project:" + captured.Name,
                    captured.Name, Icons.Files, captured.Name, captured.Dir, true,
                    MainRowKind.Project, captured,
                    MatchesAny(query, Category.Projects, captured.Name, captured.Dir));
            }
        }

        static void BuildWorktreeRows(List<ProjectInfo> projects, string query)
        {
            if (!MainCategoryEnabled(Category.Worktrees)) return;
            foreach (var project in projects)
            {
                if (!Worktrees.WorktreesByProject.TryGetValue(project.Name, out var worktrees)) continue;
                foreach (var worktree in worktrees)
                {
                    var captured = worktree;
                    var tree = captured.Tree;
                    string treeName = tree.Id == "main" ? "main" :
                        string.IsNullOrEmpty(tree.Name) ? tree.Id : tree.Name;
                    string label = captured.Project + "  ·  " + treeName;
                    AddMainRow(Category.Worktrees,
                        "worktree:" + captured.Project + ":" + tree.Id, label,
                        Icons.Git, label, tree.Path, true, MainRowKind.Worktree, captured,
                        MatchesAny(query, Category.Worktrees, captured.Project,
                            treeName, tree.Path, tree.Branch));
                }
            }
        }

        static void BuildSandboxPresetRows(string query)
        {
            if (!MainCategoryEnabled(Category.SandboxPresets)) return;
            foreach (var preset in SessionHub.Instance.Presets
                .Where(preset => preset.Source != "system")
                .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase))
            {
                var captured = preset;
                string label = PresetLabel(captured.Name, captured.Source);
                AddMainRow(Category.SandboxPresets,
                    "sandbox:" + captured.Name, label, Icons.Shield,
                    captured.Description, null, false, MainRowKind.SandboxPreset, captured,
                    MatchesAny(query, Category.SandboxPresets, captured.Name, captured.Description));
            }
        }

        static void BuildAppPresetRows(string query)
        {
            if (!MainCategoryEnabled(Category.AppPresets)) return;
            foreach (var command in SessionHub.Instance.Commands
                .Where(command => command.Source != "system")
                .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase))
            {
                var captured = command;
                string label = PresetLabel(captured.Name, captured.Source);
                AddMainRow(Category.AppPresets,
                    "app:" + captured.Name, label, Icons.Terminal,
                    captured.Description, null, false, MainRowKind.AppPreset, captured,
                    MatchesAny(query, Category.AppPresets, captured.Name,
                        captured.Description, captured.Cmd));
            }
        }

        static bool MatchesAny(string query, string first, string second,
            string third = null, string fourth = null, string fifth = null) =>
            query.Length == 0 || Matches(query, first) || Matches(query, second) ||
            Matches(query, third) || Matches(query, fourth) || Matches(query, fifth);

        static bool Matches(string query, string value) =>
            (value ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        static bool MainCategoryEnabled(string key) => _kind.Length == 0 || _kind == key;

        static void AddMainRow(string sectionKey, string key, string label, Texture2D icon,
            string tooltip, string tooltipTail, bool tooltipJoinsPath,
            MainRowKind kind, object model, bool include)
        {
            if (!include) return;
            EnsureSection(sectionKey).Rows.Add(new Row
            {
                Key = "action:" + key,
                Label = label,
                Tooltip = tooltip,
                TooltipTail = tooltipTail,
                TooltipJoinsPath = tooltipJoinsPath,
                Icon = icon,
                MainKind = kind,
                Model = model,
            });
        }

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
