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
        static readonly Dictionary<string, List<LibraryItemInfo>> Groups =
            new Dictionary<string, List<LibraryItemInfo>>();
        static readonly List<string> Order = new List<string>();
        static readonly string[] Kinds =
        {
            "Agent templates", "Prompts", "Shell commands", "Breadcrumbs", "File actions",
            "Projects", "Worktrees", "Sandbox presets", "App presets"
        };
        static readonly string[] MainCategoryOrder =
            { "Projects", "Worktrees", "Sandbox presets", "App presets" };
        static readonly Dictionary<string, List<MainCategoryAction>> MainCategories =
            new Dictionary<string, List<MainCategoryAction>>();
        static readonly List<string> VisibleMainCategories = new List<string>();
        static readonly Dictionary<string, List<WorktreeEntry>> WorktreesByProject =
            new Dictionary<string, List<WorktreeEntry>>();
        static string _worktreeRequestKey;
        static int _worktreeGeneration;
        static string _query = "";
        static string _kind = "";
        static readonly HashSet<string> ProjectFilter = new HashSet<string>();
        static string _selection;
        static string _mainSelection;
        static bool _revealSelection;
        static Rect _list;
        static FieldLifetime _fieldLifetime = new FieldLifetime();

        static string GroupKey(LibraryItemInfo item) => Templates.ContainsKey(item)
            ? Kinds[0] : item.Kind == LibraryItemKind.Shell ? Kinds[2]
            : item.Kind == LibraryItemKind.Breadcrumb ? Kinds[3]
            : item.Kind == LibraryItemKind.FileAction ? Kinds[4] : Kinds[1];

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

        public static bool AllFolded => Order.Count + VisibleMainCategories.Count > 0 &&
            Order.All(Folded.Contains) && VisibleMainCategories.All(Folded.Contains);

        public static void SetAllFolded(bool folded)
        {
            Folded.Clear();
            if (folded)
            {
                foreach (var key in Order) Folded.Add(key);
                foreach (var key in MainCategoryOrder) Folded.Add(key);
            }
        }

        // Resolve selection by catalog identity after each refresh.
        static LibraryItemInfo _selected;

        // The drawn lines, rebuilt each frame so clicks and drawing agree. Rects are in
        // screen space - see Screen - because Clicks runs outside the scroll view.
        struct Line
        {
            public LibraryItemInfo Item;
            public MainCategoryAction MainAction;
            public bool Head;    // true on a heading, false on a row
            public string Key;   // kind heading
            public Rect Rect;
        }

        sealed class MainCategoryAction
        {
            public string Key;
            public string Label;
            public string Tooltip;
            public Texture2D Icon;
            public List<string> Details = new List<string>();
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
                BuildMainCategories();
                _selected = _items.FirstOrDefault(item => Identity(item) == _selection);
                var selectedMain = FindMainAction(_mainSelection);
                if (selectedMain == null) _mainSelection = null;
                float top = body.y + 2f * (UiTheme.FieldH + Pad) + Pad;
                float available = Mathf.Max(0f, body.yMax - top);
                float detailHeight = _selected == null && selectedMain == null ? 0f :
                    Mathf.Min(RowH * 5f + UiTheme.FieldH * 2f + Pad * 4f,
                        Mathf.Max(0f, available - RowH * 2f));
                _list = new Rect(body.x, top, body.width, Mathf.Max(0f, available - detailHeight));
                if (_selected != null && detailHeight > 0f)
                    DrawDetails(new Rect(body.x + CellX, _list.yMax,
                        body.width - CellX * 2f, detailHeight), _selected);
                else if (selectedMain != null && detailHeight > 0f)
                    DrawMainDetails(new Rect(body.x + CellX, _list.yMax,
                        body.width - CellX * 2f, detailHeight), selectedMain);
                Group();
                if (_items.Count == 0 && VisibleMainCategories.Count == 0)
                {
                    _contentHeight = 0f;
                    Empty(_list);
                    return;
                }
                var list = _list;
                if (_revealSelection)
                {
                    float y = Pad;
                    foreach (var key in Order)
                    {
                        y += HeadH;
                        if (Folded.Contains(key)) continue;
                        foreach (var item in Groups[key])
                        {
                            if (Identity(item) == _selection)
                                _scroll.Reveal(y, RowH, list.height);
                            y += RowH;
                        }
                    }
                    foreach (var key in VisibleMainCategories)
                    {
                        y += HeadH;
                        if (Folded.Contains(key)) continue;
                        foreach (var action in MainCategories[key])
                        {
                            if (action.Key == _mainSelection)
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

                    foreach (var key in Order)
                        y += DrawGroup(view, y, key, Groups[key]);
                    foreach (var key in VisibleMainCategories)
                        y += DrawMainCategory(view, y, key, MainCategories[key]);
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
                UiText.StatusLabel(field.ContractedBy(Pad, 0f), "Search library…", UiTheme.Faint, GameFont.Tiny);
            TooltipHandler.TipRegion(field, "Search library names and content");
            var filter = new Rect(field.x, field.yMax + Pad, field.width, UiTheme.FieldH);
            if (UiButtons.Button(filter, _kind.Length == 0 ? "All types ▾" : _kind + " ▾"))
            {
                var options = new List<FloatMenuOption>
                {
                    new FloatMenuOption("All types", () => SetKind(""))
                };
                foreach (var kind in Kinds)
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

        static void BuildMainCategories()
        {
            MainCategories.Clear();
            VisibleMainCategories.Clear();
            foreach (var key in MainCategoryOrder)
                MainCategories[key] = new List<MainCategoryAction>();

            string query = _query.Trim();
            bool Matches(string value) => query.Length == 0 ||
                (value ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

            var projects = SessionHub.Instance.Projects
                .Where(project => PassesProject(project.Name))
                .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (MainCategoryEnabled("Projects"))
            {
                foreach (var project in projects)
                {
                    var captured = project;
                    AddMainAction(MainCategories["Projects"], "project:" + captured.Name,
                        captured.Name, captured.Name + "  -  " + captured.Dir, Icons.Files,
                        new[] { captured.Name, "Project", captured.Dir, ProjectsView.Summary(captured) },
                        "Edit", () => TerminalWindow.OpenOverPane(new EditProjectDialog(captured)),
                        "Worktrees", () => TerminalWindow.OpenOverPane(EditProjectDialog.ForWorktrees(captured)),
                        () => ProjectMenu(captured),
                        Matches("Projects") || Matches(captured.Name) || Matches(captured.Dir));
                }
            }

            if (MainCategoryEnabled("Worktrees"))
            {
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
                        AddMainAction(MainCategories["Worktrees"],
                            "worktree:" + captured.Project + ":" + tree.Id, label,
                            label + "  -  " + tree.Path, Icons.Git,
                            WorktreeDetails(captured), "Terminal",
                            () => SessionHub.Instance.SessionStore.RunHostShell(captured.Project,
                                name => TerminalWindow.Open(name), UiLayout.Fail, tree.Id),
                            "Project", () => OpenProjectWorktrees(captured.Project),
                            () => WorktreeMenu(captured),
                            Matches("Worktrees") || Matches(captured.Project) ||
                            Matches(treeName) || Matches(tree.Path) || Matches(tree.Branch));
                    }
                }
            }

            if (MainCategoryEnabled("Sandbox presets"))
            {
                foreach (var preset in SessionHub.Instance.Presets
                    .Where(preset => preset.Source != "system")
                    .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var captured = preset;
                    string label = PresetLabel(captured.Name, captured.Source);
                    AddMainAction(MainCategories["Sandbox presets"],
                        "sandbox:" + captured.Name, label, captured.Description, Icons.Shield,
                        SandboxDetails(captured), "Edit",
                        () => ModOptions.OpenSandboxPreset(captured.Name),
                        captured.Source == "override" ? "Reset" : "Remove",
                        () => RemovePreset("sandbox_presets", captured), null,
                        Matches("Sandbox presets") || Matches(captured.Name) ||
                        Matches(captured.Description));
                }
            }

            if (MainCategoryEnabled("App presets"))
            {
                foreach (var command in SessionHub.Instance.Commands
                    .Where(command => command.Source != "system")
                    .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var captured = command;
                    string label = PresetLabel(captured.Name, captured.Source);
                    AddMainAction(MainCategories["App presets"],
                        "app:" + captured.Name, label, captured.Description, Icons.Terminal,
                        AppPresetDetails(captured), "Edit",
                        () => ModOptions.OpenAppPreset(captured.Name),
                        captured.Source == "override" ? "Reset" : "Remove",
                        () => RemovePreset("app_presets", captured), null,
                        Matches("App presets") || Matches(captured.Name) ||
                        Matches(captured.Description) || Matches(captured.Cmd));
                }
            }

            foreach (var key in MainCategoryOrder)
                if (MainCategories[key].Count > 0) VisibleMainCategories.Add(key);
        }

        static bool MainCategoryEnabled(string key) => _kind.Length == 0 || _kind == key;

        static void AddMainAction(List<MainCategoryAction> items, string key, string label,
            string tooltip, Texture2D icon, IEnumerable<string> details, string primaryLabel,
            Action primary, string secondaryLabel, Action secondary, Action more, bool include)
        {
            if (!include) return;
            items.Add(new MainCategoryAction
            {
                Key = key,
                Label = label,
                Tooltip = tooltip,
                Icon = icon,
                Details = details.Where(detail => !string.IsNullOrEmpty(detail)).ToList(),
                PrimaryLabel = primaryLabel,
                Primary = primary,
                SecondaryLabel = secondaryLabel,
                Secondary = secondary,
                More = more,
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

        static MainCategoryAction FindMainAction(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var category in VisibleMainCategories)
            {
                var action = MainCategories[category].FirstOrDefault(item => item.Key == key);
                if (action != null) return action;
            }
            return null;
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
                new FloatMenuOption("Edit...", () =>
                    TerminalWindow.OpenOverPane(new EditProjectDialog(project))),
                new FloatMenuOption("Duplicate...", () =>
                    TerminalWindow.OpenOverPane(EditProjectDialog.Copy(project))),
                new FloatMenuOption("Worktrees...", () => OpenProjectWorktrees(project.Name)),
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
                "Remove worktree '" + entry.Tree.Name + "'? Managed files are removed; " +
                "unmanaged files are only unregistered.",
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
                    _mainSelection = null;
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
                    _mainSelection = null;
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

        static void DrawDetails(Rect r, LibraryItemInfo item)
        {
            Slab.Hairline(new Rect(r.x, r.y, r.width, 1f), UiTheme.Edge);
            bool template = Templates.TryGetValue(item, out var definition);
            bool runnable = Runnable(item);
            // Two stacked action rows also fit the sidebar's minimum width.
            float actionsH = UiTheme.FieldH * 2f + Pad;
            float y = r.y + Pad;
            float textBottom = r.yMax - actionsH - Pad;
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                string scope = string.IsNullOrEmpty(item.Project) ? "Global" : item.Project;
                var lines = new List<string>
                {
                    item.Name,
                    (template ? "Agent template" : KindName(item.Kind)) + " · " + scope
                };
                if (runnable)
                {
                    lines.Add("In: " + Where(item));
                    lines.Add("Runs with: " + (item.Host ? "Host" :
                        string.IsNullOrEmpty(item.AgentTemplate) ? "Not configured" : item.AgentTemplate));
                }
                else if (item.Kind == LibraryItemKind.FileAction && !template)
                    lines.Add("Host · " + FileActionModeText.Name(item.Mode));
                lines.Add(OneLine(item.Text));
                foreach (var line in lines)
                {
                    if (y + RowH > textBottom) break;
                    UiText.RowLabel(new Rect(r.x, y, r.width, RowH), line);
                    y += RowH;
                }
                TooltipHandler.TipRegion(new Rect(r.x, r.y, r.width,
                    Mathf.Max(0f, textBottom - r.y)), string.Join("\n", lines) + "\n\n" + item.Text);
            }
            if (r.height < actionsH + Pad * 2f) return;
            var primary = new Rect(r.x, r.yMax - actionsH - Pad, r.width, UiTheme.FieldH);
            string label = template ? "Create agent…" : runnable
                ? (item.Link == LibraryItemLink.Ask ? "Run in…" : "Run") : "Edit";
            if (UiButtons.Button(primary, label, UiTheme.Btn.Primary))
            {
                if (template) TerminalWindow.OpenOverPane(EditSessionDialog.FromTemplate(definition));
                else if (runnable) Run(item);
                else Edit(item);
            }
            float moreW = UiTheme.FieldH;
            var secondary = new Rect(r.x, primary.yMax + Pad,
                Mathf.Max(0f, r.width - moreW - Pad), UiTheme.FieldH);
            if (template || runnable)
            {
                if (UiButtons.Button(secondary, "Edit")) Edit(item);
            }
            else if (item.Kind == LibraryItemKind.Breadcrumb)
            {
                if (UiButtons.Button(secondary, "Copy text")) GUIUtility.systemCopyBuffer = item.Text ?? "";
            }
            if (UiButtons.Button(new Rect(r.xMax - moreW, secondary.y, moreW, secondary.height), "…"))
                Menu(item);
        }

        static void DrawMainDetails(Rect r, MainCategoryAction action)
        {
            Slab.Hairline(new Rect(r.x, r.y, r.width, 1f), UiTheme.Edge);
            float actionsH = UiTheme.FieldH * 2f + Pad;
            float y = r.y + Pad;
            float textBottom = r.yMax - actionsH - Pad;
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                foreach (var line in action.Details)
                {
                    if (y + RowH > textBottom) break;
                    UiText.RowLabel(new Rect(r.x, y, r.width, RowH), OneLine(line));
                    y += RowH;
                }
                TooltipHandler.TipRegion(new Rect(r.x, r.y, r.width,
                    Mathf.Max(0f, textBottom - r.y)), string.Join("\n", action.Details.ToArray()));
            }
            if (r.height < actionsH + Pad * 2f) return;

            float moreW = action.More == null ? 0f : UiTheme.FieldH;
            float secondaryW = Mathf.Max(0f, r.width - moreW - (moreW > 0f ? Pad : 0f));
            var primary = new Rect(r.x, r.yMax - actionsH - Pad, r.width, UiTheme.FieldH);
            if (UiButtons.Button(primary, action.PrimaryLabel ?? "Open", UiTheme.Btn.Primary))
                action.Primary?.Invoke();

            var secondary = new Rect(r.x, primary.yMax + Pad, secondaryW, UiTheme.FieldH);
            if (action.Secondary != null && UiButtons.Button(secondary,
                    action.SecondaryLabel ?? "More", UiTheme.Btn.Ghost))
                action.Secondary();
            if (action.More != null && UiButtons.Button(
                    new Rect(r.xMax - moreW, secondary.y, moreW, secondary.height), "…"))
                action.More();
        }

        static float DrawGroup(Rect view, float y, string key, List<LibraryItemInfo> bucket)
        {
            float start = y;
            bool folded = Folded.Contains(key);
            string label = key;
            var headRect = new Rect(0f, y, view.width, HeadH);
            y += DrawHeading(view, headRect, key, label, bucket.Count, folded);
            if (!folded)
                foreach (var item in bucket)
                {
                    var row = new Rect(0f, y, view.width, RowH);
                    y += DrawLibraryRow(view, row, item);
                }
            return y - start;
        }

        static float DrawMainCategory(Rect view, float y, string key,
            List<MainCategoryAction> actions)
        {
            float start = y;
            bool folded = Folded.Contains(key);
            var headRect = new Rect(0f, y, view.width, HeadH);
            y += DrawHeading(view, headRect, key, key, actions.Count, folded);
            if (!folded)
                foreach (var action in actions)
                {
                    var row = new Rect(0f, y, view.width, RowH);
                    y += DrawMainActionRow(view, row, action);
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

        static float DrawLibraryRow(Rect view, Rect r, LibraryItemInfo item)
        {
            RowChrome.Hover(r, Identity(item) == _selection, true, RowHoverPolicy.OverlayAware);
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = UiTheme.Dim;
                var icon = Templates.ContainsKey(item) ? Icons.Agents :
                    item.Kind == LibraryItemKind.Shell ? Icons.Terminal :
                    item.Kind == LibraryItemKind.FileAction ? Icons.Files :
                    item.Kind == LibraryItemKind.Breadcrumb ? Icons.Keyboard : Icons.Library;
                GUI.DrawTexture(new Rect(CellX, r.y + (RowH - ArrowW) / 2f, ArrowW, ArrowW), icon);
                GUI.color = UiTheme.Lead;
                UiText.RowLabel(new Rect(CellX + ArrowW + Pad, r.y,
                    Mathf.Max(0f, r.width - CellX * 2f - ArrowW - Pad), RowH), item.Name);
            }
            TooltipHandler.TipRegion(r, item.Name + "\n" +
                (string.IsNullOrEmpty(item.Project) ? "Global" : item.Project) +
                "\n" + OneLine(item.Text));

            Lines.Add(new Line { Item = item, Rect = Screen(r) });
            return RowH;
        }

        static float DrawMainActionRow(Rect view, Rect r, MainCategoryAction action)
        {
            RowChrome.Hover(r, action.Key == _mainSelection, true, RowHoverPolicy.OverlayAware);
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = action.Primary == null ? UiTheme.Faint : UiTheme.Dim;
                GUI.DrawTexture(new Rect(CellX, r.y + (RowH - ArrowW) / 2f, ArrowW, ArrowW),
                    action.Icon);
                GUI.color = action.Primary == null ? UiTheme.Faint : UiTheme.Lead;
                UiText.RowLabel(new Rect(CellX + ArrowW + Pad, r.y,
                    Mathf.Max(0f, r.width - CellX * 2f - ArrowW - Pad), RowH), action.Label);
            }
            TooltipHandler.TipRegion(r, action.Tooltip);
            Lines.Add(new Line { MainAction = action, Rect = Screen(r) });
            return RowH;
        }

        // The height the rows want, measured off the same folds the draw reads.
        static float Measure()
        {
            float h = Pad;
            foreach (var key in Order)
            {
                h += HeadH;
                if (!Folded.Contains(key)) h += Groups[key].Count * RowH;
            }
            foreach (var key in VisibleMainCategories)
            {
                h += HeadH;
                if (!Folded.Contains(key)) h += MainCategories[key].Count * RowH;
            }
            return h + Pad;
        }

        static void Empty(Rect body)
        {
            var r = new Rect(body.x + CellX, body.y + Pad, body.width - CellX * 2f, RowH * 3f);
            UiText.StatusLabel(r, !SessionHub.Instance.Online
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

        // Type order is stable across filtering and catalog refreshes.
        static void Group()
        {
            foreach (var list in Groups.Values) list.Clear();
            Order.Clear();

            foreach (var item in _items)
            {
                string key = GroupKey(item);
                if (!Groups.TryGetValue(key, out var list))
                    Groups[key] = list = new List<LibraryItemInfo>();
                list.Add(item);
            }

            foreach (var kv in Groups)
                if (kv.Value.Count > 0) Order.Add(kv.Key);

            Order.Sort((a, b) => Array.IndexOf(Kinds, a).CompareTo(Array.IndexOf(Kinds, b)));

            foreach (var list in Groups.Values) list.Sort((a, b) =>
                string.CompareOrdinal(a?.Name ?? "", b?.Name ?? ""));
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
                    // Headings only fold. Item actions belong to their own rows.
                    if (e.button == 0)
                    {
                        if (!Folded.Remove(line.Key)) Folded.Add(line.Key);
                    }
                    e.Use();
                    return;
                }

                if (line.Item == null)
                {
                    if (e.button == 0 && line.MainAction != null)
                    {
                        _mainSelection = line.MainAction.Key;
                        _selection = null;
                        _selected = null;
                        _revealSelection = true;
                    }
                    if (e.button == 1 && line.MainAction?.More != null)
                    {
                        _mainSelection = line.MainAction.Key;
                        _selection = null;
                        _selected = null;
                        _revealSelection = true;
                        line.MainAction.More();
                    }
                    e.Use();
                    return;
                }

                _selected = line.Item;
                _selection = Identity(line.Item);
                _mainSelection = null;
                _revealSelection = true;
                AgentSidebar.RememberLibrary(line.Item.Name, Templates.ContainsKey(line.Item));
                if (e.button == 1) Menu(line.Item);
                e.Use();
                return;
            }
        }

        // ------------------------------------------------------------------ menus

        static void TemplateMenu(AgentTemplateInfo template)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Create agent...", () => TerminalWindow.OpenOverPane(
                    EditSessionDialog.FromTemplate(template))),
                new FloatMenuOption("Edit...", () =>
                    TerminalWindow.OpenOverPane(EditSessionDialog.EditTemplate(template))),
                new FloatMenuOption("Duplicate...", () =>
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
                opts.Add(new UiSubmenu("Run in", () => WhereOptions(s)));

            var edit = new FloatMenuOption("Edit...", () =>
                TerminalWindow.OpenOverPane(EditLibraryItemDialog.ForEdit(s)));
            opts.Add(edit);

            var duplicate = new FloatMenuOption("Duplicate...", () =>
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
                case LibraryItemLink.Ask: return "run in...";
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
            _mainSelection = null;
            _revealSelection = true;
            _query = "";
            _kind = "";
            Folded.Clear();
            return true;
        }

        static void ProjectFilterChanged()
        {
            _worktreeRequestKey = null;
            _mainSelection = null;
            _scroll.JumpTo(Vector2.zero);
        }
    }
}
