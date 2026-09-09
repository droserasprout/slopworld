using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Workspace search is a sidebar body rather than a terminal command: the daemon runs
    // ripgrep where it can see the projects, and this side keeps only the small structured
    // answer needed to group matches and open the existing pager on one of them.
    public static partial class SearchView
    {
        static float RowH => UiWidgets.TinyRowH;
        static float Pad => UiWidgets.GapS;
        static float CellX => UiWidgets.GapS;
        static float ToolsH => UiWidgets.FieldH + UiWidgets.GapS + UiWidgets.RowH;

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
        static FieldLifetime _fieldLifetime = new FieldLifetime();

        static string _query = "";
        static bool _regex;
        static bool _case;
        static bool _word;
        static bool _includeIgnored;
        static bool _loading;
        static int _generation;
        static int _pending;
        static Match _selected;
        static Match _showing;
        static bool _focus;
        static bool _layoutDirty = true;
        static float _contentHeight;
        static int _layoutRevision = int.MinValue;

        public static void Entered()
        {
            ResetFieldLifetime();
            _focus = true;
        }

        public static void Draw(Rect body)
        {
            using (FieldLifetimeScope.Push(_fieldLifetime))
            {
                var tools = new Rect(body.x + CellX, body.y + Pad,
                    body.width - CellX * 2f, ToolsH);
                DrawTools(tools);

                var results = new Rect(body.x, tools.yMax + Pad,
                    body.width, Mathf.Max(0f, body.yMax - tools.yMax - Pad));
                DrawResults(results);
            }
        }

        static void DrawTools(Rect r)
        {
            float buttonW = UiWidgets.FieldH;
            var field = new Rect(r.x, r.y, r.width - buttonW - UiWidgets.GapS,
                UiWidgets.FieldH);
            var e = Event.current;

            string was = _query;
            _query = UiWidgets.Field(field, "search.query", _query);
            if (_focus)
            {
                GUI.FocusControl("search.query");
                _focus = false;
            }

            // The tab gives this field focus when it is opened, but a sidebar entry must not
            // keep the game's shortcuts captive after a search, an Escape, or a click elsewhere.
            if (e.type == EventType.MouseDown && !field.Contains(e.mousePosition))
                ReleaseFocus();
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && Focused)
            {
                ReleaseFocus();
                e.Use();
                return;
            }

            var go = new Rect(field.xMax + UiWidgets.GapS, field.y, buttonW, field.height);
            bool clicked = UiWidgets.Button(go, "›", UiWidgets.Btn.Primary);
            bool entered = e.type == EventType.KeyDown &&
                (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && Focused;
            if (clicked || entered)
            {
                if (entered) e.Use();
                ReleaseFocus();
                Search();
            }

            float y = field.yMax + UiWidgets.GapS;
            float w = (r.width - UiWidgets.GapS * 3f) / 4f;
            _case = UiWidgets.Checkbox(new Rect(r.x, y, w, UiWidgets.RowH),
                "Case", _case, "Case-sensitive search");
            _word = UiWidgets.Checkbox(new Rect(r.x + w + UiWidgets.GapS, y,
                w, UiWidgets.RowH), "Word", _word, "Match whole words");
            _regex = UiWidgets.Checkbox(new Rect(r.x + (w + UiWidgets.GapS) * 2f, y,
                w, UiWidgets.RowH), "Regex", _regex, "Interpret the query as a regex");
            _includeIgnored = UiWidgets.Checkbox(new Rect(r.x + (w + UiWidgets.GapS) * 3f, y,
                w, UiWidgets.RowH), "Include ignored", _includeIgnored,
                "Include files ignored by Git");

            // Changing text does not search on every frame; Enter is the deliberate boundary
            // between editing a potentially expensive expression and running it.
            if (was != _query) _selected = null;
        }

        static bool Focused => GUI.GetNameOfFocusedControl() == "search.query";

        // Public because changing sidebar tabs removes the entry that owns this focus; without
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
            string query = (_query ?? "").Trim();
            if (query.Length == 0)
            {
                ResetToEmpty();
                return;
            }

            ReleaseViewer();
            Groups.Clear();
            DirtyLayout();
            _selected = null;
            _loading = true;
            Scroll.JumpTo(Vector2.zero);
            int generation = ++_generation;

            var projects = SearchProjects();
            _pending = projects.Count;
            if (_pending == 0) { _loading = false; return; }

            foreach (var p in projects)
            {
                var group = new Group { Project = p.Name };
                Groups.Add(group);
                string url = WireContract.Routes.Search + "?path=" + Uri.EscapeDataString(p.Dir) +
                    "&q=" + Uri.EscapeDataString(query) +
                    "&regex=" + (_regex ? "1" : "0") +
                    "&case=" + (_case ? "1" : "0") +
                    "&word=" + (_word ? "1" : "0") +
                    "&gitignore=" + (_includeIgnored ? "0" : "1") +
                    "&hidden=" + (Settings.SidebarShowHidden ? "1" : "0");
                DaemonClient.Get(url, j => OnResults(group, p, generation, j),
                    msg => OnError(group, generation, msg));
            }
        }

        static void ResetToEmpty()
        {
            // Invalidate replies for the previous query before clearing its rows. Otherwise a
            // slow request can repopulate the result list after the user erased the field.
            ++_generation;
            _pending = 0;
            _loading = false;
            Groups.Clear();
            DirtyLayout();
            Scroll.JumpTo(Vector2.zero);
            ReleaseViewer();
        }

        static List<ProjectInfo> SearchProjects()
        {
            var projects = new List<ProjectInfo>();
            foreach (var p in SessionHub.Instance.Projects)
                if (!string.IsNullOrEmpty(p.Dir) && AgentSidebar.Passes(p.Name))
                    projects.Add(p);
            projects.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return projects;
        }

        static void OnResults(Group group, ProjectInfo project, int generation, JVal j)
        {
            if (generation != _generation) return;
            foreach (var row in j["matches"].Items)
                group.Matches.Add(new Match
                {
                    Project = project.Name,
                    Root = project.Dir,
                    Path = row["path"].AsString(),
                    Line = row["line"].AsInt(),
                    Column = row["column"].AsInt(),
                    Text = row["text"].AsString(),
                });
            group.Matches.Sort((a, b) =>
            {
                int path = string.CompareOrdinal(a.Path, b.Path);
                return path != 0 ? path : a.Line.CompareTo(b.Line);
            });
            group.Truncated = j["truncated"].AsBool();
            DirtyLayout();
            Done(generation);
        }

        static void OnError(Group group, int generation, string msg)
        {
            if (generation != _generation) return;
            group.Error = msg;
            DirtyLayout();
            Done(generation);
        }

        static void DirtyLayout() => _layoutDirty = true;

        static void Done(int generation)
        {
            if (generation != _generation) return;
            _pending--;
            if (_pending <= 0) _loading = false;
            DirtyLayout();
        }

        static void DrawResults(Rect body)
        {
            EnsureLayout();
            Hits.Clear();
            float height = _contentHeight;
            var view = UiScrollBody.ConditionalView(body, height);
            using (WidgetState.Save())
            using (Scroll.Scope(body, view))
            {
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
            GUI.color = UiWidgets.Lead;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            UiWidgets.RowLabel(new Rect(CellX, y, width - CellX * 2f, RowH),
                $"{group.Project}  {group.Matches.Count}");
            GUI.color = Color.white;
            y += RowH;
        }

        static void FileHeading(float width, ref float y, string path)
        {
            GUI.color = UiWidgets.Name;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            UiWidgets.RowLabel(new Rect(CellX + UiWidgets.GapS, y,
                width - CellX * 2f - UiWidgets.GapS, RowH), path);
            GUI.color = Color.white;
            y += RowH;
        }

        static void Result(float width, ref float y, Match match)
        {
            var r = new Rect(0f, y, width, RowH);
            bool over = RowChrome.Hover(r, ReferenceEquals(match, _selected), true,
                RowHoverPolicy.OverlayAware);

            string prefix = match.Line + ":" + match.Column;
            float prefixW = Mathf.Min(width * 0.55f, UiWidgets.Wide(prefix) + 8f);
            float rx = width - Pad;
            var acts = over ? RowAct.View | RowAct.Edit : RowAct.None;
            if (acts != RowAct.None) rx = RowActions.Draw(r, rx, acts) - UiWidgets.GapXS;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = UiWidgets.Name;
            UiWidgets.RowLabel(new Rect(CellX + UiWidgets.GapM, y, prefixW, RowH), prefix);
            GUI.color = over ? UiWidgets.Lead : UiWidgets.Dim;
            UiWidgets.RowLabel(new Rect(CellX + UiWidgets.GapM + prefixW, y,
                Mathf.Max(0f, rx - CellX - UiWidgets.GapM - prefixW), RowH), match.Text.Trim());
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
            UiWidgets.RowLabel(new Rect(CellX, y, width - CellX * 2f, RowH), text);
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
            string path = match.Root.TrimEnd('/') + "/" + match.Path;
            if (ReferenceEquals(match, _showing) && Viewer.Reopen())
            {
                SessionHub.Instance.Terminal.SendKeys(Viewer.Session,
                    new[] { match.Line.ToString() + "g" }, true);
                return;
            }
            _showing = match;
            Viewer.ViewFileAt(match.Project, path, match.Line, "search-" + Leaf(match.Path));
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
                false), UiWidgets.Fail);

        public static void ReleaseViewer()
        {
            _showing = null;
            Viewer.Release();
        }

        public static void CloseViewerIf(string session) => Viewer.CloseIf(session);
    }
}
