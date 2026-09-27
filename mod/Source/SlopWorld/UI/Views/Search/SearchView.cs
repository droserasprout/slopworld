using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Workspace search is a sidebar view, not a terminal command.
    // The daemon runs ripgrep where it can access the projects.
    // The client keeps only the structured results that it needs to group matches and open the pager.
    public static partial class SearchView
    {
        static float RowH => UiTheme.TinyRowH;
        static float Pad => UiTheme.GapS;
        static float CellX => UiTheme.GapS;
        static float ToolsH => UiTheme.FieldH + UiTheme.GapS + UiTheme.RowH;

        sealed class Match
        {
            public string Project;
            public string Root;
            public string Path;
            public int Line;
            public int Column;
            public string Text;
        }

        sealed class Group
        {
            public string Project;
            public string Worktree;
            public string Error;
            public bool Truncated;
            public readonly List<Match> Matches = new List<Match>();
        }

        struct Hit
        {
            public Rect Rect;
            public Match Match;
        }

        enum RowKind
        {
            ProjectHeading,
            Heading,
            File,
            Match,
            Note,
        }

        struct LayoutRow
        {
            public RowKind Kind;
            public Group Group;
            public Match Match;
            public string Text;
            public Color Color;
        }

        static readonly List<Group> Groups = new List<Group>();
        static readonly List<Hit> Hits = new List<Hit>();
        static readonly List<LayoutRow> Layout = new List<LayoutRow>();
        static readonly SmoothScroll Scroll = new SmoothScroll();
        static readonly Pager Viewer = new Pager();
        internal static Pager ActivePager => Viewer;
        static FieldLifetime _fieldLifetime = new FieldLifetime();

        static SearchSubmission _submitted;
        static readonly BoundedWork Requests = new BoundedWork(4);
        static SearchView() { SidebarScopes.Changed += FilterChanged; }
        public static void FilterChanged() { if (_submitted != null) RunSearch(); }
        static string _query = "";
        static bool _regex;
        static bool _case;
        static bool _word;
        static bool _includeIgnored;
        static bool _loading;
        static readonly OperationGate Operations = new OperationGate();
        static int _pending;
        static Match _selected;
        static Match _showing;
        static bool _focus;
        static bool _layoutDirty = true;
        static float _contentHeight;
        static int _layoutRevision = int.MinValue;

        public static void Entered()
        {
            if (Viewer.Session == null || !Viewer.Alive)
                foreach (var info in SessionHub.Instance.Sessions)
                    if (info != null && info.Alive && info.Intent == "search")
                        Viewer.AttachRestored(info);
            ResetFieldLifetime();
            _focus = true;
        }

        public static void Draw(Rect body)
        {
            SidebarScopes.Update();
            using (FieldLifetimeScope.Push(_fieldLifetime))
            {
                var tools = new Rect(body.x + CellX, body.y + Pad,
                    body.width - CellX * 2f, ToolsH);
                var results = new Rect(body.x, tools.yMax + Pad,
                    body.width, Mathf.Max(0f, body.yMax - tools.yMax - Pad));
                if (Scroll.HandleWheel(results, _layoutRevision == int.MinValue ? -1f : _contentHeight)) return;
                DrawTools(tools);
                DrawResults(results);
            }
        }

        static void DrawTools(Rect r)
        {
            float buttonW = UiTheme.FieldH;
            var field = new Rect(r.x, r.y, r.width - buttonW - UiTheme.GapS,
                UiTheme.FieldH);
            var e = Event.current;

            string was = _query;
            _query = UiText.Field(field, "search.query", _query);
            if (_focus)
            {
                GUI.FocusControl("search.query");
                _focus = false;
            }

            // The tab focuses this field when it opens. A sidebar entry must not keep game shortcuts
            // captive after a search, Escape, or a click elsewhere.
            if (e.type == EventType.MouseDown && !field.Contains(e.mousePosition))
                ReleaseFocus();
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && Focused)
            {
                ReleaseFocus();
                e.Use();
                return;
            }

            var go = new Rect(field.xMax + UiTheme.GapS, field.y, buttonW, field.height);
            bool clicked = UiButtons.Button(go, "›", UiTheme.Btn.Primary);
            bool entered = e.type == EventType.KeyDown &&
                (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && Focused;
            if (clicked || entered)
            {
                if (entered) e.Use();
                ReleaseFocus();
                Search();
            }

            float y = field.yMax + UiTheme.GapS;
            float w = (r.width - UiTheme.GapS * 3f) / 4f;
            _case = UiControls.Checkbox(new Rect(r.x, y, w, UiTheme.RowH),
                "Case", _case, "Case-sensitive search");
            _word = UiControls.Checkbox(new Rect(r.x + w + UiTheme.GapS, y,
                w, UiTheme.RowH), "Word", _word, "Match whole words");
            _regex = UiControls.Checkbox(new Rect(r.x + (w + UiTheme.GapS) * 2f, y,
                w, UiTheme.RowH), "Regex", _regex, "Interpret the query as a regex");
            _includeIgnored = UiControls.Checkbox(new Rect(r.x + (w + UiTheme.GapS) * 3f, y,
                w, UiTheme.RowH), "Include ignored", _includeIgnored,
                "Include files ignored by Git");

            // Changing text does not search on every frame. Enter is the deliberate boundary
            // between editing a potentially expensive expression and running it.
            if (was != _query) _selected = null;
        }

        static bool Focused => GUI.GetNameOfFocusedControl() == "search.query";

        // Public because changing sidebar tabs removes the entry that owns this focus. Without
        // clearing it first, Unity keeps reporting a text field after that entry is gone.
        public static void ReleaseFocus()
        {
            ResetFieldLifetime();
            _focus = false;
            if (Focused) GUI.FocusControl(null);
        }

        public static void Closed()
        {
            ReleaseFocus();
        }

        static void ResetFieldLifetime()
        {
            _fieldLifetime.Cancel();
            _fieldLifetime = new FieldLifetime();
        }

        public static void Search()
        {
            _submitted = new SearchSubmission(_query, _regex, _case, _word, _includeIgnored);
            RunSearch();
        }

        static void RunSearch()
        {
            var submitted = _submitted;
            string query = submitted.Query;
            if (query.Length == 0) { ResetToEmpty(); return; }

            var projects = SidebarScopes.EnabledScopes();
            ReleaseViewer();
            Groups.Clear();
            DirtyLayout();
            _selected = null;
            _loading = true;
            Scroll.JumpTo(Vector2.zero);
            int generation = Operations.Begin();

            _pending = projects.Count;
            if (_pending == 0) { _loading = false; return; }

            foreach (var p in projects)
            {
                var group = new Group { Project = p.Project, Worktree = p.Label };
                Groups.Add(group);
                string url = WireProtocol.Routes.Search + "?path=" + Uri.EscapeDataString(p.Path) +
                    "&q=" + Uri.EscapeDataString(query) +
                    "&regex=" + (submitted.Regex ? "1" : "0") +
                    "&case=" + (submitted.Case ? "1" : "0") +
                    "&word=" + (submitted.Word ? "1" : "0") +
                    "&gitignore=" + (submitted.IncludeIgnored ? "0" : "1") +
                    "&hidden=" + (Settings.SidebarShowHidden ? "1" : "0");
                Requests.Add(() => Operations.IsCurrent(generation) && SidebarScopes.Enabled(p.Key), done =>
                    DaemonClient.Get<Wire.SearchResult>(url,
                        j => { try { OnResults(group, p, generation, j); } finally { done(); } },
                        msg => { try { OnError(group, generation, msg); } finally { done(); } }));
            }
        }

        static void ResetToEmpty()
        {
            // Invalidate replies for the previous query before clearing its rows. Otherwise a
            // slow request can repopulate the result list after the user erased the field.
            Operations.Invalidate();
            _pending = 0;
            _loading = false;
            Groups.Clear();
            DirtyLayout();
            Scroll.JumpTo(Vector2.zero);
            ReleaseViewer();
        }

        static void OnResults(Group group, BrowseScope project, int generation, Wire.SearchResult j)
        {
            if (!Operations.IsCurrent(generation)) return;
            foreach (var row in j.Matches)
                group.Matches.Add(new Match
                {
                    Project = project.Key,
                    Root = project.Path,
                    Path = row.Path,
                    Line = (int)row.Line,
                    Column = (int)row.Column,
                    Text = row.Text,
                });
            group.Matches.Sort((a, b) =>
            {
                int path = string.CompareOrdinal(a.Path, b.Path);
                return path != 0 ? path : a.Line.CompareTo(b.Line);
            });
            group.Truncated = j.Truncated;
            DirtyLayout();
            Done(generation);
        }

        static void OnError(Group group, int generation, string msg)
        {
            if (!Operations.IsCurrent(generation)) return;
            group.Error = msg;
            DirtyLayout();
            Done(generation);
        }

        static void DirtyLayout() => _layoutDirty = true;

        static void Done(int generation)
        {
            if (!Operations.IsCurrent(generation)) return;
            _pending--;
            if (_pending <= 0) _loading = false;
            DirtyLayout();
        }

        static void DrawResults(Rect body)
        {
            EnsureLayout();
            Hits.Clear();
            float height = _contentHeight;
            var geometry = UiScrollBody.Measure(body, height,
                UiScrollbarReservation.WhenNeeded);
            var view = geometry.View;
            using (WidgetState.Save())
            using (Scroll.Scope(body, view))
            {
                if (SmoothScroll.WheelOnly) return;
                float visibleTop = Scroll.Position.y;
                float visibleBottom = visibleTop + body.height;
                int first = Mathf.Max(0,
                    Mathf.FloorToInt((visibleTop - Pad) / RowH) - 1);
                int last = Mathf.Min(Layout.Count,
                    Mathf.CeilToInt((visibleBottom - Pad) / RowH) + 1);
                for (int i = first; i < last; i++)
                {
                    float y = Pad + i * RowH;
                    DrawRow(view.width, ref y, Layout[i]);
                }
            }
        }

        static void DrawRow(float width, ref float y, LayoutRow row)
        {
            switch (row.Kind)
            {
                case RowKind.ProjectHeading:
                    Note(width, ref y, row.Text, UiTheme.Lead);
                    break;
                case RowKind.Heading:
                    Heading(width, ref y, row.Group);
                    break;
                case RowKind.File:
                    FileHeading(width, ref y, row.Text);
                    break;
                case RowKind.Match:
                    Result(width, ref y, row.Match);
                    break;
                case RowKind.Note:
                    Note(width, ref y, row.Text, row.Color);
                    break;
            }
        }

        static void Heading(float width, ref float y, Group group)
        {
            var r = new Rect(0f, y, width, RowH);
            GUI.color = UiTheme.Lead;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            UiText.RowLabel(new Rect(CellX, y, width - CellX * 2f, RowH),
                $"    {group.Worktree}  {group.Matches.Count}");
            GUI.color = Color.white;
            y += RowH;
        }

        static void FileHeading(float width, ref float y, string path)
        {
            GUI.color = UiTheme.Name;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            UiText.RowLabel(new Rect(CellX + UiTheme.GapS, y,
                width - CellX * 2f - UiTheme.GapS, RowH), path);
            GUI.color = Color.white;
            y += RowH;
        }

        static void Result(float width, ref float y, Match match)
        {
            var r = new Rect(0f, y, width, RowH);
            bool over = RowChrome.Hover(r, ReferenceEquals(match, _selected), true,
                RowHoverPolicy.OverlayAware);

            string prefix = match.Line + ":" + match.Column;
            float prefixW = Mathf.Min(width * 0.55f, UiTheme.Wide(prefix) + 8f);
            float rx = width - Pad;
            var acts = over ? RowAct.View | RowAct.Edit : RowAct.None;
            if (acts != RowAct.None) rx = RowActions.Draw(r, rx, acts) - UiTheme.GapXS;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = UiTheme.Name;
            UiText.RowLabel(new Rect(CellX + UiTheme.GapM, y, prefixW, RowH), prefix);
            GUI.color = over ? UiTheme.Lead : UiTheme.Dim;
            UiText.RowLabel(new Rect(CellX + UiTheme.GapM + prefixW, y,
                Mathf.Max(0f, rx - CellX - UiTheme.GapM - prefixW), RowH), match.Text.Trim());
            GUI.color = Color.white;
            if (RowActions.Hit(r, width - Pad, acts) == RowAct.None)
                TooltipHandler.TipRegion(r,
                    $"{match.Path}:{match.Line}:{match.Column}\n{match.Text.Trim()}");
            Hits.Add(new Hit { Rect = r, Match = match });
            y += RowH;
        }

        static void Note(float width, ref float y, string text, Color color)
        {
            GUI.color = color;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            UiText.RowLabel(new Rect(CellX, y, width - CellX * 2f, RowH), text);
            GUI.color = Color.white;
            y += RowH;
        }

        public static void Clicks()
        {
            if (!ColonistBarStrip.Interactive) return;
            var e = Event.current;
            if (e.rawType != EventType.MouseDown || (e.button != 0 && e.button != 1)) return;
            // Partially visible rows retain their full geometry for action placement.
            // Only the portion inside the scroll viewport may receive clicks.
            if (!ResultsBody().Contains(e.mousePosition)) return;
            foreach (var hit in Hits)
            {
                var scr = Screen(hit.Rect);
                if (!ColonistBarStrip.MouseOver(scr)) continue;

                if (e.button == 1)
                {
                    Menu(hit.Match);
                    e.Use();
                    return;
                }

                var act = RowActions.Hit(scr, scr.xMax - Pad, RowAct.View | RowAct.Edit);
                if (act != RowAct.None) Act(hit.Match, act);
                else
                {
                    _selected = hit.Match;
                    Open(hit.Match);
                }
                e.Use();
                return;
            }
        }

        static void Menu(Match match)
        {
            string path = match.Root.TrimEnd('/') + "/" + match.Path;
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Copy path", () => Copy(path)),
                new FloatMenuOption("Copy relative path", () => Copy(match.Path)),
            };

            FilesView.AddFileActions(opts, match.Project, path, Leaf(match.Path), match.Path);

            opts.Add(new FloatMenuOption("View", () => Open(match)));
            opts.Add(new FloatMenuOption("Edit", () => FilesView.EditFile(
                match.Project, path, "edit-" + Leaf(match.Path), match.Line)));
            TerminalWindow.OpenOverPane(new UiMenu(opts));
        }

        static Rect Screen(Rect r)
        {
            var body = ResultsBody();
            var moved = new Rect(body.x + r.x - Scroll.Position.x,
                body.y + r.y - Scroll.Position.y, r.width, r.height);
            return moved.yMax <= body.y || moved.y >= body.yMax ? Rect.zero : moved;
        }

        static Rect ResultsBody()
        {
            var body = AgentSidebar.Body;
            float toolsBottom = body.y + Pad + ToolsH;
            return new Rect(body.x, toolsBottom + Pad, body.width,
                Mathf.Max(0f, body.yMax - toolsBottom - Pad));
        }

        static void Open(Match match)
        {
            Open(match, true);
        }

        public static bool FocusLocation(string project, string path, int line)
        {
            foreach (var group in Groups)
                foreach (var match in group.Matches)
                    if (match.Project == project && match.Path == path && match.Line == line)
                    {
                        _selected = match;
                        Open(match, false);
                        return true;
                    }
            return false;
        }

        static void Open(Match match, bool remember)
        {
            string path = match.Root.TrimEnd('/') + "/" + match.Path;
            if (remember) AgentSidebar.RememberSearch(match.Project, match.Path, match.Line);
            if (ReferenceEquals(match, _showing) && Viewer.Reopen())
            {
                SessionHub.Instance.Terminal.SendKeys(Viewer.Session,
                    new[] { match.Line.ToString() + "g" }, true);
                return;
            }
            _showing = match;
            Viewer.ViewFileAt(match.Project, path, match.Line, FilesView.ReaderLabel(match.Project, "search-" + Leaf(match.Path)), "search");
        }

        static void Act(Match match, RowAct act)
        {
            switch (act)
            {
                case RowAct.View:
                    Open(match);
                    break;

                case RowAct.Edit:
                    string path = match.Root.TrimEnd('/') + "/" + match.Path;
                    FilesView.EditFile(match.Project, path, "edit-" + Leaf(match.Path), match.Line);
                    break;
            }
        }

        static string Leaf(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash < 0 ? path : path.Substring(slash + 1);
        }

        static void Copy(string text) => DaemonClipboard.Copy(text,
            () => Messages.Message($"SlopWorld: copied {text}", MessageTypeDefOf.SilentInput,
                false), UiLayout.Fail);

        public static void ReleaseViewer()
        {
            _showing = null;
            Viewer.Release();
        }

        public static void CloseViewerIf(string session) => Viewer.CloseIf(session);
    }
}
