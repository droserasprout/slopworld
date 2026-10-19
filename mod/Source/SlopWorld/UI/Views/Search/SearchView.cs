using System;
using System.Collections.Generic;
using System.Globalization;
using RimWorld;
using UnityEngine;
using Verse;
using Group = SlopWorld.SearchStore.Group;
using LayoutRow = SlopWorld.SearchResultTree.Row;
using Match = SlopWorld.SearchStore.Match;
using RowKind = SlopWorld.SearchResultTree.RowKind;

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
        const float ResultIndent = 11f;
        static readonly string[] OptionLabels =
        {
            "case",
            "word",
            "regex",
            "gitignd",
            "inc",
            "exc",
        };

        static float PathFieldsH =>
            ((_includePaths ? 1 : 0) + (_excludePaths ? 1 : 0)) * (UiTheme.FieldH + UiTheme.GapS);

        static float ToolsH(float width) =>
            UiTheme.FieldH + UiTheme.GapS + OptionsH(width) + PathFieldsH;

        static float OptionsH(float width)
        {
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Small;
                var row = new Rect(0f, 0f, width, UiTheme.RowH);
                float x = 0f,
                    y = 0f;
                foreach (var label in OptionLabels)
                    OptionRect(ref x, ref y, row, label);
                return y + UiTheme.RowH;
            }
        }

        struct Hit
        {
            public Rect Rect;
            public Match Match;
            public string FoldKey;
        }

        static readonly SearchResultTree ResultTree = new SearchResultTree();

        static readonly SearchStore Store = new SearchStore(
            SidebarScopes.Enabled,
            (url, ok, fail) => DaemonClient.Get<Wire.SearchResult>(url, ok, fail)
        );
        static List<Group> Groups => Store.Groups;
        static bool _loading => Store.Loading;
        static readonly List<Hit> Hits = new List<Hit>();
        static readonly List<LayoutRow> Layout = new List<LayoutRow>();
        static readonly SmoothScroll Scroll = new SmoothScroll();
        static float ScrollbarHitInset;
        static readonly PagerTabs Viewers = new PagerTabs();
        static readonly MouseClickSequence ResultClicks = new MouseClickSequence();
        static long _restoredVersion = -1;

        internal static bool ReaderOpening => Viewers.Find(tab => tab.Opening) != null;
        internal static IEnumerable<Pager> Readers
        {
            get
            {
                RestoreReader();
                return Viewers.All;
            }
        }
        static FieldLifetime _fieldLifetime = new FieldLifetime();

        static SearchView()
        {
            Store.Changed += DirtyLayout;
            SidebarScopes.Changed += FilterChanged;
        }

        public static void FilterChanged()
        {
            if (Store.Submitted != null)
                RunSearch(Store.Submitted);
        }

        static string _query = "";
        static bool _regex;
        static bool _case;
        static bool _word;
        static bool _includeIgnored;
        static bool _includePaths;
        static bool _excludePaths;
        static string _include = "";
        static string _exclude = "";
        static Match _selected;
        static bool _focus;
        static bool _layoutDirty = true;
        static float _contentHeight;
        static int _layoutRevision = int.MinValue;

        static void RestoreReader()
        {
            var hub = SessionHub.Instance;
            if (_restoredVersion == hub.SessionsVersion)
                return;
            _restoredVersion = hub.SessionsVersion;
            foreach (var info in hub.Sessions)
                if (info != null && info.Alive && info.Intent == "search")
                    Viewers.AttachRestored(info);
        }

        public static void Entered()
        {
            RestoreReader();
            ResetFieldLifetime();
            _focus = true;
        }

        public static void Draw(Rect body)
        {
            SidebarScopes.Update();
            using (FieldLifetimeScope.Push(_fieldLifetime))
            {
                var tools = new Rect(
                    body.x + CellX,
                    body.y + Pad,
                    body.width - CellX * 2f,
                    ToolsH(body.width - CellX * 2f)
                );
                var results = new Rect(
                    body.x,
                    tools.yMax + Pad,
                    body.width,
                    Mathf.Max(0f, body.yMax - tools.yMax - Pad)
                );
                if (
                    Scroll.HandleWheel(
                        results,
                        _layoutRevision == int.MinValue ? -1f : _contentHeight
                    )
                )
                    return;
                DrawTools(tools);
                DrawResults(results);
            }
        }

        static void DrawTools(Rect r)
        {
            float buttonW = UiTheme.FieldH;
            var field = new Rect(r.x, r.y, r.width - buttonW - UiTheme.GapS, UiTheme.FieldH);
            var includeField = new Rect(
                r.x,
                field.yMax + UiTheme.GapS + OptionsH(r.width) + UiTheme.GapS,
                r.width,
                UiTheme.FieldH
            );
            var excludeField = new Rect(
                r.x,
                includeField.y + (_includePaths ? UiTheme.FieldH + UiTheme.GapS : 0f),
                r.width,
                UiTheme.FieldH
            );
            var e = Event.current;

            if (
                e.type == EventType.MouseDown
                && !field.Contains(e.mousePosition)
                && !(_includePaths && includeField.Contains(e.mousePosition))
                && !(_excludePaths && excludeField.Contains(e.mousePosition))
            )
                ReleaseFocus();

            string was = _query;
            _query = UiText.Field(field, "search.query", _query);
            if (_includePaths)
                _include = DrawPathField(includeField, "search.include", "inc", _include);
            if (_excludePaths)
                _exclude = DrawPathField(excludeField, "search.exclude", "exc", _exclude);
            if (_focus)
            {
                GUI.FocusControl("search.query");
                _focus = false;
            }

            // The tab focuses this field when it opens. A sidebar entry must not keep game shortcuts
            // captive after a search, Escape, or a click elsewhere.
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && Focused)
            {
                ReleaseFocus();
                e.Use();
                return;
            }

            var go = new Rect(field.xMax + UiTheme.GapS, field.y, buttonW, field.height);
            bool clicked = UiButtons.Button(go, "›", UiTheme.Btn.Primary);
            bool entered =
                e.type == EventType.KeyDown
                && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                && Focused;
            if (clicked || entered)
            {
                if (entered)
                    e.Use();
                ReleaseFocus();
                Search();
            }

            float y = field.yMax + UiTheme.GapS;
            bool caseSensitive = _case;
            bool wholeWord = _word;
            bool regex = _regex;
            bool includeIgnored = _includeIgnored;
            bool includedPaths = _includePaths;
            bool excludedPaths = _excludePaths;
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Small;
                float x = r.x;
                _case = DrawOption(ref x, ref y, r, "case", _case, "Case-sensitive search");
                _word = DrawOption(ref x, ref y, r, "word", _word, "Match whole words");
                _regex = DrawOption(
                    ref x,
                    ref y,
                    r,
                    "regex",
                    _regex,
                    "Interpret the query as a regex"
                );
                _includeIgnored = DrawOption(
                    ref x,
                    ref y,
                    r,
                    "gitignd",
                    _includeIgnored,
                    "Include files ignored by Git"
                );
                _includePaths = DrawOption(
                    ref x,
                    ref y,
                    r,
                    "inc",
                    _includePaths,
                    "Filter included paths by glob"
                );
                _excludePaths = DrawOption(
                    ref x,
                    ref y,
                    r,
                    "exc",
                    _excludePaths,
                    "Filter excluded paths by glob"
                );
            }

            if (
                (
                    caseSensitive != _case
                    || wholeWord != _word
                    || regex != _regex
                    || includeIgnored != _includeIgnored
                    || includedPaths != _includePaths
                    || excludedPaths != _excludePaths
                ) && !string.IsNullOrWhiteSpace(_query)
            )
                Search();

            // Text edits wait for submission; option toggles apply the current draft.
            if (was != _query)
                _selected = null;
        }

        static string DrawPathField(Rect r, string name, string label, string value)
        {
            float labelW = UiTheme.Wide(label) + UiTheme.GapS;
            var field = new Rect(r.x, r.y, r.width - labelW, r.height);
            using (WidgetState.Save())
            {
                GUI.color = UiTheme.Dim;
                UiText.FadedRowLabel(
                    new Rect(field.xMax + UiTheme.GapS, r.y, labelW - UiTheme.GapS, r.height),
                    label,
                    TextAnchor.MiddleRight
                );
            }
            TooltipHandler.TipRegion(field, "Path or glob, e.g. notes, *.cs, src/**, or *.{cs,rs}");
            return UiText.Field(field, name, value);
        }

        // Measurement and drawing share wrapping so result drawing and hit testing
        // start below the same controls even at the minimum sidebar width.
        static Rect OptionRect(ref float x, ref float y, Rect row, string label)
        {
            float width = Mathf.Min(row.width, UiTheme.Wide(label) + UiTheme.GapXS * 2f);
            if (x > row.x && x + width > row.xMax)
            {
                x = row.x;
                y += UiTheme.RowH;
            }
            var rect = new Rect(x, y, width, UiTheme.RowH);
            x = rect.xMax + UiTheme.GapS;
            return rect;
        }

        static bool DrawOption(
            ref float x,
            ref float y,
            Rect row,
            string label,
            bool on,
            string tip
        ) => ToggleCell.DrawText(OptionRect(ref x, ref y, row, label), label, on, tip);

        static bool Focused
        {
            get
            {
                string name = GUI.GetNameOfFocusedControl();
                return name == "search.query"
                    || name == "search.include"
                    || name == "search.exclude";
            }
        }

        // Public because changing sidebar tabs removes the entry that owns this focus. Without
        // clearing it first, Unity keeps reporting a text field after that entry is gone.
        public static void ReleaseFocus()
        {
            ResetFieldLifetime();
            _focus = false;
            if (Focused)
                GUI.FocusControl(null);
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

        public static void Search() =>
            RunSearch(
                new SearchSubmission(
                    _query,
                    _regex,
                    _case,
                    _word,
                    _includeIgnored,
                    _includePaths ? _include : "",
                    _excludePaths ? _exclude : ""
                )
            );

        static void RunSearch(SearchSubmission submitted)
        {
            // Result replacement does not dismiss the file the user is reading.
            if (submitted.Query.Length > 0)
                _selected = null;
            Scroll.JumpTo(Vector2.zero);
            // Files visibility settings do not constrain Search; its own flags filter results.
            Store.Search(submitted, SidebarScopes.EnabledScopes(), hidden: true);
        }

        static void DirtyLayout() => _layoutDirty = true;

        public static bool AllFolded => ResultTree.AllFolded(Groups);

        public static void SetAllFolded(bool folded)
        {
            ResultTree.SetAllFolded(Groups, folded);
            DirtyLayout();
            Hits.Clear();
        }

        static void DrawResults(Rect body)
        {
            EnsureLayout();
            Hits.Clear();
            float height = _contentHeight;
            var geometry = UiScrollBody.Measure(
                body,
                height,
                UiScrollbarReservation.WhenNeeded,
                UiTheme.ScrollTrackXInset
            );
            var view = geometry.View;
            ScrollbarHitInset = geometry.ScrollbarHitInset;
            using (WidgetState.Save())
            using (Scroll.Scope(body, view))
            {
                if (SmoothScroll.WheelOnly)
                    return;
                float visibleTop = Scroll.Position.y;
                float visibleBottom = visibleTop + body.height;
                int first = Mathf.Max(0, Mathf.FloorToInt((visibleTop - Pad) / RowH) - 1);
                int last = Mathf.Min(
                    Layout.Count,
                    Mathf.CeilToInt((visibleBottom - Pad) / RowH) + 1
                );
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
                case RowKind.File:
                    Heading(width, ref y, row);
                    break;
                case RowKind.Match:
                    Result(width, ref y, row.Match, row.Depth);
                    break;
                case RowKind.Note:
                    Note(width, ref y, row.Text, UiTheme.Faint);
                    break;
                case RowKind.Error:
                    Note(width, ref y, row.Text, UiTheme.Bad);
                    break;
            }
        }

        static void Heading(float width, ref float y, LayoutRow row)
        {
            var r = new Rect(0f, y, width, RowH);
            RowChrome.Hover(r, false, true, RowHoverPolicy.OverlayAware);
            r.width = Mathf.Max(0f, width - ScrollbarHitInset);
            bool collapsed = ResultTree.IsCollapsed(row.FoldKey);
            GUI.color = UiTheme.Faint;
            var arrow = new Rect(
                CellX + row.Depth * ResultIndent,
                y + (RowH - UiTheme.DisclosureW) / 2f,
                UiTheme.DisclosureW,
                UiTheme.DisclosureW
            );
            GUI.DrawTexture(arrow, collapsed ? TexButton.Reveal : TexButton.Collapse);
            GUI.color = row.Kind == RowKind.File ? UiTheme.Name : UiTheme.Faint;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            float left = arrow.xMax + UiTheme.GapXS;
            if (row.MainCheckout)
            {
                // Match visible text ink rather than the padded line box.
                float side = UiTheme.RootIconW;
                GUI.DrawTexture(new Rect(left, y + (RowH - side) / 2f, side, side), Icons.Root);
                left += side + UiTheme.GapXS;
            }
            float labelRight = r.width - Pad;
            if (row.MatchCount.HasValue)
            {
                string count = row.MatchCount.Value.ToString(CultureInfo.InvariantCulture);
                float countWidth = UiTheme.Wide(count);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(labelRight - countWidth, y, countWidth, RowH), count);
                Text.Anchor = TextAnchor.MiddleLeft;
                labelRight -= countWidth + UiTheme.GapXS;
            }
            UiText.FadedRowLabel(
                new Rect(left, y, Mathf.Max(0f, labelRight - left), RowH),
                row.Text
            );
            GUI.color = Color.white;
            if (row.Kind == RowKind.Heading)
                Slab.Hairline(new Rect(CellX, r.yMax - 1f, width - CellX * 2f, 1f), UiTheme.Edge);
            TooltipHandler.TipRegion(
                r,
                (row.MainCheckout ? "Main checkout  " : "")
                    + row.Text
                    + (row.MatchCount.HasValue ? $"\n{row.MatchCount.Value} matching lines" : "")
                    + (collapsed ? "\n\nClick to unfold." : "\n\nClick to fold.")
            );
            Hits.Add(
                new Hit
                {
                    Rect = r,
                    FoldKey = row.FoldKey,
                    Match = row.MenuMatch,
                }
            );
            y += RowH;
        }

        static void Result(float width, ref float y, Match match, int depth)
        {
            var r = new Rect(0f, y, width, RowH);
            bool over = RowChrome.Hover(
                r,
                ReferenceEquals(match, _selected),
                true,
                RowHoverPolicy.OverlayAware
            );
            r.width = Mathf.Max(0f, width - ScrollbarHitInset);

            string prefix = match.Line + ":" + match.Column;
            float prefixW = Mathf.Min(width * 0.55f, UiTheme.Wide(prefix) + 8f);
            float rx = width - Pad;
            var acts = over ? RowAct.View | RowAct.Edit : RowAct.None;
            if (acts != RowAct.None)
                rx = RowActions.Draw(r, rx - ScrollbarHitInset, acts) - UiTheme.GapXS;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = UiTheme.Name;
            UiText.FadedRowLabel(new Rect(CellX + ResultIndent * depth, y, prefixW, RowH), prefix);
            GUI.color = over ? UiTheme.Lead : UiTheme.Dim;
            UiText.FadedRowLabel(
                new Rect(
                    CellX + ResultIndent * depth + prefixW,
                    y,
                    Mathf.Max(0f, rx - CellX - ResultIndent * depth - prefixW),
                    RowH
                ),
                match.Text.Trim()
            );
            GUI.color = Color.white;
            if (RowActions.Hit(r, r.width - Pad, acts) == RowAct.None)
                TooltipHandler.TipRegion(
                    r,
                    $"{match.Path}:{match.Line}:{match.Column}\n{match.Text.Trim()}"
                );
            Hits.Add(new Hit { Rect = r, Match = match });
            y += RowH;
        }

        static void Note(float width, ref float y, string text, Color color)
        {
            GUI.color = color;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            UiText.FadedRowLabel(new Rect(CellX, y, width - CellX * 2f, RowH), text);
            GUI.color = Color.white;
            y += RowH;
        }

        public static void Clicks()
        {
            if (!ColonistBarStrip.Interactive)
                return;
            var e = Event.current;
            if (e.rawType != EventType.MouseDown || (e.button != 0 && e.button != 1))
                return;
            // Partially visible rows retain their full geometry for action placement.
            // Only the portion inside the scroll viewport may receive clicks.
            if (!ResultsBody().Contains(e.mousePosition))
            {
                ResultClicks.Reset();
                return;
            }
            foreach (var hit in Hits)
            {
                var scr = Screen(hit.Rect);
                if (!ColonistBarStrip.MouseOver(scr))
                    continue;

                if (e.button == 1 && hit.Match != null)
                {
                    ResultClicks.Reset();
                    Menu(hit.Match);
                    e.Use();
                    return;
                }

                if (hit.FoldKey != null)
                {
                    ResultClicks.Reset();
                    if (e.button == 0)
                    {
                        ResultTree.Toggle(hit.FoldKey);
                        DirtyLayout();
                        Hits.Clear();
                    }
                    e.Use();
                    return;
                }

                var act = RowActions.Hit(scr, scr.xMax - Pad, RowAct.View | RowAct.Edit);
                if (act != RowAct.None)
                {
                    ResultClicks.Reset();
                    Act(hit.Match, act);
                }
                else
                {
                    if (!ReferenceEquals(_selected, hit.Match))
                        ResultClicks.Reset();
                    int clickCount = ResultClicks.Observe(e, Time.realtimeSinceStartup);
                    _selected = hit.Match;
                    Open(hit.Match);
                    if (clickCount >= 2)
                    {
                        Viewers.LockPreview(
                            hit.Match.Project,
                            hit.Match.Root.TrimEnd('/') + "/" + hit.Match.Path
                        );
                        ResultClicks.Reset();
                    }
                }
                e.Use();
                return;
            }
            ResultClicks.Reset();
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
            opts.Add(
                new FloatMenuOption(
                    "Edit",
                    () =>
                        FilesView.EditFile(
                            match.Project,
                            path,
                            "edit-" + Leaf(match.Path),
                            match.Line
                        )
                )
            );
            WorkspaceWindow.OpenOverPane(new UiMenu(opts));
        }

        static Rect Screen(Rect r)
        {
            var body = ResultsBody();
            var moved = new Rect(
                body.x + r.x - Scroll.Position.x,
                body.y + r.y - Scroll.Position.y,
                r.width,
                r.height
            );
            return moved.yMax <= body.y || moved.y >= body.yMax ? Rect.zero : moved;
        }

        static Rect ResultsBody()
        {
            var body = Sidebar.Body;
            float toolsBottom = body.y + Pad + ToolsH(body.width - CellX * 2f);
            return new Rect(
                body.x,
                toolsBottom + Pad,
                body.width,
                Mathf.Max(0f, body.yMax - toolsBottom - Pad)
            );
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
                    ResultTree.Reveal(Groups, match);
                    DirtyLayout();
                    _selected = match;
                    Open(match, false);
                    return true;
                }
            return false;
        }

        static void Open(Match match, bool remember)
        {
            Sidebar.RevealOpenFiles();
            string path = match.Root.TrimEnd('/') + "/" + match.Path;
            if (remember)
                Sidebar.RememberSearch(match.Project, match.Path, match.Line);
            var viewer = Viewers.Find(tab => tab.Owns(match.Project, path));
            if (viewer != null && !viewer.Opening && viewer.Reopen())
            {
                SessionHub.Instance.Terminal.SendKeys(
                    viewer.Session,
                    new[] { match.Line.ToString(CultureInfo.InvariantCulture) + "g" },
                    true
                );
                return;
            }
            (viewer ?? Viewers.ForPreview()).ViewFileAt(
                match.Project,
                path,
                match.Line,
                FilesView.ReaderLabel(match.Project, "search-" + Leaf(match.Path)),
                "search"
            );
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

        static void Copy(string text) => UiClipboardActions.Copy(text, $"SlopWorld: copied {text}");

        public static void ReleaseViewer()
        {
            Viewers.ReleasePreview();
        }

        public static void CloseViewerIf(string session) => Viewers.CloseIf(session);

        internal static IPreviewTab FindReader(string session)
        {
            RestoreReader();
            return Viewers.FindSession(session);
        }
    }
}
