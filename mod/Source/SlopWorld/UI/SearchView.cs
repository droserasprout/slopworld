using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Workspace search is a sidebar body rather than a terminal command: the daemon runs
    // ripgrep where it can see the projects, and this side keeps only the small structured
    // answer needed to group matches and open the existing pager on one of them.
    public static class SearchView
    {
        static float RowH => SlopWidgets.TinyRowH;
        const float Pad = 6f;
        const float CellX = 8f;
        const float ToolsH = 54f;

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

        static readonly List<Group> Groups = new List<Group>();
        static readonly List<Hit> Hits = new List<Hit>();
        static readonly SmoothScroll Scroll = new SmoothScroll();
        static readonly Pager Viewer = new Pager();

        static string _query = "";
        static bool _regex;
        static bool _case;
        static bool _word;
        static bool _loading;
        static int _generation;
        static int _pending;
        static Match _selected;
        static Match _showing;
        static bool _focus;

        public static void Entered() => _focus = true;

        public static void Draw(Rect body)
        {
            DrawTools(new Rect(body.x + CellX, body.y + Pad,
                body.width - CellX * 2f, ToolsH));

            var results = new Rect(body.x, body.y + ToolsH + Pad,
                body.width, Mathf.Max(0f, body.height - ToolsH - Pad));
            DrawResults(results);
        }

        static void DrawTools(Rect r)
        {
            const float buttonW = 30f;
            var field = new Rect(r.x, r.y, r.width - buttonW - SlopWidgets.GapS,
                SlopWidgets.FieldH);
            string was = _query;
            _query = SlopWidgets.Field(field, "search.query", _query);
            if (_focus)
            {
                GUI.FocusControl("search.query");
                _focus = false;
            }

            var go = new Rect(field.xMax + SlopWidgets.GapS, field.y, buttonW, field.height);
            bool clicked = SlopWidgets.Button(go, "›", SlopWidgets.Btn.Primary);
            bool entered = Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return ||
                 Event.current.keyCode == KeyCode.KeypadEnter) &&
                GUI.GetNameOfFocusedControl() == "search.query";
            if (clicked || entered)
            {
                if (entered) Event.current.Use();
                Search();
            }

            float y = field.yMax + SlopWidgets.GapS;
            float w = (r.width - SlopWidgets.GapS * 2f) / 3f;
            _case = SlopWidgets.Checkbox(new Rect(r.x, y, w, SlopWidgets.RowH),
                "Case", _case, "Case-sensitive search");
            _word = SlopWidgets.Checkbox(new Rect(r.x + w + SlopWidgets.GapS, y,
                w, SlopWidgets.RowH), "Word", _word, "Match whole words");
            _regex = SlopWidgets.Checkbox(new Rect(r.x + (w + SlopWidgets.GapS) * 2f, y,
                w, SlopWidgets.RowH), "Regex", _regex, "Interpret the query as a regex");

            // Changing text does not search on every frame; Enter is the deliberate boundary
            // between editing a potentially expensive expression and running it.
            if (was != _query) _selected = null;
        }

        public static void Search()
        {
            string query = (_query ?? "").Trim();
            if (query.Length == 0) return;

            ReleaseViewer();
            Groups.Clear();
            _selected = null;
            _loading = true;
            int generation = ++_generation;

            var projects = new List<ProjectInfo>();
            foreach (var p in SessionHub.Instance.Projects)
                if (!string.IsNullOrEmpty(p.Dir)) projects.Add(p);
            projects.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            _pending = projects.Count;
            if (_pending == 0) { _loading = false; return; }

            foreach (var p in projects)
            {
                var group = new Group { Project = p.Name };
                Groups.Add(group);
                string url = "/api/search?path=" + Uri.EscapeDataString(p.Dir) +
                    "&q=" + Uri.EscapeDataString(query) +
                    "&regex=" + (_regex ? "1" : "0") +
                    "&case=" + (_case ? "1" : "0") +
                    "&word=" + (_word ? "1" : "0") +
                    "&hidden=" + (Settings.SidebarShowHidden ? "1" : "0");
                SlopClient.Get(url, j =>
                {
                    if (generation != _generation) return;
                    foreach (var row in j["matches"].Items)
                        group.Matches.Add(new Match
                        {
                            Project = p.Name,
                            Root = p.Dir,
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
                    Done(generation);
                }, msg =>
                {
                    if (generation != _generation) return;
                    group.Error = msg;
                    Done(generation);
                });
            }
        }

        static void Done(int generation)
        {
            if (generation != _generation) return;
            _pending--;
            if (_pending <= 0) _loading = false;
        }

        static void DrawResults(Rect body)
        {
            Hits.Clear();
            float height = Measure();
            var view = new Rect(0f, 0f, body.width - (height > body.height ? 16f : 0f),
                Mathf.Max(body.height, height));
            Scroll.Begin(body, view);
            try
            {
                float y = Pad;
                if (Groups.Count == 0)
                {
                    Note(view.width, ref y, _loading ? "Searching…" :
                        string.IsNullOrWhiteSpace(_query) ? "Type a query and press Enter."
                        : "No results.", SlopWidgets.Faint);
                }
                foreach (var group in Groups)
                {
                    if (group.Matches.Count == 0 && group.Error == null && !_loading) continue;
                    Heading(view.width, ref y, group);
                    if (group.Error != null) Note(view.width, ref y, group.Error, SlopWidgets.Bad);
                    else
                    {
                        string file = null;
                        foreach (var match in group.Matches)
                        {
                            if (match.Path != file)
                            {
                                file = match.Path;
                                FileHeading(view.width, ref y, file);
                            }
                            Result(view.width, ref y, match);
                        }
                    }
                    if (group.Truncated)
                        Note(view.width, ref y, "… more matches", SlopWidgets.Faint);
                }
                if (_loading) Note(view.width, ref y, "Searching…", SlopWidgets.Faint);
            }
            finally
            {
                Scroll.End();
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }
        }

        static float Measure()
        {
            float rows = 1f;
            foreach (var g in Groups)
                if (g.Matches.Count > 0 || g.Error != null || _loading)
                    rows += 1f + g.Matches.Count + Files(g) + (g.Error != null ? 1f : 0f) +
                        (g.Truncated ? 1f : 0f);
            return Pad * 2f + rows * RowH;
        }

        static int Files(Group group)
        {
            int count = 0;
            string path = null;
            foreach (var match in group.Matches)
                if (match.Path != path) { path = match.Path; count++; }
            return count;
        }

        static void Heading(float width, ref float y, Group group)
        {
            var r = new Rect(0f, y, width, RowH);
            GUI.color = SlopWidgets.Lead;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            SlopWidgets.RowLabel(new Rect(CellX, y, width - CellX * 2f, RowH),
                $"{group.Project}  {group.Matches.Count}");
            GUI.color = Color.white;
            y += RowH;
        }

        static void FileHeading(float width, ref float y, string path)
        {
            GUI.color = SlopWidgets.Name;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            SlopWidgets.RowLabel(new Rect(CellX + 8f, y, width - CellX * 2f - 8f, RowH), path);
            GUI.color = Color.white;
            y += RowH;
        }

        static void Result(float width, ref float y, Match match)
        {
            var r = new Rect(0f, y, width, RowH);
            bool over = SlopWidgets.HoverRow(r);
            if (ReferenceEquals(match, _selected))
                Widgets.DrawBoxSolid(r, SlopWidgets.RowOn);

            string prefix = match.Line + ":" + match.Column;
            float prefixW = Mathf.Min(width * 0.55f, SlopWidgets.Wide(prefix) + 8f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = SlopWidgets.Name;
            SlopWidgets.RowLabel(new Rect(CellX + 16f, y, prefixW, RowH), prefix);
            GUI.color = over ? SlopWidgets.Lead : SlopWidgets.Dim;
            SlopWidgets.RowLabel(new Rect(CellX + 16f + prefixW, y,
                width - CellX * 2f - 16f - prefixW, RowH), match.Text.Trim());
            GUI.color = Color.white;
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
            SlopWidgets.RowLabel(new Rect(CellX, y, width - CellX * 2f, RowH), text);
            GUI.color = Color.white;
            y += RowH;
        }

        public static void Clicks()
        {
            if (!ColonistBarStrip.Interactive) return;
            var e = Event.current;
            if (e.rawType != EventType.MouseDown || e.button != 0) return;
            foreach (var hit in Hits)
            {
                if (!ColonistBarStrip.MouseOver(Screen(hit.Rect))) continue;
                _selected = hit.Match;
                Open(hit.Match);
                e.Use();
                return;
            }
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
            return new Rect(body.x, body.y + ToolsH + Pad, body.width,
                Mathf.Max(0f, body.height - ToolsH - Pad));
        }

        static void Open(Match match)
        {
            string path = match.Root.TrimEnd('/') + "/" + match.Path;
            if (ReferenceEquals(match, _showing) && Viewer.Reopen())
            {
                SessionHub.Instance.SendKeys(Viewer.Session,
                    new[] { match.Line.ToString() + "g" }, true);
                return;
            }
            _showing = match;
            Viewer.ViewFileAt(match.Project, path, match.Line, "search-" + Leaf(match.Path));
        }

        static string Leaf(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash < 0 ? path : path.Substring(slash + 1);
        }

        public static void ReleaseViewer()
        {
            _showing = null;
            Viewer.Release();
        }

        public static void CloseViewerIf(string session) => Viewer.CloseIf(session);
    }
}
