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
    public static partial class LibraryView
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
            _catalogError = null;
            void failed(string error) { _catalogError = error; fail?.Invoke(error); }
            SessionHub.Instance.Catalog.RefreshProjects(failed);
            SessionHub.Instance.Catalog.RefreshLibrary(failed);
            SessionHub.Instance.Catalog.RefreshTemplates(failed);
            SessionHub.Instance.Catalog.LoadPresets(fail: failed);
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
        static readonly Dictionary<string, string> WorktreeErrors = new Dictionary<string, string>();
        static string _catalogError;
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

        static void SetKind(string kind)
        {
            _kind = kind;
            _scroll.JumpTo(Vector2.zero);
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
