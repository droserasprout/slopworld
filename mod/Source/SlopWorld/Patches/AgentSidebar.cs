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

        const float Nominal = 0.5f;
        const float Floor = 0.3f;

        static float HeadH => SlopWidgets.TinyRowH;
        static float AddH => TopBar.H;

        const float AddIcon = SlopWidgets.IconW;
        const float Pad = SlopWidgets.GapS;
        const float CellX = SlopWidgets.GapS;
        const float TextGap = SlopWidgets.GapS;

        public static float TabH => TopBar.H;
        const float TabIcon = 20f;

        const float RowGap = SlopWidgets.GapXS;

        static float GhostH => NameH + 2f;

        static float NameH => SlopWidgets.LineHOf(GameFont.Small);
        static float SubH => SlopWidgets.TinyH;

        static float TextH => NameH + SubH * 2f;

        const float BellW = 13f;
        const float AgoGap = 6f;

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

        static void Show(string tab)
        {
            if (Settings.SidebarTab == tab) return;

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
                if (InTab(info, tab)) Routed.Add(info);
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
            // Fonts do not scale with portraits; shrinking below this point saves no height.
            float useful = Mathf.Clamp((TextH + RowGap) / each, Floor, Nominal);
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
                string key = string.IsNullOrEmpty(info?.Project) ? Loose : info.Project;

                if (!Buckets.TryGetValue(key, out var list))
                    Buckets[key] = list = new List<int>();
                list.Add(i);
                Named[i] = session;
            }

            foreach (var s in SessionHub.Instance.Sessions)
            {
                if (!s.Ephemeral || IsRouted(s)) continue;
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
            Slab.VHairline(new Rect(panel.xMax - 1f, panel.y, 1f, panel.height),
                SlopWidgets.Edge);

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
            if (!ColonistBarStrip.MouseOver(AddBar)) return false;

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

            float y = (TabH - TabIcon) / 2f;

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

            float right = Width - CellX - TabIcon;

            if (Files || Search)
            {
                bool showing = Settings.SidebarShowHidden;
                Tab(new Rect(right, y, TabIcon, TabIcon), Icons.Hidden, showing,
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
                Tab(new Rect(right, y, TabIcon, TabIcon), Icons.Refresh, false,
                    "Read every working tree again.", GitView.Refresh);
            }


            if (ColonistBarStrip.MouseOver(strip) && Event.current.rawType == EventType.MouseDown
                && ColonistBarStrip.Interactive
                && Event.current.mousePosition.x < Width - GripW)
                Event.current.Use();
        }

        static void Tab(Rect r, Texture2D icon, bool on, string tip, System.Action go)
        {
            TooltipHandler.TipRegion(r, tip);
            if (SlopWidgets.IconButton(r, icon, on ? SlopWidgets.Lead : SlopWidgets.Off)
                && ColonistBarStrip.Interactive)
                go();

            // The selected tab is marked by the same blue signal used for the current row.
            if (on)
                Slab.Fill(new Rect(r.x, TabH - 2f, r.width, 2f), SlopWidgets.Accent);
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

        static string Title(SessionInfo info)
        {
            if (info == null) return "";
            var t = info.Title ?? "";
            var clean = new System.Text.StringBuilder(t.Length);
            foreach (char c in t)
                clean.Append(char.IsControl(c) ? ' ' : c);
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

            Slab.Fill(new Rect(w - 1f, 0f, lit ? 2f : 1f, UI.screenHeight),
                lit ? SlopWidgets.EdgeLit : SlopWidgets.Edge);

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
