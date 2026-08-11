using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public static class AgentSidebar
    {
        public const float MinWidth = 150f;
        public const float MaxWidth = 460f;

        public static float Width => Mathf.Clamp(Settings.SidebarWidth, MinWidth,
            Mathf.Max(MinWidth, Mathf.Min(MaxWidth, UI.screenWidth * 0.4f)));

        // The scale an uncrowded portrait is drawn at. Read off the text rather than fixed:
        // a row is as tall as the three lines in it, and the pawn beside them is drawn that
        // tall - overflow included, so the whole picture is the height of the text and the
        // pitch keeps its `RowGap` between one portrait and the next. A constant here left
        // a 37px square sitting in a row the font was free to grow past.
        //
        // A face is a width as well as a height, and the label wants the rest of the panel,
        // so a share of the column caps it: a large font in a narrow one would otherwise
        // hand the whole row to the portrait. Bounded at both ends besides - the portrait
        // cache renders one unscaled texture, so past Ceiling the face is being upsampled,
        // and under Floor it is a thumbnail of a pawn.
        static float Nominal => Mathf.Clamp(
            Mathf.Min(Patch_SidebarPortraitDraw.FaceForHeight(TextH), Width * WidthShare)
                / ColonistBarColonistDrawer.PawnTextureSize.y,
            Floor, Ceiling);

        const float WidthShare = 0.35f;
        const float Floor = 0.3f;
        const float Ceiling = 1f;

        static float HeadH => SlopWidgets.TinyRowH;
        static float AddH => TopBar.H;

        const float AddIcon = SlopWidgets.IconW;
        const float Pad = SlopWidgets.GapS;
        const float CellX = SlopWidgets.GapS;
        const float TextGap = SlopWidgets.GapS;

        // The strip is one row of the buttons every view has - the five tabs on the left,
        // the filter on the right - and, under it, a second row of the buttons only the
        // current view has. That row is right-aligned like the filter above it and is only
        // there when the view actually has such a button, so the two views that have none
        // do not wear an empty band. `TabH` is what the body is pushed down by, so both
        // [Body] and [Place] follow on their own.
        static float TabRowH => TopBar.H;
        public static float TabH => TabRowH + (HasActions ? TabRowH : 0f);
        const float TabIcon = 20f;

        const float RowGap = SlopWidgets.GapXS;

        static float GhostH => NameH + 2f;

        static float NameH => SlopWidgets.LineHOf(GameFont.Small);
        static float SubH => SlopWidgets.TinyH;

        static float TextH => NameH + SubH * 2f;

        const float BellW = 13f;
        const float AgoGap = 6f;

        // The state badge is a share of the portrait rather than a fixed size: the column
        // shrinks to fit and a marker that did not would swallow a small face. The shared
        // status marker is its floor, below which a circle is a speck.
        const float BadgeShare = 0.22f;
        const float BadgeInset = 1f;
        const float BadgeRing = 1.5f;

        const float GhostMarkW = 12f;

        const float ArrowW = 12f;
        const float GripW = 5f;


        // The colonist bar draws and hit-tests the same location table.
        static readonly Vector2 Parked = new Vector2(-9999f, -9999f);

        struct Row
        {
            public string Session;
            public Pawn Pawn;
            public Rect Line;   // the whole row, for the highlight and the hover
            public Rect Text;   // beside the portrait: the three labels, and what a click takes
            public Rect Face;   // the square the close-up is drawn in, read by the drawer patch

            public bool Ghost;
        }

        struct Head
        {
            public string Label;
            public Rect Rect;   // the whole band, so the arrow and the name click as one
            public int Count;   // what is under it, which is the only thing a fold hides
            public bool Folded;
        }

        static readonly List<Row> Rows = new List<Row>();
        static readonly List<Head> Heads = new List<Head>();

        static readonly Dictionary<string, List<int>> Buckets =
            new Dictionary<string, List<int>>();
        static readonly List<string> Order = new List<string>();

        static readonly Dictionary<int, string> Named = new Dictionary<int, string>();

        static readonly Dictionary<string, List<SessionInfo>> Ghosts =
            new Dictionary<string, List<SessionInfo>>();

        static readonly List<SessionInfo> TopGhosts = new List<SessionInfo>();

        static readonly List<Row> ViewRows = new List<Row>();
        static readonly List<SessionInfo> Routed = new List<SessionInfo>();

        const string Loose = "no project";

        static HashSet<string> _folded;

        static HashSet<string> Folded
        {
            get
            {
                if (_folded == null)
                {
                    _folded = new HashSet<string>();
                    foreach (var name in Settings.FoldedProjects.Split('\n'))
                        if (name.Length > 0) _folded.Add(name);
                }
                return _folded;
            }
        }

        static void Fold(string key, bool on)
        {
            if (on) Folded.Add(key);
            else Folded.Remove(key);

            var names = new List<string>(Folded);
            names.Sort(System.StringComparer.Ordinal);
            var s = Settings.S;
            s.foldedProjects = string.Join("\n", names.ToArray());
            s.Write();
        }

        public static Rect Panel => new Rect(0f, 0f, Width, UI.screenHeight);

        public const string TabAgents = "agents", TabFiles = "files", TabSearch = "search",
            TabGit = "git", TabShortcuts = "shortcuts";

        public static bool Files => Settings.SidebarTab == TabFiles;
        public static bool Search => Settings.SidebarTab == TabSearch;
        public static bool Git => Settings.SidebarTab == TabGit;
        public static bool Shortcuts => Settings.SidebarTab == TabShortcuts;
        public static bool Agents => !Files && !Search && !Git && !Shortcuts;

        // Which views own a second row. Keep in step with what [Actions] draws.
        static bool HasActions => Files || Search || Git;

        // The project filter: a set of ticked keys every view is read through, empty being
        // all of them rather than none. Held and written the way the folds are - one name a
        // line - because it is the same kind of thing, and a key no project answers to
        // shows nothing rather than falling back to all: that is the honest reading while
        // the daemon is still handing its list over.
        //
        // Whatever has no project of its own is one more key, so it can be ticked like any
        // other. A project actually named this shares its line, which is the price of a
        // sentinel that reads the same in the settings file as it does in the menu.
        public const string NoProject = "[none]";

        static HashSet<string> _filter;

        static HashSet<string> Ticks
        {
            get
            {
                if (_filter == null)
                {
                    _filter = new HashSet<string>();
                    foreach (var name in Settings.SidebarFilter.Split('\n'))
                        if (name.Length > 0) _filter.Add(name);
                }
                return _filter;
            }
        }

        public static bool Filtering => Ticks.Count > 0;

        public static bool Ticked(string key) => Ticks.Contains(key);

        static string Key(string project) =>
            string.IsNullOrEmpty(project) ? NoProject : project;

        public static bool Passes(string project) =>
            !Filtering || Ticks.Contains(Key(project));

        // What the filter is, for a tooltip or an empty line: the name when it is one name,
        // and a count when it is more.
        public static string FilterLabel
        {
            get
            {
                if (Ticks.Count != 1) return Ticks.Count + " projects";
                foreach (var key in Ticks) return key;
                return "";
            }
        }

        // A blank key clears the filter outright: "all projects" is no filter at all rather
        // than every name ticked, so a project made later is in it too.
        public static void ToggleFilter(string key)
        {
            if (key.Length == 0)
            {
                if (!Filtering) return;
                Ticks.Clear();
            }
            else if (!Ticks.Remove(key)) Ticks.Add(key);

            var names = new List<string>(Ticks);
            names.Sort(System.StringComparer.Ordinal);
            var s = Settings.S;
            s.sidebarFilter = string.Join("\n", names.ToArray());
            s.Write();

            // The agents, files and shortcuts views read the filter as they draw. The other
            // two hold what they asked the daemon for, and a filter that widened is a
            // project they never asked about.
            if (Search) SearchView.Search();
            else if (Git) GitView.Refresh();
        }

        static void Show(string tab)
        {
            if (Settings.SidebarTab == tab) return;

            if (tab != TabFiles) FilesView.ClearFocus();
            if (tab != TabFiles) FilesView.ReleaseViewer();
            if (tab != TabSearch)
            {
                SearchView.ReleaseViewer();
                SearchView.ReleaseFocus();
            }
            if (tab != TabGit) GitView.ReleaseViewer();

            var s = Settings.S;
            s.sidebarTab = tab;
            s.Write();

            if (tab == TabGit || tab == TabFiles) GitView.Entered();

            if (tab == TabSearch) SearchView.Entered();

            if (tab == TabShortcuts) SessionHub.Instance.RefreshShortcuts();
        }

        static RowAct RoutedAction(SessionInfo info) => RowActions.Of(info);

        public static bool IsRouted(SessionInfo info)
        {
            RowAct act = RoutedAction(info);
            return (act & (RowAct.View | RowAct.Edit | RowAct.Diff)) != 0;
        }

        static bool InTab(SessionInfo info, string tab)
        {
            RowAct act = RoutedAction(info);
            return tab == TabFiles
                ? (act & (RowAct.View | RowAct.Edit)) != 0
                : tab == TabGit && (act & RowAct.Diff) != 0;
        }

        static List<SessionInfo> RoutedFor(string tab)
        {
            Routed.Clear();
            foreach (var info in SessionHub.Instance.Sessions)
                if (InTab(info, tab) && Passes(info.Project)) Routed.Add(info);
            Routed.Sort(ByName);
            return Routed;
        }

        public static float RoutedHeight(string tab) => RoutedFor(tab).Count * GhostH;

        public static Rect TreeBody(Rect body, string tab) =>
            new Rect(body.x, body.y + RoutedHeight(tab), body.width,
                Mathf.Max(0f, body.height - RoutedHeight(tab)));

        public static void DrawRouted(Rect body, string tab)
        {
            ViewRows.Clear();
            float y = body.y;
            foreach (var info in RoutedFor(tab))
            {
                var row = new Row
                {
                    Session = info.Name,
                    Ghost = true,
                    Line = new Rect(body.x, y, body.width, GhostH),
                    Text = new Rect(body.x + CellX + ArrowW + 4f, y + 1f,
                        body.width - CellX - ArrowW - 4f - Pad, NameH),
                    Face = Rect.zero,
                };
                ViewRows.Add(row);
                DrawRoutedRow(row, info);
                y += GhostH;
            }
        }

        static void DrawRoutedRow(Row row, SessionInfo info)
        {
            bool current = row.Session == TerminalWindow.CurrentName;
            if (current) Slab.Fill(row.Line, SlopWidgets.RowOn);
            else SlopWidgets.HoverRow(row.Line);

            var text = row.Text;
            var act = RoutedAction(info);
            if (act != RowAct.None)
            {
                float d = Mathf.Min(GhostMarkW, text.height);
                GUI.color = SlopWidgets.Off;
                GUI.DrawTexture(new Rect(text.x, text.y + (text.height - d) / 2f, d, d),
                    RowActions.Tex(act));
                text.x += d + 4f;
                text.width -= d + 4f;
            }

            Text.Font = GameFont.Small;
            GUI.color = SlopWidgets.Dim;
            SlopWidgets.RowLabel(text, Label(info) ?? row.Session);
            GUI.color = Color.white;
        }

        public static bool ClickRouted()
        {
            if (!ColonistBarStrip.Interactive) return false;
            var e = Event.current;
            if (e.rawType != EventType.MouseDown || (e.button != 0 && e.button != 1))
                return false;
            foreach (var row in ViewRows)
            {
                if (!ColonistBarStrip.MouseOver(row.Line)) continue;
                if (e.button == 1)
                {
                    RowMenu(row.Session);
                }
                else
                {
                    SessionSelectable.Current = row.Session;
                    var info = SessionHub.Instance.Get(row.Session);
                    if (info != null && info.Gone) SessionHub.Instance.Start(row.Session);
                    else TerminalWindow.Open(row.Session);
                }
                e.Use();
                return true;
            }
            return false;
        }

        public static void FocusTerminal() => Show(TabAgents);

        public static void ShowFiles() => Show(TabFiles);
        public static void ShowSearch() => Show(TabSearch);
        public static void ShowGit() => Show(TabGit);
        public static void ShowShortcuts() => Show(TabShortcuts);

        public static Rect AddBar =>
            new Rect(CellX, UI.screenHeight - Pad - AddH, Width - CellX * 2f, AddH);

        // Keep the visual strip inset, but let its hit target reach the panel's screen
        // edges so the bottom-left screen pixel still belongs to Add. Short of the grip on
        // the right: [Grip] runs first and takes the press with `Use`, which does not clear
        // `rawType` - the gate this press is read through - so an overlap would start a
        // resize and open the menu on the same click. [Tabs] holds the same line back.
        static Rect AddHitBar =>
            new Rect(0f, AddBar.y, Mathf.Max(0f, Width - GripW),
                UI.screenHeight - AddBar.y);

        public static Rect Body =>
            new Rect(0f, TabH, Width,
                Mathf.Max(0f, UI.screenHeight - TabH - AddH - Pad * 2f));

        public static List<string> Sessions()
        {
            var order = new List<string>();

            if (Agents)
            {
                foreach (var row in Rows)
                    if (row.Session != null && !row.Ghost) order.Add(row.Session);
                return order;
            }

            // Tree views have no Rows, but Alt+number must still return to an agent.
            foreach (var key in Order)
                foreach (int i in Buckets[key])
                    if (Named.TryGetValue(i, out var session) && session != null)
                        order.Add(session);
            return order;
        }

        public static List<string> WalkOrder()
        {
            var order = new List<string>();
            foreach (var row in Rows)
                if (row.Session != null) order.Add(row.Session);
            return order;
        }

        public static bool Drawing { get; private set; }

        public static bool FaceBox(Pawn pawn, out Rect box)
        {
            if (pawn != null)
                foreach (var row in Rows)
                    if (row.Pawn == pawn) { box = row.Face; return true; }

            box = Rect.zero;
            return false;
        }

        public static float Place(
            List<ColonistBar.Entry> entries, List<Vector2> locs, int count, bool plus)
        {
            Rows.Clear();
            Heads.Clear();
            Bucket(entries, locs, count);

            if (!Agents)
            {
                // Skipping entries would leave invisible vanilla hit targets over the tree.
                for (int i = 0; i < count && i < locs.Count; i++) locs[i] = Parked;
                return Nominal;
            }

            float top = TabH + Pad;
            float room = UI.screenHeight - top - Pad;

            int rows = 0;
            foreach (var key in Order)
                if (!Folded.Contains(key) && Buckets.TryGetValue(key, out var b))
                    rows += b.Count;

            float s = Fit(rows, Order.Count, plus, room, GhostRoom());
            float pitch = Pitch(s);
            float cell = ColonistBar.BaseSize.y * s;
            float face = ColonistBarColonistDrawer.PawnTextureSize.y * s;
            float rowH = Mathf.Max(face, TextH);

            float width = Width;
            float y = top;

            foreach (var g in TopGhosts) y = GhostRow(g, width, y);

            foreach (var key in Order)
            {
                var bucket = Buckets.TryGetValue(key, out var b) ? b : Empty;
                bool folded = Folded.Contains(key);
                var ghosts = Ghosts.TryGetValue(key, out var gs) ? gs : EmptyGhosts;

                Heads.Add(new Head
                {
                    Label = key,
                    Rect = new Rect(0f, y, width, HeadH),
                    Count = bucket.Count + ghosts.Count,
                    Folded = folded,
                });
                y += HeadH;

                if (folded)
                {
                    foreach (int i in bucket) locs[i] = Parked;
                    continue;
                }

                foreach (var g in ghosts) y = GhostRow(g, width, y);

                foreach (int i in bucket)
                {
                    locs[i] = new Vector2(CellX + (face - cell) / 2f, y + (rowH - cell) / 2f);

                    float tx = CellX + face + TextGap;
                    var line = new Rect(0f, y, width, rowH);
                    Rows.Add(new Row
                    {
                        Session = Session(entries[i].pawn),
                        Pawn = entries[i].pawn,
                        Line = line,
                        Text = new Rect(tx, y + (rowH - TextH) / 2f, width - tx - Pad, TextH),
                        Face = new Rect(CellX, y + (rowH - face) / 2f, face, face),
                    });

                    y += pitch;
                }
            }

            return s;
        }

        static float Pitch(float s) => Mathf.Max(
            (ColonistBar.BaseSize.y + ColonistBar.BaseSpaceBetweenColonistsVertical) * s,
            TextH + RowGap);

        static float Fit(int rows, int groups, bool plus, float room, float ghosts)
        {
            if (rows <= 0) return Nominal;
            float fixedH = groups * HeadH + (plus ? AddH + 2f : 0f) + ghosts;
            float each = ColonistBar.BaseSize.y + ColonistBar.BaseSpaceBetweenColonistsVertical;
            float s = (room - fixedH) / (rows * each);
            // Fonts do not shrink with portraits, so once the labels set the pitch there is
            // no height left to save. Min rather than Clamp: the two bounds are both read
            // off the same font now and would otherwise cross.
            float useful = Mathf.Min(Nominal, Mathf.Max(Floor, (TextH + RowGap) / each));
            return Mathf.Clamp(s, useful, Nominal);
        }

        static void Bucket(List<ColonistBar.Entry> entries, List<Vector2> locs, int count)
        {
            foreach (var list in Buckets.Values) list.Clear();
            foreach (var list in Ghosts.Values) list.Clear();
            Order.Clear();
            Named.Clear();
            TopGhosts.Clear();

            for (int i = 0; i < count && i < entries.Count; i++)
            {
                var pawn = entries[i].pawn;
                if (pawn == null)
                {
                    locs[i] = Parked;
                    continue;
                }

                var session = Session(pawn);
                var info = session == null ? null : SessionHub.Instance.Get(session);
                if (IsRouted(info))
                {
                    // Reconciliation may leave a routed permanent session's pawn for one tick.
                    locs[i] = Parked;
                    continue;
                }
                // Parked rather than skipped, for the reason the routed ones are: the
                // colonist bar hit-tests the same table it is drawn from.
                if (!Passes(info?.Project))
                {
                    locs[i] = Parked;
                    continue;
                }
                string key = string.IsNullOrEmpty(info?.Project) ? Loose : info.Project;

                if (!Buckets.TryGetValue(key, out var list))
                    Buckets[key] = list = new List<int>();
                list.Add(i);
                Named[i] = session;
            }

            foreach (var s in SessionHub.Instance.Sessions)
            {
                if (!s.Ephemeral || IsRouted(s) || !Passes(s.Project)) continue;
                if (string.IsNullOrEmpty(s.Project)) { TopGhosts.Add(s); continue; }

                if (!Ghosts.TryGetValue(s.Project, out var list))
                    Ghosts[s.Project] = list = new List<SessionInfo>();
                list.Add(s);
            }

            foreach (var kv in Buckets)
                if (kv.Value.Count > 0) Order.Add(kv.Key);
            foreach (var kv in Ghosts)
                if (kv.Value.Count > 0 && !Order.Contains(kv.Key)) Order.Add(kv.Key);

            Order.Sort((a, b) =>
                a == Loose ? (b == Loose ? 0 : 1)
                : b == Loose ? -1
                : string.CompareOrdinal(a, b));

            TopGhosts.Sort(ByName);
            foreach (var list in Ghosts.Values) list.Sort(ByName);
        }

        static readonly List<int> Empty = new List<int>();
        static readonly List<SessionInfo> EmptyGhosts = new List<SessionInfo>();

        static float GhostRow(SessionInfo s, float width, float y)
        {
            float tx = CellX + ArrowW + 4f;
            Rows.Add(new Row
            {
                Session = s.Name,
                Pawn = null,
                Ghost = true,
                Line = new Rect(0f, y, width, GhostH),
                Text = new Rect(tx, y + 1f, width - tx - Pad, NameH),
                Face = Rect.zero,
            });
            return y + GhostH;
        }

        static int ByName(SessionInfo a, SessionInfo b) =>
            string.CompareOrdinal(a?.Name ?? "", b?.Name ?? "");

        static float GhostRoom()
        {
            float h = TopGhosts.Count * GhostH;
            foreach (var kv in Ghosts)
                if (!Folded.Contains(kv.Key)) h += kv.Value.Count * GhostH;
            return h;
        }

        static string Session(Pawn pawn) =>
            pawn == null ? null : AgentColony.Current?.SessionOf(pawn);


        public static void DrawBack()
        {
            if (Event.current.type == EventType.Layout) return;

            if (ColonistBarStrip.Suppressed) return;

            if (!Wanted())
            {
                _resizing = false;
                return;
            }
            Drawing = true;

            // Menus and the grip run here, before vanilla consumes portrait clicks.
            var panel = Panel;
            Slab.Fill(panel, SlopWidgets.Panel);

            if (Files)
            {
                DrawRouted(Body, TabFiles);
                FilesView.Draw(TreeBody(Body, TabFiles));
            }
            else if (Search)
            {
                SearchView.Draw(Body);
            }
            else if (Git)
            {
                DrawRouted(Body, TabGit);
                GitView.Draw(TreeBody(Body, TabGit));
            }
            else if (Shortcuts)
            {
                ShortcutsView.Draw(Body);
            }
            else
            {
                string currentSession = SessionSelectable.Current;

                foreach (var row in Rows)
                {
                    bool current = row.Session != null && row.Session == currentSession;

                    if (current) Slab.Fill(row.Line, SlopWidgets.RowOn);
                    else SlopWidgets.HoverRow(row.Line);
                }

                foreach (var head in Heads) DrawHead(head);
            }

            Tabs();
            DrawAdd();

            Grip();
            if (AddClick()) return;
            if (Files)
            {
                if (!ClickRouted()) FilesView.Clicks();
            }
            else if (Search) SearchView.Clicks();
            else if (Git)
            {
                if (!ClickRouted()) GitView.Clicks();
            }
            else if (Shortcuts) ShortcutsView.Clicks();
            else Menus();
        }

        static void DrawAdd()
        {
            var r = AddBar;
            bool over = ColonistBarStrip.SidebarHover(r);

            if (over)
            {
                Slab.Fill(r, SlopWidgets.Hover);
                TooltipHandler.TipRegion(r, "Add a project, an agent or a shortcut");
            }
            Slab.Hairline(new Rect(r.x, r.y, r.width, 1f), SlopWidgets.Edge);

            float d = AddIcon;
            GUI.color = over ? Color.white : SlopWidgets.Lead;
            GUI.DrawTexture(
                new Rect(r.center.x - d / 2f, r.center.y - d / 2f, d, d), Icons.Add);
            GUI.color = Color.white;
        }

        static bool AddClick()
        {
            if (!ColonistBarStrip.Interactive) return false;

            var e = Event.current;
            if (e.rawType != EventType.MouseDown || e.button != 0) return false;
            if (!ColonistBarStrip.MouseOver(AddHitBar)) return false;

            e.Use();

            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Project...", () =>
                    TerminalWindow.OpenOverPane(new EditProjectDialog(null))),
                new FloatMenuOption("Agent...", () =>
                    TerminalWindow.OpenOverPane(new EditSessionDialog(null))),
                new FloatMenuOption("Shortcut...", () =>
                    TerminalWindow.OpenOverPane(new EditShortcutDialog(null))),
            };
            TerminalWindow.OpenOverPane(new SlopMenu(opts));
            return true;
        }

        static void Tabs()
        {
            var strip = new Rect(0f, 0f, Width, TabH);
            Slab.Hairline(new Rect(CellX, TabH - 1f, Width - CellX * 2f, 1f),
                SlopWidgets.Edge);

            float y = (TabRowH - TabIcon) / 2f;

            const float Gap = 3f;
            float x = CellX;
            Tab(new Rect(x, y, TabIcon, TabIcon), Icons.Agents, Agents,
                "Agents - every session, under the project it runs in", () => Show(TabAgents));
            x += TabIcon + Gap;
            Tab(new Rect(x, y, TabIcon, TabIcon), Icons.Files, Files,
                "Files - every project's directory, as a tree", () => Show(TabFiles));
            x += TabIcon + Gap;
            Tab(new Rect(x, y, TabIcon, TabIcon), Icons.Search, Search,
                "Search - find text across every project", () => Show(TabSearch));
            x += TabIcon + Gap;
            Tab(new Rect(x, y, TabIcon, TabIcon), Icons.Git, Git,
                "Git - what every working tree has that its last commit does not",
                () => Show(TabGit));
            x += TabIcon + Gap;
            Tab(new Rect(x, y, TabIcon, TabIcon), Icons.Shortcuts, Shortcuts,
                "Shortcuts - one-shot errands you can run against any project",
                () => Show(TabShortcuts));

            FilterButton();

            if (HasActions)
                Actions(new Rect(FilterRect.x, TabRowH + y, TabIcon, TabIcon));

            if (ColonistBarStrip.MouseOver(strip) && Event.current.rawType == EventType.MouseDown
                && ColonistBarStrip.Interactive
                && Event.current.mousePosition.x < Width - GripW)
                Event.current.Use();
        }

        // The buttons only the view up right now has, on their own row under the tabs and
        // right-aligned under the filter. A view without one leaves the row out entirely -
        // see [HasActions], which has to agree with what this draws.
        static void Actions(Rect r)
        {
            if (Files || Search)
            {
                bool showing = Settings.SidebarShowHidden;
                Tab(r, Icons.Hidden, showing,
                    showing
                        ? "Showing dotfiles. Click to hide them."
                        : "Hiding dotfiles. Click to show them.",
                    () =>
                    {
                        Settings.S.sidebarShowHidden = !showing;
                        Settings.S.Write();
                        if (Files) FilesView.Reload();
                        else SearchView.Search();
                    });
            }
            else if (Git)
            {
                Tab(r, Icons.Refresh, false,
                    "Read every working tree again.", GitView.Refresh);
            }
        }

        // Where the filter button is. One rect, so the menu comes out under the button
        // whether the button or the command palette opened it - and, being fixed rather
        // than taken from the mouse, so a menu that reopens itself after each tick reopens
        // in the place it was.
        static Rect FilterRect =>
            new Rect(Width - CellX - TabIcon, (TabRowH - TabIcon) / 2f, TabIcon, TabIcon);

        static void FilterButton()
        {
            Tab(FilterRect, Icons.Filter, Filtering,
                Filtering
                    ? $"Showing {FilterLabel}. Click to tick another, or all of them."
                    : "Every project. Click to show only some of them.",
                OpenFilterMenu);
        }

        // Ticks, not a pick: a tick closes the menu the way every option in one does, and
        // opens it again where it was, so several can be set without hunting the button
        // back down between them.
        public static void OpenFilterMenu()
        {
            var opts = new List<FloatMenuOption>
            {
                SlopWidgets.MenuToggle("All projects", !Filtering, () => Tick("")),
            };

            // Ordered the way every view orders its headings, so the menu and the column
            // under it read down in the same order - the loose one last.
            var names = new List<string>();
            foreach (var p in SessionHub.Instance.Projects) names.Add(p.Name);
            names.Sort(System.StringComparer.Ordinal);
            foreach (var name in names)
            {
                var key = name;
                opts.Add(SlopWidgets.MenuToggle(key, Ticked(key), () => Tick(key)));
            }

            opts.Add(SlopWidgets.MenuToggle(NoProject, Ticked(NoProject),
                () => Tick(NoProject)));

            TerminalWindow.OpenOverPane(
                new SlopMenu(opts, new Vector2(FilterRect.x, FilterRect.yMax)));
        }

        static void Tick(string key)
        {
            ToggleFilter(key);
            OpenFilterMenu();
        }

        static void Tab(Rect r, Texture2D icon, bool on, string tip, System.Action go)
        {
            TooltipHandler.TipRegion(r, tip);
            if (SlopWidgets.IconButton(r, icon, on ? SlopWidgets.Lead : SlopWidgets.Off)
                && ColonistBarStrip.Interactive)
                go();

            // The selected tab is marked by the same blue signal used for the current row,
            // at the foot of whichever of the strip's rows the button sits in.
            if (on)
                Slab.Fill(new Rect(r.x, (r.y < TabRowH ? TabRowH : TabH) - 2f, r.width, 2f),
                    SlopWidgets.Accent);
        }

        static void DrawHead(Head head)
        {
            var r = head.Rect;
            SlopWidgets.HoverRow(r);

            GUI.color = SlopWidgets.Faint;
            var arrow = new Rect(CellX, r.y + (HeadH - ArrowW) / 2f, ArrowW, ArrowW);
            GUI.DrawTexture(arrow, head.Folded ? TexButton.Reveal : TexButton.Collapse);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerLeft;

            float lx = arrow.xMax + 4f;
            string tail = head.Folded ? "  " + head.Count : "";
            var label = new Rect(lx, r.y, r.width - lx - CellX, HeadH);
            SlopWidgets.RowLabel(label, head.Label + tail);

            Slab.Hairline(new Rect(CellX, r.yMax - 1f, r.width - CellX * 2f, 1f),
                SlopWidgets.Edge);

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            var p = SessionHub.Instance.Project(head.Label);
            TooltipHandler.TipRegion(r, p == null
                ? "Agents whose project has gone, and anyone here who is not an agent.\n\n" +
                  "Click to fold."
                : $"{p.Dir}\n({ProjectsView.Summary(p)})\n\n" +
                  "Click to fold, right-click for the project.");
        }

        public static void DrawFront()
        {
            if (!Drawing) return;
            try
            {
                var hub = SessionHub.Instance;
                foreach (var row in Rows)
                {
                    var info = row.Session == null ? null : hub.Get(row.Session);
                    var state = info?.State ?? AgentState.Down;
                    var tint = TerminalWindow.StateColor(state);

                    if (row.Ghost)
                    {
                        Text.Font = GameFont.Small;
                        var text = row.Text;

                        var act = RowActions.Of(info);
                        if (act != RowAct.None)
                        {
                            float d = Mathf.Min(GhostMarkW, text.height);
                            GUI.color = SlopWidgets.Off;
                            GUI.DrawTexture(
                                new Rect(text.x, text.y + (text.height - d) / 2f, d, d),
                                RowActions.Tex(act));
                            text.x += d + 4f;
                            text.width -= d + 4f;
                        }

                        GUI.color = SlopWidgets.Dim;
                        SlopWidgets.RowLabel(text, Label(info) ?? row.Session);
                        GUI.color = Color.white;
                        Click(row, info);
                        continue;
                    }

                    DrawStateBadge(row.Face, state);

                    Text.Font = GameFont.Small;
                    var name = new Rect(row.Text.x, row.Text.y, row.Text.width, NameH);
                    if (info != null && info.Bell)
                    {
                        float d = Mathf.Min(BellW, NameH);
                        GUI.color = SlopWidgets.Warn;
                        GUI.DrawTexture(
                            new Rect(name.xMax - d, name.y + (NameH - d) / 2f, d, d),
                            Icons.Bell);
                        name.width -= d + 3f;
                    }
                    GUI.color = tint;
                    SlopWidgets.RowLabel(name, row.Session ?? row.Pawn?.LabelShort ?? "?");

                    Text.Font = GameFont.Tiny;
                    var word = new Rect(row.Text.x, row.Text.y + NameH, row.Text.width, SubH);
                    string ago = state == AgentState.Down ? "" : Ago(info);
                    if (ago.Length > 0)
                    {
                        Text.Anchor = TextAnchor.UpperRight;
                        GUI.color = SlopWidgets.Faint;
                        Widgets.Label(word, ago);
                        Text.Anchor = TextAnchor.UpperLeft;
                        word.width -= Mathf.Ceil(SlopWidgets.Wide(ago)) + AgoGap;
                    }
                    GUI.color = SlopWidgets.Dim;
                    SlopWidgets.RowLabel(word, Word(state));

                    string title = Title(info);
                    GUI.color = title.Length > 0 ? SlopWidgets.Dim : SlopWidgets.Off;
                    if (title.Length == 0) title = Ground(info);
                    var line3 = new Rect(row.Text.x, row.Text.y + NameH + SubH,
                        row.Text.width, SubH);
                    SlopWidgets.RowLabel(line3, title);
                    if (title.Length > 0 && SlopWidgets.Wide(title) > line3.width)
                        TooltipHandler.TipRegion(line3, title);

                    GUI.color = Color.white;
                    Click(row, info);
                }

                Absorb();
            }
            finally
            {
                // This cleanup must also happen when label drawing throws.
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                Drawing = false;
            }
        }

        static string Word(AgentState state) => state.ToString().ToLower();

        // Presence, the way a chat client marks it: a circle in the lower corner of the
        // portrait, which is the one round thing in a rectangular UI because it sits on a
        // face and is read as one. The front pass runs after the colonist bar has put the
        // portrait down, which is what lets the badge land on top of it. The ring is the
        // panel's own dark, so the circle keeps an edge over hair and clothing.
        //
        // The corner is the drawn portrait's, not the cell's: hair and clothing overflow
        // the face box by a third of it at each end, and the box's own lower edge is level
        // with the pawn's chin - which is where the badge sat, over the mouth.
        static void DrawStateBadge(Rect face, AgentState state)
        {
            if (face.width <= 0f) return;

            float d = Mathf.Max(SlopWidgets.StatusMarker,
                Mathf.Round(face.width * BadgeShare));
            var portrait = Patch_SidebarPortraitDraw.PortraitRect(face);
            var center = new Vector2(portrait.xMax - d / 2f - BadgeInset,
                portrait.yMax - d / 2f - BadgeInset);

            GUI.color = SlopWidgets.ViewBg;
            GUI.DrawTexture(Icons.DotBox(center, d + BadgeRing * 2f), Icons.Dot);
            GUI.color = TerminalWindow.StateColor(state);
            GUI.DrawTexture(Icons.DotBox(center, d), Icons.Dot);
            GUI.color = Color.white;
        }

        static string Ago(SessionInfo info)
        {
            if (info == null || info.StateSince <= 0) return "";
            long s = (SessionInfo.NowMs - info.StateSince) / 1000L;
            if (s < 0L) return "";
            if (s < 60L) return "<1m";
            if (s < 3600L) return s / 60L + "m";
            if (s < 86400L) return s / 3600L + "h";
            return s / 86400L + "d";
        }

        static string Label(SessionInfo info)
        {
            string t = Title(info);
            return t.Length > 0 ? t : info?.Name;
        }

        // A title is whatever the app in the pane set, and a coding agent puts a sigil in
        // front of its own. The game's font has no glyph for those: Unity draws the
        // character's width and no ink, so the line begins with an indent that nothing in
        // the layout accounts for and nothing on the screen explains. Asking the font is
        // the only way to know - the set it carries is not a range anyone can name here.
        // Control characters are already the daemon's business (tmux.rs, clean_title).
        static string Title(SessionInfo info)
        {
            if (info == null) return "";
            var t = info.Title ?? "";
            var font = Text.CurFontStyle?.font;
            var clean = new System.Text.StringBuilder(t.Length);
            foreach (char c in t)
                clean.Append(char.IsControl(c) || (font != null && !font.HasCharacter(c))
                    ? ' '
                    : c);
            return clean.ToString().Trim();
        }

        static string Ground(SessionInfo info)
        {
            if (info == null) return "";
            return info.Ephemeral ? "temporary" : Leaf(info.Dir);
        }

        static string Leaf(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return "";
            string trimmed = dir.TrimEnd('/');
            int cut = trimmed.LastIndexOf('/');
            return cut < 0 ? trimmed : trimmed.Substring(cut + 1);
        }

        static void Click(Row row, SessionInfo info)
        {
            if (row.Session == null || !ColonistBarStrip.Interactive) return;
            if (!Widgets.ButtonInvisible(row.Text, false)) return;

            SessionSelectable.Current = row.Session;

            if (!ColonistBarStrip.Drawing)
            {
                Find.Selector.ClearSelection();
                if (row.Pawn == null) return;
                CameraJumper.TryJumpAndSelect(row.Pawn);
                return;
            }

            if (info != null && info.Gone) SessionHub.Instance.Start(row.Session);
            else if (row.Session != TerminalWindow.CurrentName) TerminalWindow.Open(row.Session);
        }

        // Called by the Harmony finalizer when vanilla prevents the normal front pass.
        public static void EndDraw() => Drawing = false;


        static void Menus()
        {
            if (!ColonistBarStrip.Interactive) return;

            var e = Event.current;
            if (e.rawType != EventType.MouseDown) return;
            if (e.button != 0 && e.button != 1) return;

            foreach (var head in Heads)
            {
                if (!ColonistBarStrip.MouseOver(head.Rect)) continue;
                if (e.button == 0) Fold(head.Label, !head.Folded);
                else HeadMenu(head);
                e.Use();
                return;
            }

            if (e.button != 1) return;
            foreach (var row in Rows)
            {
                if (row.Session == null) continue;
                if (!ColonistBarStrip.MouseOver(row.Line)) continue;
                RowMenu(row.Session, row.Pawn);
                e.Use();
                return;
            }
        }

        static void RowMenu(string name, Pawn pawn = null)
        {
            var hub = SessionHub.Instance;
            var info = hub.Get(name);
            var opts = new List<FloatMenuOption>();

            bool alive = info != null && info.Alive;
            opts.Add(new FloatMenuOption(alive ? "Stop" : "Start", () =>
            {
                if (alive) hub.Stop(name, SlopWidgets.Fail);
                else hub.Start(name, SlopWidgets.Fail);
            }));

            var term = new FloatMenuOption("Terminal", () => TerminalWindow.Open(name));
            term.Disabled = !alive;
            opts.Add(term);

            if (info != null && !info.Ephemeral)
                opts.Add(new FloatMenuOption("Edit...", () =>
                    TerminalWindow.OpenOverPane(new EditSessionDialog(info))));

            if (info != null && !string.IsNullOrEmpty(info.Project))
                opts.Add(new FloatMenuOption("Duplicate...", () =>
                    TerminalWindow.OpenOverPane(EditSessionDialog.Copy(info))));

            if (info != null && !info.Ephemeral)
                opts.Add(new FloatMenuOption("Remove", () =>
                    TerminalWindow.OpenOverPane(Dialog_MessageBox.CreateConfirmation(
                        $"Remove session '{name}'? This kills it, drops it from config.toml, and moves " +
                        "its private state to recoverable trash for 14 days.",
                        () => hub.Remove(name, SlopWidgets.Fail),
                        destructive: true))));

            // Unlike the core's colony-wide reroll, a row owns one particular agent.
            if (pawn != null)
                opts.Add(new FloatMenuOption("New look", () =>
                {
                    if (!pawn.Destroyed) AgentLook.Reroll(pawn);
                }));

            TerminalWindow.OpenOverPane(new SlopMenu(opts));
        }

        static void HeadMenu(Head head)
        {
            var hub = SessionHub.Instance;
            var p = hub.Project(head.Label);
            if (p == null) return;

            string name = p.Name;
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Edit...", () =>
                    TerminalWindow.OpenOverPane(new EditProjectDialog(p))),
                new FloatMenuOption("Duplicate...", () =>
                    TerminalWindow.OpenOverPane(EditProjectDialog.Copy(p))),
                new FloatMenuOption("Terminal (host)", () =>
                    hub.RunHostShell(name, session => TerminalWindow.Open(session),
                        SlopWidgets.Fail)),
            };

            int agents = 0;
            foreach (var s in hub.Sessions)
                if (s.Project == name) agents++;

            var del = new FloatMenuOption(
                agents > 0 ? $"Delete ({agents} agent{(agents == 1 ? "" : "s")} in it)" : "Delete",
                () => TerminalWindow.OpenOverPane(Dialog_MessageBox.CreateConfirmation(
                    $"Remove project '{name}'? The directory is left alone; only the entry " +
                    "in config.toml goes.",
                    () => hub.RemoveProject(name, SlopWidgets.Fail),
                    destructive: true)));
            del.Disabled = agents > 0;
            opts.Add(del);

            TerminalWindow.OpenOverPane(new SlopMenu(opts));
        }



        static bool _resizing;
        static float _grab;

        static void Grip()
        {
            float w = Width;
            var grip = new Rect(w - GripW, 0f, GripW * 2f, UI.screenHeight);
            if (!ColonistBarStrip.Interactive && _resizing)
            {
                _resizing = false;
                Settings.S.Write();
            }

            bool over = ColonistBarStrip.SidebarHover(grip);
            bool lit = over || _resizing;

            // The panel's right edge and the grip's own tell are the same line, and it is
            // drawn here alone: a second draw of [SlopWidgets.Edge] over this one composites
            // into a heavier boundary than the palette's, on the sidebar only. At rest it is
            // one screen pixel like every other rule; lit it is a bar and may be a GUI one.
            if (lit) Slab.Fill(new Rect(w - 1f, 0f, 2f, UI.screenHeight), SlopWidgets.EdgeLit);
            else Slab.VHairline(new Rect(w - 1f, 0f, 1f, UI.screenHeight), SlopWidgets.Edge);

            if (!ColonistBarStrip.Interactive) return;

            var e = Event.current;

            // Poll Input: an absorbing window can prevent this layer from receiving MouseDown.
            if (!_resizing)
            {
                if (!over || !Input.GetMouseButtonDown(0)) return;
                _resizing = true;
                _grab = w - e.mousePosition.x;
            }
            else if (Input.GetMouseButton(0))
            {
                SetWidth(e.mousePosition.x + _grab);
            }
            else
            {
                _resizing = false;
                Settings.S.Write();
            }

            if (e.rawType == EventType.MouseDown || e.rawType == EventType.MouseUp
                || e.rawType == EventType.MouseDrag)
                e.Use();
        }

        static void SetWidth(float w)
        {
            Settings.S.sidebarWidth = Mathf.Clamp(w, MinWidth, MaxWidth);
            Patch_MainTabWindowShift.Reposition();
        }

        static void Absorb()
        {
            if (!ColonistBarStrip.Interactive) return;

            var e = Event.current;
            if (e.rawType != EventType.MouseDown) return;
            if (!ColonistBarStrip.MouseOver(Panel)) return;
            e.Use();
        }

        static bool Wanted()
        {
            return ColonistBarStrip.BarShown && !Cutscene.Playing;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(GenMapUI), nameof(GenMapUI.DrawPawnLabel),
        new[] { typeof(Pawn), typeof(Vector2), typeof(float), typeof(float),
                typeof(Dictionary<string, string>), typeof(GameFont), typeof(bool),
                typeof(bool) })]
    public static class Patch_SidebarPawnLabel
    {
        static bool Prefix() => !AgentSidebar.Drawing;
    }
}
