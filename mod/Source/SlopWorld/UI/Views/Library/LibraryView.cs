using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Draw type-grouped library rows from AgentSidebar's back pass. The shared AddBar owns
    // creation and keeps the view usable over a terminal.
    public static class LibraryView
    {
        // Off the font, for the reason the other two trees' are.
        static float RowH => UiTheme.TinyRowH;
        static float HeadH => UiTheme.TinyRowH;
        static float Pad => UiTheme.GapS;
        static float CellX => UiTheme.GapS;
        const float ArrowW = UiTheme.DisclosureW;

        // The text that drives the rows, snapshotted once per frame so size and draw agree.
        static List<LibraryItemInfo> _items = new List<LibraryItemInfo>();

        static readonly Dictionary<LibraryItemInfo, AgentTemplateInfo> Templates =
            new Dictionary<LibraryItemInfo, AgentTemplateInfo>();

        public static void Refresh(Action<string> fail = null)
        {
            SessionHub.Instance.Catalog.RefreshProjects(fail);
            SessionHub.Instance.Catalog.RefreshLibrary(fail);
            SessionHub.Instance.Catalog.RefreshTemplates(fail);
            SessionHub.Instance.Catalog.LoadPresets(fail: fail);
            _worktreeRequestKey = null;
        }

        // Grouped by catalog kind. The Library owns its project scope independently from the
        // global sidebar filter.
        static readonly Dictionary<string, Section> SectionsByKey =
            new Dictionary<string, Section>();
        static readonly List<Section> Sections = new List<Section>();
        static class Category
        {
            public const string Templates = "Agent templates";
            public const string Prompts = "Prompts";
            public const string Shell = "Shell commands";
            public const string Breadcrumbs = "Breadcrumbs";
            public const string FileActions = "File actions";
            public const string Projects = "Projects";
            public const string Worktrees = "Worktrees";
            public const string SandboxPresets = "Sandbox presets";
            public const string AppPresets = "App presets";

            public static readonly string[] Order =
            {
                Templates, Prompts, Shell, Breadcrumbs, FileActions,
                Projects, Worktrees, SandboxPresets, AppPresets
            };

            public static readonly string[] Main =
                { Projects, Worktrees, SandboxPresets, AppPresets };
        }
        static readonly Dictionary<string, List<WorktreeEntry>> WorktreesByProject =
            new Dictionary<string, List<WorktreeEntry>>();
        static string _worktreeRequestKey;
        static int _worktreeGeneration;
        static string _query = "";
        static string _kind = "";
        static readonly HashSet<string> ProjectFilter = new HashSet<string>();
        static string _selection;
        static bool _revealSelection;
        static Rect _list;
        static FieldLifetime _fieldLifetime = new FieldLifetime();

        static string GroupKey(LibraryItemInfo item) => Templates.ContainsKey(item)
            ? Category.Templates : item.Kind == LibraryItemKind.Shell ? Category.Shell
            : item.Kind == LibraryItemKind.Breadcrumb ? Category.Breadcrumbs
            : item.Kind == LibraryItemKind.FileAction ? Category.FileActions : Category.Prompts;

        static string Identity(LibraryItemInfo item) =>
            (Templates.ContainsKey(item) ? "template:" : "item:") + item.Name;

        public static void Closed()
        {
            _fieldLifetime.Cancel();
            _fieldLifetime = new FieldLifetime();
            if (GUI.GetNameOfFocusedControl() == "library.query") GUI.FocusControl(null);
        }

        // Record collapsed headings only in memory.
        // Like folds in other views, this state belongs to the view and not to the saved colony.
        static readonly HashSet<string> Folded = new HashSet<string>();

        public static bool AllFolded => Sections.Count > 0 &&
            Sections.All(section => Folded.Contains(section.Key));

        public static void SetAllFolded(bool folded)
        {
            Folded.Clear();
            if (folded)
            {
                foreach (var section in Sections) Folded.Add(section.Key);
                foreach (var key in Category.Main) Folded.Add(key);
            }
        }

        // The drawn lines, rebuilt each frame so clicks and drawing agree. Rects are in
        // screen space - see Screen - because Clicks runs outside the scroll view.
        struct Line
        {
            public Row Row;
            public bool Head;    // true on a heading, false on a row
            public string Key;   // section heading
            public Rect Rect;
        }

        sealed class Section
        {
            public string Key;
            public readonly List<Row> Rows = new List<Row>();
        }

        enum MainRowKind { Project, Worktree, SandboxPreset, AppPreset }

        // Keep list data small. Detail text and actions are built only for the selected row;
        // row tooltip text is formatted only while a visible row is drawn.
        sealed class Row
        {
            public LibraryItemInfo Item;
            public MainRowKind MainKind;
            public object Model;
            public string Key;
            public string Label;
            public Texture2D Icon;
            public string Tooltip;
            public string TooltipTail;
            public bool TooltipJoinsPath;

            public static Row ForItem(LibraryItemInfo item) => PrepareItemRow(item);
        }

        sealed class RowDetails
        {
            public bool IsItem;
            public List<string> Lines = new List<string>();
            public string Tooltip;
            public string PrimaryLabel;
            public Action Primary;
            public string SecondaryLabel;
            public Action Secondary;
            public Action More;
        }

        sealed class WorktreeEntry
        {
            public string Project;
            public Wire.Worktree Tree;
        }

        static readonly List<Line> Lines = new List<Line>();

        // ------------------------------------------------------------------ drawing

        // Only the list scrolls. Clip hit targets to its viewport so partially visible
        // rows cannot intercept search or detail actions. The shared AddBar owns creation.
        static Rect Screen(Rect r)
        {
            var list = _list;
            var moved = new Rect(list.x + r.x, list.y + r.y - _scroll.Position.y, r.width, r.height);
            float top = Mathf.Max(moved.y, list.y);
            float bottom = Mathf.Min(moved.yMax, list.yMax);
            return bottom <= top ? Rect.zero : new Rect(moved.x, top, moved.width, bottom - top);
        }

        static readonly SmoothScroll _scroll = new SmoothScroll();
        static float _contentHeight;

        public static bool ProjectFiltering => ProjectFilter.Count > 0;

        public static string ProjectFilterLabel
        {
            get
            {
                if (ProjectFilter.Count != 1) return ProjectFilter.Count + " projects";
                foreach (var key in ProjectFilter) return key;
                return "";
            }
        }

        public static bool PassesProject(string project)
        {
            string key = string.IsNullOrEmpty(project) ? AgentSidebar.NoProject : project;
            return !ProjectFiltering || ProjectFilter.Contains(key);
        }

        public static void OpenProjectFilterMenu(Rect anchor)
        {
            var options = new List<FloatMenuOption>
            {
                UiLayout.MenuToggle("All projects", !ProjectFiltering,
                    () => ToggleProjectFilter("", anchor)),
            };
            var names = SessionHub.Instance.Projects
                .Select(project => project.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            foreach (var name in names)
            {
                var key = name;
                options.Add(UiLayout.MenuToggle(key, ProjectFilter.Contains(key),
                    () => ToggleProjectFilter(key, anchor)));
            }
            options.Add(UiLayout.MenuToggle(AgentSidebar.NoProject,
                ProjectFilter.Contains(AgentSidebar.NoProject),
                () => ToggleProjectFilter(AgentSidebar.NoProject, anchor)));
            TerminalWindow.OpenOverPane(new UiMenu(options,
                new Vector2(anchor.x, anchor.yMax)));
        }

        static void ToggleProjectFilter(string key, Rect anchor)
        {
            if (key.Length == 0)
                ProjectFilter.Clear();
            else if (!ProjectFilter.Remove(key))
                ProjectFilter.Add(key);
            ProjectFilterChanged();
            OpenProjectFilterMenu(anchor);
        }

        public static void Draw(Rect body)
        {
            Lines.Clear();
            if (_scroll.HandleWheel(_list, _contentHeight)) return;
            using (FieldLifetimeScope.Push(_fieldLifetime))
            using (WidgetState.Save())
            {
                // Global definitions stay available while project-specific entries follow
                // the Library's own filter. Builtins remain in their attached menus.
                _items = SessionHub.Instance.Library
                    .Where(s => !s.Builtin && (string.IsNullOrEmpty(s.Project) || PassesProject(s.Project))).ToList();
                Templates.Clear();
                foreach (var template in SessionHub.Instance.Templates)
                {
                    var row = new LibraryItemInfo
                    {
                        Name = template.Name,
                        Project = "",
                        Text = template.Description
                    };
                    Templates[row] = template;
                    _items.Add(row);
                }
                DrawTools(body);
                _items = _items.Where(item => (_kind.Length == 0 || GroupKey(item) == _kind) &&
                    (item.Name.IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     (item.Text ?? "").IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                BuildSections();
                var selected = FindSelectedRow();
                float top = body.y + 2f * (UiTheme.FieldH + Pad) + Pad;
                float available = Mathf.Max(0f, body.yMax - top);
                float detailHeight = selected == null ? 0f :
                    Mathf.Min(RowH * 5f + UiTheme.FieldH * 2f + Pad * 4f,
                        Mathf.Max(0f, available - RowH * 2f));
                var selectedDetails = selected != null && detailHeight > 0f
                    ? PrepareDetails(selected) : null;
                _list = new Rect(body.x, top, body.width, Mathf.Max(0f, available - detailHeight));
                if (selected != null && detailHeight > 0f)
                    DrawDetails(new Rect(body.x + CellX, _list.yMax,
                        body.width - CellX * 2f, detailHeight), selectedDetails);
                if (Sections.Count == 0)
                {
                    _contentHeight = 0f;
                    Empty(_list);
                    return;
                }
                var list = _list;
                if (_revealSelection)
                {
                    float y = Pad;
                    foreach (var section in Sections)
                    {
                        y += HeadH;
                        if (Folded.Contains(section.Key)) continue;
                        foreach (var row in section.Rows)
                        {
                            if (row.Key == _selection)
                                _scroll.Reveal(y, RowH, list.height);
                            y += RowH;
                        }
                    }
                    _revealSelection = false;
                }
                float height = Measure();
                _contentHeight = height;
                var geometry = UiScrollBody.Measure(list, height,
                    UiScrollbarReservation.WhenNeeded);
                var view = geometry.View;

                // AgentSidebar.DrawBack skips Layout events, so use GUI rather than GUILayout.
                using (_scroll.Scope(list, view))
                {
                    float y = Pad;
                    foreach (var section in Sections)
                        y += DrawSection(view, y, section);
                }
            }
        }

        static void DrawTools(Rect body)
        {
            var field = new Rect(body.x + CellX, body.y + Pad,
                Mathf.Max(0f, body.width - CellX * 2f), UiTheme.FieldH);
            var e = Event.current;
            if ((e.type == EventType.MouseDown && !field.Contains(e.mousePosition)) ||
                (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape))
                Closed();
            string before = _query;
            _query = UiText.Field(field, "library.query", _query);
            if (before != _query) _scroll.JumpTo(Vector2.zero);
            if (_query.Length == 0 && GUI.GetNameOfFocusedControl() != "library.query")
                UiText.PlainStatusLabel(field.ContractedBy(Pad, 0f), "Search library", UiTheme.Faint, GameFont.Tiny);
            TooltipHandler.TipRegion(field, "Search library names and content");
            var filter = new Rect(field.x, field.yMax + Pad, field.width, UiTheme.FieldH);
            if (UiButtons.Button(filter, _kind.Length == 0 ? "All types ▾" : _kind + " ▾"))
            {
                var options = new List<FloatMenuOption>
                {
                    new FloatMenuOption("All types", () => SetKind(""))
                };
                foreach (var kind in Category.Order)
                {
                    string value = kind;
                    options.Add(new FloatMenuOption(value, () => SetKind(value)));
                }
                TerminalWindow.OpenOverPane(new UiMenu(options));
            }
        }

        static void SetKind(string kind)
        {
            _kind = kind;
            _scroll.JumpTo(Vector2.zero);
        }

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

        static RowDetails PrepareItemDetails(LibraryItemInfo item)
        {
            bool template = Templates.TryGetValue(item, out var definition);
            bool runnable = Runnable(item);
            string scope = string.IsNullOrEmpty(item.Project) ? "Global" : item.Project;
            string label = template ? "Create agent" : runnable
                ? (item.Link == LibraryItemLink.Ask ? "Choose a project" : "Run") : "Edit";
            var lines = new List<string>
            {
                item.Name,
                (template ? "Agent template" : KindName(item.Kind)) + " · " + scope,
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
                lines.Add("Host · " + FileActionModeText.Name(item.Mode));
            lines.Add(OneLine(item.Text));

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
                Tooltip = string.Join("\n", lines) + "\n\n" + item.Text,
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
                    throw new ArgumentOutOfRangeException();
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

        static void BuildSections()
        {
            foreach (var section in SectionsByKey.Values) section.Rows.Clear();
            foreach (var item in _items)
                EnsureSection(GroupKey(item)).Rows.Add(Row.ForItem(item));

            BuildMainCategories();

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

        static void BuildMainCategories()
        {
            string query = _query.Trim();
            var projects = SessionHub.Instance.Projects
                .Where(project => PassesProject(project.Name))
                .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

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
            EnsureWorktrees(projects);
            foreach (var project in projects)
            {
                if (!WorktreesByProject.TryGetValue(project.Name, out var worktrees)) continue;
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

        static void EnsureWorktrees(List<ProjectInfo> projects)
        {
            string key = SessionHub.Instance.Catalog.ProjectsRevision + "\n" +
                string.Join("\n", projects.Select(project =>
                    project.Name + "\t" + project.ExpandedDir).ToArray());
            if (_worktreeRequestKey == key) return;

            _worktreeRequestKey = key;
            WorktreesByProject.Clear();
            int generation = ++_worktreeGeneration;
            foreach (var project in projects)
            {
                var captured = project;
                DaemonClient.Get<Wire.WorktreesReply>(
                    WireProtocol.Routes.Worktrees + "?project=" + Uri.EscapeDataString(captured.Name),
                    reply =>
                    {
                        if (generation != _worktreeGeneration) return;
                        var rows = reply.Worktrees.Select(tree => new WorktreeEntry
                        {
                            Project = captured.Name,
                            Tree = tree,
                        }).ToList();
                        if (!rows.Any(row => row.Tree.Id == "main"))
                            rows.Insert(0, new WorktreeEntry
                            {
                                Project = captured.Name,
                                Tree = new Wire.Worktree
                                {
                                    Id = "main", Name = "main", Path = captured.ExpandedDir,
                                    Phase = "ready"
                                }
                            });
                        WorktreesByProject[captured.Name] = rows
                            .OrderBy(row => row.Tree.Id == "main" ? 0 : 1)
                            .ThenBy(row => row.Tree.Name, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    },
                    error =>
                    {
                        if (generation == _worktreeGeneration)
                            WorktreesByProject[captured.Name] = new List<WorktreeEntry>();
                    }, TaskInfo.Host, 60000);
            }
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

        static void OpenProjectWorktrees(string project)
        {
            var info = SessionHub.Instance.Catalog.Project(project);
            if (info != null) TerminalWindow.OpenOverPane(EditProjectDialog.ForWorktrees(info));
        }

        static void ProjectMenu(ProjectInfo project)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Edit", () =>
                    TerminalWindow.OpenOverPane(new EditProjectDialog(project))),
                new FloatMenuOption("Duplicate", () =>
                    TerminalWindow.OpenOverPane(EditProjectDialog.Copy(project))),
                new FloatMenuOption("Manage worktrees", () => OpenProjectWorktrees(project.Name)),
                new FloatMenuOption("Terminal (host)", () =>
                    SessionHub.Instance.SessionStore.RunHostShell(project.Name,
                        name => TerminalWindow.Open(name), UiLayout.Fail)),
                new FloatMenuOption("Delete", () => TerminalWindow.OpenOverPane(
                    CatalogActions.RemoveProject(project.Name))),
            };
            TerminalWindow.OpenOverPane(new UiMenu(options));
        }

        static void WorktreeMenu(WorktreeEntry entry)
        {
            var tree = entry.Tree;
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Terminal", () =>
                    SessionHub.Instance.SessionStore.RunHostShell(entry.Project,
                        name => TerminalWindow.Open(name), UiLayout.Fail, tree.Id)),
                new FloatMenuOption("Project", () => OpenProjectWorktrees(entry.Project)),
            };
            if (tree.Id != "main")
                options.Add(new FloatMenuOption("Remove", () => RemoveWorktree(entry)));
            TerminalWindow.OpenOverPane(new UiMenu(options));
        }

        static void RemoveWorktree(WorktreeEntry entry)
        {
            if (entry.Tree.Attachments.Count > 0) return;
            TerminalWindow.OpenOverPane(ConfirmDialog.Create(
                "Remove worktree '" + entry.Tree.Name + "'? This removes managed files. " +
                "It unregisters unmanaged files without deleting them.",
                () =>
                {
                    _worktreeRequestKey = null;
                    DaemonClient.Send<Wire.Ack>("DELETE",
                        WireProtocol.Routes.Worktrees + "/" + Uri.EscapeDataString(entry.Tree.Id) +
                        "?project=" + Uri.EscapeDataString(entry.Project), null,
                        _ => { }, UiLayout.Fail, TaskInfo.Host, 60000);
                }, destructive: true));
        }

        static void RemovePreset(string kind, PresetInfo preset)
        {
            string message = preset.Source == "override"
                ? "Reset this user override and return to the system preset?"
                : "Remove this user preset?";
            TerminalWindow.OpenOverPane(ConfirmDialog.Create(message, () =>
                SessionHub.Instance.Catalog.RemovePreset(kind, preset.Name, () =>
                {
                    ClearActionSelection();
                }, UiLayout.Fail), destructive: true));
        }

        static void RemovePreset(string kind, CommandInfo command)
        {
            string message = command.Source == "override"
                ? "Reset this user override and return to the system preset?"
                : "Remove this user preset?";
            TerminalWindow.OpenOverPane(ConfirmDialog.Create(message, () =>
                SessionHub.Instance.Catalog.RemovePreset(kind, command.Name, () =>
                {
                    ClearActionSelection();
                }, UiLayout.Fail), destructive: true));
        }

        static void Edit(LibraryItemInfo item)
        {
            if (Templates.TryGetValue(item, out var template))
                TerminalWindow.OpenOverPane(EditSessionDialog.EditTemplate(template));
            else
                TerminalWindow.OpenOverPane(EditLibraryItemDialog.ForEdit(item));
        }

        static void Menu(LibraryItemInfo item)
        {
            if (Templates.TryGetValue(item, out var template)) TemplateMenu(template);
            else RowMenu(item);
        }

        static void DrawDetails(Rect r, RowDetails details)
        {
            Slab.Hairline(new Rect(r.x, r.y, r.width, 1f), UiTheme.Edge);

            // Two stacked action rows also fit the sidebar's minimum width.
            float actionsH = UiTheme.FieldH * 2f + Pad;
            float y = r.y + Pad;
            float textBottom = r.yMax - actionsH - Pad;
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                foreach (var line in details.Lines)
                {
                    if (y + RowH > textBottom) break;
                    UiText.RowLabel(new Rect(r.x, y, r.width, RowH),
                        details.IsItem ? line : OneLine(line));
                    y += RowH;
                }
                TooltipHandler.TipRegion(new Rect(r.x, r.y, r.width,
                    Mathf.Max(0f, textBottom - r.y)), details.Tooltip);
            }
            if (r.height < actionsH + Pad * 2f) return;
            var primary = new Rect(r.x, r.yMax - actionsH - Pad, r.width, UiTheme.FieldH);
            if (UiButtons.Button(primary, details.PrimaryLabel ?? "Open", UiTheme.Btn.Primary))
                details.Primary?.Invoke();
            float moreW = details.More == null ? 0f : UiTheme.FieldH;
            float secondaryW = Mathf.Max(0f, r.width - moreW - (moreW > 0f ? Pad : 0f));
            var secondary = new Rect(r.x, primary.yMax + Pad, secondaryW, UiTheme.FieldH);
            if (details.Secondary != null)
            {
                bool clicked = details.IsItem
                    ? UiButtons.Button(secondary, details.SecondaryLabel)
                    : UiButtons.Button(secondary, details.SecondaryLabel, UiTheme.Btn.Ghost);
                if (clicked) details.Secondary();
            }
            if (details.More != null && UiButtons.Button(
                    new Rect(r.xMax - moreW, secondary.y, moreW, secondary.height), "…"))
                details.More();
        }

        static float DrawSection(Rect view, float y, Section section)
        {
            float start = y;
            bool folded = Folded.Contains(section.Key);
            var headRect = new Rect(0f, y, view.width, HeadH);
            y += DrawHeading(view, headRect, section.Key, section.Key, section.Rows.Count, folded);
            if (!folded)
                foreach (var rowData in section.Rows)
                {
                    var row = new Rect(0f, y, view.width, RowH);
                    y += DrawRow(view, row, rowData);
                }
            return y - start;
        }

        static float DrawHeading(Rect view, Rect headRect, string key, string label,
            int count, bool folded)
        {
            Lines.Add(new Line { Head = true, Key = key, Rect = Screen(headRect) });

            RowChrome.Hover(headRect, false, true, RowHoverPolicy.OverlayAware);

            Rect arrow;
            using (WidgetState.Save())
            {
                GUI.color = UiTheme.Faint;
                arrow = new Rect(CellX, headRect.y + (HeadH - ArrowW) / 2f,
                    ArrowW, ArrowW);
                GUI.DrawTexture(arrow, folded ? TexButton.Reveal : TexButton.Collapse);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                float lx = arrow.xMax + UiTheme.GapXS;
                string tail = "  " + count;
                var labelRect = new Rect(lx, headRect.y, view.width - lx - CellX, HeadH);
                UiText.RowLabel(labelRect, label + tail);
            }

            Slab.Hairline(new Rect(CellX, headRect.yMax - 1f,
                view.width - CellX * 2f, 1f), UiTheme.Edge);

            TooltipHandler.TipRegion(headRect, "Click to fold.");

            return HeadH;
        }

        static float DrawRow(Rect view, Rect r, Row row)
        {
            var hitRect = Screen(r);
            RowChrome.Hover(r, row.Key == _selection, true, RowHoverPolicy.OverlayAware);
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = UiTheme.Dim;
                GUI.DrawTexture(new Rect(CellX, r.y + (RowH - ArrowW) / 2f, ArrowW, ArrowW), row.Icon);
                GUI.color = UiTheme.Lead;
                UiText.RowLabel(new Rect(CellX + ArrowW + Pad, r.y,
                    Mathf.Max(0f, r.width - CellX * 2f - ArrowW - Pad), RowH),
                    row.Label);
            }
            if (hitRect.width > 0f && hitRect.height > 0f)
            {
                TooltipHandler.TipRegion(r, RowTooltip(row));
                Lines.Add(new Line { Row = row, Rect = hitRect });
            }
            return RowH;
        }

        static string RowTooltip(Row row)
        {
            if (row.Item != null)
            {
                string scope = string.IsNullOrEmpty(row.Item.Project) ? "Global" : row.Item.Project;
                return row.Item.Name + "\n" + scope + "\n" + OneLine(row.Item.Text);
            }
            return row.TooltipJoinsPath
                ? row.Tooltip + "  -  " + row.TooltipTail
                : row.Tooltip;
        }

        // The height the rows want, measured off the same folds the draw reads.
        static float Measure()
        {
            float h = Pad;
            foreach (var section in Sections)
            {
                h += HeadH;
                if (!Folded.Contains(section.Key)) h += section.Rows.Count * RowH;
            }
            return h + Pad;
        }

        static void Empty(Rect body)
        {
            var r = new Rect(body.x + CellX, body.y + Pad, body.width - CellX * 2f, RowH * 3f);
            UiText.PlainStatusLabel(r, !SessionHub.Instance.Online
                ? $"daemon {SessionHub.Instance.Status}"
                : _query.Length > 0 || _kind.Length > 0
                    ? "No matching entries."
                    : ProjectFiltering
                    ? $"No library entries in {ProjectFilterLabel}."
                    : "No library entries yet. Press + at the foot of the panel.",
                UiTheme.Faint, GameFont.Tiny);
        }

        static string KindName(LibraryItemKind kind)
        {
            switch (kind)
            {
                case LibraryItemKind.Shell: return "Shell";
                case LibraryItemKind.Breadcrumb: return "Breadcrumb";
                case LibraryItemKind.FileAction: return "File Action";
                default: return "Prompt";
            }
        }

        // ------------------------------------------------------------------ clicks
        //
        // Called from AgentSidebar's back pass where Menus is in the agents view and
        // Clicks is in the files/git views.

        public static void Clicks()
        {
            if (!ColonistBarStrip.Interactive) return;

            var e = Event.current;
            if (e.rawType != EventType.MouseDown) return;
            if (e.button != 0 && e.button != 1) return;

            // The "+" is the column's, at the foot of the panel below this body, and
            // AgentSidebar answers it before this is ever asked.
            foreach (var line in Lines)
            {
                // Screen() zeroes a line clipped out of the scroll view, and Rect.zero is
                // nowhere the mouse can be.
                if (!ColonistBarStrip.MouseOver(line.Rect)) continue;

                if (line.Head)
                {
                    // Headings only fold; row variants own their actions.
                    if (e.button == 0)
                    {
                        if (!Folded.Remove(line.Key)) Folded.Add(line.Key);
                    }
                    e.Use();
                    return;
                }

                var row = line.Row;
                if (row == null) continue;
                bool select = row.Item != null || e.button == 0 || HasMainMenu(row);
                if (select)
                {
                    _selection = row.Key;
                    _revealSelection = true;
                }
                if (row.Item != null)
                {
                    AgentSidebar.RememberLibrary(row.Item.Name, Templates.ContainsKey(row.Item));
                    if (e.button == 1) Menu(row.Item);
                }
                else if (e.button == 1)
                    OpenMainMenu(row);
                e.Use();
                return;
            }
        }

        static bool HasMainMenu(Row row) =>
            row.MainKind == MainRowKind.Project || row.MainKind == MainRowKind.Worktree;

        static void OpenMainMenu(Row row)
        {
            if (row.MainKind == MainRowKind.Project)
                ProjectMenu((ProjectInfo)row.Model);
            else if (row.MainKind == MainRowKind.Worktree)
                WorktreeMenu((WorktreeEntry)row.Model);
        }

        // ------------------------------------------------------------------ menus

        static void TemplateMenu(AgentTemplateInfo template)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Create agent", () => TerminalWindow.OpenOverPane(
                    EditSessionDialog.FromTemplate(template))),
                new FloatMenuOption("Edit", () =>
                    TerminalWindow.OpenOverPane(EditSessionDialog.EditTemplate(template))),
                new FloatMenuOption("Duplicate", () =>
                    TerminalWindow.OpenOverPane(EditSessionDialog.EditTemplate(template, true))),
                new FloatMenuOption("Delete", () => TerminalWindow.OpenOverPane(
                    ConfirmDialog.Create("Remove template '" + template.Name + "'? Existing agents keep their snapshots.",
                        () => SessionHub.Instance.Catalog.RemoveAgentTemplate(template, template.Name,
                            null, UiLayout.Fail), destructive: true))),
            };
            TerminalWindow.OpenOverPane(new UiMenu(options));
        }

        static void RowMenu(LibraryItemInfo s)
        {
            var opts = new List<FloatMenuOption>();

            // Run is the reason ordinary runnable items exist. Breadcrumbs are definitions only.
            if (s.Kind != LibraryItemKind.Breadcrumb && s.Kind != LibraryItemKind.FileAction)
                opts.Add(new FloatMenuOption("Run", () => Run(s)));

            if (Runnable(s) && s.Link == LibraryItemLink.Ask)
                opts.Add(new UiSubmenu("Choose a project", () => WhereOptions(s)));

            var edit = new FloatMenuOption("Edit", () =>
                TerminalWindow.OpenOverPane(EditLibraryItemDialog.ForEdit(s)));
            opts.Add(edit);

            var duplicate = new FloatMenuOption("Duplicate", () =>
                TerminalWindow.OpenOverPane(EditLibraryItemDialog.Copy(s)));
            opts.Add(duplicate);

            var name = s.Name;
            opts.Add(new FloatMenuOption("Delete", () =>
                TerminalWindow.OpenOverPane(ConfirmDialog.Create(
                    $"Remove library entry '{name}'? Anything it already started keeps running.",
                    () => SessionHub.Instance.Catalog.RemoveLibraryItem(name, UiLayout.Fail),
                    destructive: true))));

            TerminalWindow.OpenOverPane(new UiMenu(opts));
        }

        // ------------------------------------------------------------------ actions

        // Do not reopen AskWhere after resolving a temporary project: `temp` marks the return
        // path where `project == null` is intentional.
        static void Run(LibraryItemInfo s, string project = null, bool temp = false)
        {
            AgentSidebar.RememberLibrary(s?.Name);
            // An entry that never said where goes through a menu first.
            if (s.Link == LibraryItemLink.Ask && project == null && !temp)
            {
                AskWhere(s);
                return;
            }

            bool scratch = temp || s.Link == LibraryItemLink.Temp;
            SessionHub.Instance.SessionStore.RunLibraryItem(s.Name,
                session => TerminalWindow.Open(session),
                UiLayout.Fail,
                // A project named outright wins. A temporary run has none, whichever of
                // the two said so. Otherwise the entry's own.
                project ?? (scratch ? null : s.Project),
                scratch, Patch_LoadingTips.RandomTips(Patch_LoadingTips.TipBatch));
        }

        // Every project, plus a temporary one - last, being the answer for the run that
        // belongs nowhere in particular. Hung off the row's own menu where there is one, and
        // opened as a menu of its own where the run was asked for from somewhere else.
        static List<FloatMenuOption> WhereOptions(LibraryItemInfo s)
        {
            var options = SessionHub.Instance.Projects
                .Select(p => new FloatMenuOption($"{p.Name}  -  {p.Dir}",
                    () => Run(s, p.Name)))
                .ToList();

            options.Add(new FloatMenuOption(
                $"A temporary project under {ProjectInfo.TempRoot}",
                () => Run(s, null, true)));

            return options;
        }

        static void AskWhere(LibraryItemInfo s) =>
            TerminalWindow.OpenOverPane(new UiMenu(WhereOptions(s)));

        // Where an errand runs, in the few words a row and a tooltip have.
        static string Where(LibraryItemInfo s)
        {
            switch (s.Link)
            {
                case LibraryItemLink.Temp: return "a temporary project";
                case LibraryItemLink.Ask: return "a project you choose at run time";
                default:
                    return string.IsNullOrEmpty(s.Project)
                        ? "no project"
                        : s.Project;
            }
        }

        // The first line of a prompt, which is all a row has space for.
        static string OneLine(string text)
        {
            text = (text ?? "").Replace("\r", "");
            int nl = text.IndexOf('\n');
            return nl < 0 ? text : text.Substring(0, nl) + " ...";
        }

        public static bool FocusLocation(string name, bool template = false)
        {
            bool exists = template
                ? SessionHub.Instance.Templates.Any(candidate => candidate.Name == name)
                : SessionHub.Instance.Library.Any(candidate => candidate.Name == name && !candidate.Builtin);
            if (!exists) return false;
            _selection = (template ? "template:" : "item:") + name;
            _revealSelection = true;
            _query = "";
            _kind = "";
            Folded.Clear();
            return true;
        }

        static void ProjectFilterChanged()
        {
            _worktreeRequestKey = null;
            ClearActionSelection();
            _scroll.JumpTo(Vector2.zero);
        }

        static void ClearActionSelection()
        {
            if (_selection != null && _selection.StartsWith("action:", StringComparison.Ordinal))
                _selection = null;
        }
    }
}
