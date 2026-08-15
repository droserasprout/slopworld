using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Mechanical split: AgentSidebar.Rendering methods.
    public static partial class AgentSidebar
    {
        public static void DrawRouted(Rect body, SidebarTab tab)
        {
            Layout.ViewRows.Clear();
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
                Layout.ViewRows.Add(row);
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
            DrawGhostLabel(text, info, row.Session);
            GUI.color = Color.white;
        }

        public static bool ClickRouted()
        {
            if (!ColonistBarStrip.Interactive) return false;
            var e = Event.current;
            if (e.rawType != EventType.MouseDown || (e.button != 0 && e.button != 1))
                return false;
            foreach (var row in Layout.ViewRows)
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

        public static void FocusTerminal() => Show(SidebarTab.Agents);

        public static void ShowFiles() => Show(SidebarTab.Files);
        public static void ShowSearch() => Show(SidebarTab.Search);
        public static void ShowGit() => Show(SidebarTab.Git);
        public static void ShowShortcuts() => Show(SidebarTab.Shortcuts);

        // The visual rect fills the panel while its hit rect stops before Grip so one consumed click cannot resize and open the menu.
        public static Rect AddBar
        {
            get
            {
                float y = UI.screenHeight - AddH;
                return new Rect(0f, y, Width, AddH);
            }
        }

        static Rect AddHitBar =>
            new Rect(AddBar.x, AddBar.y, Mathf.Max(0f, Width - GripW), AddBar.height);

        public static Rect Body =>
            new Rect(0f, TabH, Width,
                Mathf.Max(0f, UI.screenHeight - TabH - AddH));

        public static List<string> Sessions()
        {
            var order = new List<string>();

            if (CurrentTab == SidebarTab.Agents)
            {
                foreach (var row in Layout.Rows)
                    if (row.Session != null && !row.Ghost) order.Add(row.Session);
                return order;
            }

            // Tree views have no Rows, but Alt+number must still return to an agent.
            foreach (var key in Layout.Order)
                foreach (int i in Layout.Buckets[key])
                    if (Layout.Named.TryGetValue(i, out var session) && session != null)
                        order.Add(session);
            return order;
        }

        public static List<string> WalkOrder()
        {
            var order = new List<string>();
            foreach (var row in Layout.Rows)
                if (row.Session != null) order.Add(row.Session);
            return order;
        }

        public static bool Drawing { get; private set; }

        public static bool AgentScrollOpen => Layout.AgentScrollOpen;

        public static bool FaceBox(Pawn pawn, out Rect box)
        {
            if (pawn != null)
                foreach (var row in Layout.Rows)
                    if (row.Pawn == pawn) { box = row.Face; return true; }

            box = Rect.zero;
            return false;
        }

        public static float Place(
            List<ColonistBar.Entry> entries, List<Vector2> locs, int count, bool plus)
        {
            Layout.BeginFrame();
            Bucket(entries, locs, count);

            if (CurrentTab != SidebarTab.Agents)
            {
                // Skipping entries would leave invisible vanilla hit targets over the tree.
                for (int i = 0; i < count && i < locs.Count; i++) locs[i] = Parked;
                return Nominal;
            }

            // Agent rows are content coordinates: DrawBack/DrawFront put them inside the
            // shared scroll view, whose screen origin is Body. The chrome below is still
            // screen-fixed and never enters this coordinate space.
            float top = Pad;
            float room = Body.height;

            int rows = 0;
            foreach (var key in Layout.Order)
                if (!Folded.Contains(key) && Layout.Buckets.TryGetValue(key, out var b))
                    rows += b.Count;

            // Add is outside this viewport now, so the body already accounts for its height.
            float s = Fit(rows, Layout.Order.Count, false, room, GhostRoom());
            float pitch = Pitch(s);
            float cell = ColonistBar.BaseSize.y * s;
            float face = ColonistBarColonistDrawer.PawnTextureSize.y * s;
            float rowH = Mathf.Max(face, TextH);

            float width = Width;
            float y = top;

            foreach (var g in Layout.TopGhosts) y = GhostRow(g, width, y);

            foreach (var key in Layout.Order)
            {
                var bucket = Layout.Buckets.TryGetValue(key, out var b) ? b : Empty;
                bool folded = Folded.Contains(key);
                var ghosts = Layout.Ghosts.TryGetValue(key, out var gs) ? gs : EmptyGhosts;

                Layout.Heads.Add(new Head
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
                    locs[i] = new Vector2(PortraitX + (face - cell) / 2f,
                        y + (rowH - cell) / 2f);

                    // Keep the three text lines at the same gap from the portrait after the
                    // portrait column moves to the screen edge; headings and chrome retain
                    // their CellX inset.
                    float tx = PortraitX + face + TextGap;
                    var line = new Rect(0f, y, width, rowH);
                    Layout.Rows.Add(new Row
                    {
                        Session = Session(entries[i].pawn),
                        Pawn = entries[i].pawn,
                        Line = line,
                        Text = new Rect(tx, y + (rowH - TextH) / 2f, width - tx - Pad, TextH),
                        Face = new Rect(PortraitX, y + (rowH - face) / 2f, face, face),
                    });

                    y += pitch;
                }
            }

            Layout.AgentContentH = Mathf.Max(Body.height, y + Pad);

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

                if (!Layout.Buckets.TryGetValue(key, out var list))
                    Layout.Buckets[key] = list = new List<int>();
                list.Add(i);
                Layout.Named[i] = session;
            }

            foreach (var s in SessionHub.Instance.Sessions)
            {
                if (!s.Ephemeral || IsRouted(s) || !Passes(s.Project)) continue;
                if (string.IsNullOrEmpty(s.Project)) { Layout.TopGhosts.Add(s); continue; }

                if (!Layout.Ghosts.TryGetValue(s.Project, out var list))
                    Layout.Ghosts[s.Project] = list = new List<SessionInfo>();
                list.Add(s);
            }

            foreach (var kv in Layout.Buckets)
                if (kv.Value.Count > 0) Layout.Order.Add(kv.Key);
            foreach (var kv in Layout.Ghosts)
                if (kv.Value.Count > 0 && !Layout.Order.Contains(kv.Key)) Layout.Order.Add(kv.Key);

            Layout.Order.Sort((a, b) =>
                a == Loose ? (b == Loose ? 0 : 1)
                : b == Loose ? -1
                : string.CompareOrdinal(a, b));

            Layout.TopGhosts.Sort(ByName);
            foreach (var list in Layout.Ghosts.Values) list.Sort(ByName);
        }

        static readonly List<int> Empty = new List<int>();
        static readonly List<SessionInfo> EmptyGhosts = new List<SessionInfo>();

        static float GhostRow(SessionInfo s, float width, float y)
        {
            float tx = CellX + ArrowW + 4f;
            Layout.Rows.Add(new Row
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
            float h = Layout.TopGhosts.Count * GhostH;
            foreach (var kv in Layout.Ghosts)
                if (!Folded.Contains(kv.Key)) h += kv.Value.Count * GhostH;
            return h;
        }

        static string Session(Pawn pawn) =>
            pawn == null ? null : AgentColony.Current?.SessionOf(pawn);

        static void BeginAgentScroll()
        {
            var body = Body;
            // The full-width view keeps SmoothScroll active while omitting the gutter that narrows every row.
            var view = new Rect(0f, 0f, body.width, Layout.AgentContentH);
            Layout.AgentScroll.Begin(body, view, false);
            Layout.AgentScrollOpen = true;
        }

        static void EndAgentScroll()
        {
            if (!Layout.AgentScrollOpen) return;
            Layout.AgentScrollOpen = false;
            Layout.AgentScroll.End();
        }

        static void DrawChromeAndClicks()
        {
            Tabs();
            DrawAdd();

            Grip();
            if (AddClick()) return;
            switch (CurrentTab)
            {
                case SidebarTab.Files:
                    if (!ClickRouted()) FilesView.Clicks();
                    break;
                case SidebarTab.Search:
                    SearchView.Clicks();
                    break;
                case SidebarTab.Git:
                    if (!ClickRouted()) GitView.Clicks();
                    break;
                case SidebarTab.Shortcuts:
                    ShortcutsView.Clicks();
                    break;
                default:
                    Menus();
                    break;
            }
            Absorb();
        }

        static void DrawAgentTab()
        {
            Tabs();
            DrawAdd();
            Grip();
            AddClick();

            BeginAgentScroll();

            string currentSession = SessionSelectable.Current;
            foreach (var row in Layout.Rows)
            {
                bool current = row.Session != null && row.Session == currentSession;

                if (current) Slab.Fill(row.Line, SlopWidgets.RowOn);
                else SlopWidgets.HoverRow(row.Line);
            }

            foreach (var head in Layout.Heads) DrawHead(head);
            // This must precede vanilla: its portrait handler consumes right-clicks.
            // The scroll group also lets Menus use the content-local row geometry
            // directly, just like portrait and label hit testing.
            Menus();
        }

        public static void DrawBack()
        {
            if (Event.current.type == EventType.Layout) return;

            if (ColonistBarStrip.Suppressed) return;

            if (!Wanted())
            {
                Layout.Resizing = false;
                return;
            }
            Drawing = true;
            Patch_SidebarPortraitDraw.ClearDeferredSelection();

            // The panel, fixed chrome and menus run here, before vanilla consumes input.
            // Only the agent body remains grouped around vanilla's portrait pass.
            var panel = Panel;
            Slab.Fill(panel, SlopWidgets.Panel);

            switch (CurrentTab)
            {
                case SidebarTab.Agents:
                    DrawAgentTab();
                    return;
                case SidebarTab.Files:
                    DrawRouted(Body, SidebarTab.Files);
                    FilesView.Draw(TreeBody(Body, SidebarTab.Files));
                    break;
                case SidebarTab.Search:
                    SearchView.Draw(Body);
                    break;
                case SidebarTab.Git:
                    DrawRouted(Body, SidebarTab.Git);
                    GitView.Draw(TreeBody(Body, SidebarTab.Git));
                    break;
                case SidebarTab.Shortcuts:
                    ShortcutsView.Draw(Body);
                    break;
            }

            DrawChromeAndClicks();
        }

        static void DrawAdd()
        {
            var r = AddBar;
            // Keep the button lit while its menu is stacked over the pane. The menu owns the
            // press, but the pointer is still visibly over the control that opened it.
            bool over = ColonistBarStrip.MouseOver(AddHitBar);

            Slab.Fill(r, over ? SlopWidgets.Hover : SlopWidgets.Panel);
            TooltipHandler.TipRegion(r,
                "Add a project, an agent, a shortcut, a sandbox preset, a command or a host shell");
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
                new SlopSubmenu("Shortcuts", ShortcutOptions),
                new FloatMenuOption("Sandbox preset...", SlopOptions.OpenNewSandboxPreset),
                new FloatMenuOption("Command...", SlopOptions.OpenNewCommand),
                new SlopSubmenu("Host shell", HostShellOptions),
            };
            TerminalWindow.OpenOverPane(new SlopMenu(opts));
            return true;
        }

        static List<FloatMenuOption> ShortcutOptions() => new List<FloatMenuOption>
        {
            new FloatMenuOption("Prompt...", () =>
                TerminalWindow.OpenOverPane(new EditShortcutDialog(ShortcutKind.Prompt))),
            new FloatMenuOption("Breadcrumb...", () =>
                TerminalWindow.OpenOverPane(new EditShortcutDialog(ShortcutKind.Breadcrumb))),
            new FloatMenuOption("Shell...", () =>
                TerminalWindow.OpenOverPane(new EditShortcutDialog(ShortcutKind.Shell))),
            new FloatMenuOption("File Action...", () =>
                TerminalWindow.OpenOverPane(new EditShortcutDialog(ShortcutKind.FileAction))),
        };

        static List<FloatMenuOption> HostShellOptions()
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("~", () =>
                    SessionHub.Instance.RunHostShell("",
                        session => TerminalWindow.Open(session), SlopWidgets.Fail)),
            };
            foreach (var p in SessionHub.Instance.Projects)
            {
                string name = p.Name;
                options.Add(new FloatMenuOption($"{name}  -  {p.Dir}", () =>
                    SessionHub.Instance.RunHostShell(name,
                        session => TerminalWindow.Open(session), SlopWidgets.Fail)));
            }
            return options;
        }

        static void Tabs()
        {
            var strip = new Rect(0f, 0f, Width, TabH);
            Slab.Hairline(new Rect(CellX, TabH - 1f, Width - CellX * 2f, 1f),
                SlopWidgets.Edge);

            float y = (TabRowH - TabIcon) / 2f;

            const float Gap = 3f;
            float x = CellX;
            Tab(new Rect(x, y, TabIcon, TabIcon), Icons.Agents,
                CurrentTab == SidebarTab.Agents,
                "Agents - every session, under the project it runs in",
                () => Show(SidebarTab.Agents));
            x += TabIcon + Gap;
            Tab(new Rect(x, y, TabIcon, TabIcon), Icons.Files,
                CurrentTab == SidebarTab.Files,
                "Files - every project's directory, as a tree",
                () => Show(SidebarTab.Files));
            x += TabIcon + Gap;
            Tab(new Rect(x, y, TabIcon, TabIcon), Icons.Search,
                CurrentTab == SidebarTab.Search,
                "Search - find text across every project",
                () => Show(SidebarTab.Search));
            x += TabIcon + Gap;
            Tab(new Rect(x, y, TabIcon, TabIcon), Icons.Git,
                CurrentTab == SidebarTab.Git,
                "Git - what every working tree has that its last commit does not",
                () => Show(SidebarTab.Git));
            x += TabIcon + Gap;
            Tab(new Rect(x, y, TabIcon, TabIcon), Icons.Shortcuts,
                CurrentTab == SidebarTab.Shortcuts,
                "Shortcuts - one-shot errands you can run against any project",
                () => Show(SidebarTab.Shortcuts));

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
            if (CurrentTab == SidebarTab.Files || CurrentTab == SidebarTab.Search)
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
                        if (CurrentTab == SidebarTab.Files) FilesView.Reload();
                        else SearchView.Search();
                    });
            }
            else if (CurrentTab == SidebarTab.Git)
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

        static void DrawRows()
        {
            var hub = SessionHub.Instance;
            foreach (var row in Layout.Rows)
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

                    DrawGhostLabel(text, info, row.Session, hostIcon: true);
                    GUI.color = Color.white;
                    Click(row, info);
                    continue;
                }

                DrawStateBadge(row.Face, row.Text, state);

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
                // The summary gets the upper secondary line. Down or fresh agents have no
                // useful summary yet, so it remains empty without falling back to the ground.
                if (state != AgentState.Down)
                {
                    string title = Title(info);
                    if (title.Length > 0)
                    {
                        GUI.color = SlopWidgets.Dim;
                        var line2 = new Rect(row.Text.x, row.Text.y + NameH,
                            row.Text.width, SubH);
                        SlopWidgets.RowLabel(line2, title);
                        if (SlopWidgets.Wide(title) > line2.width)
                            TooltipHandler.TipRegion(line2, title);
                    }
                }

                var word = new Rect(row.Text.x, row.Text.y + NameH + SubH,
                    row.Text.width, SubH);
                string ago = state == AgentState.Down ? "" : Ago(info);
                if (ago.Length > 0)
                {
                    GUI.color = SlopWidgets.Faint;
                    SlopWidgets.RowLabel(word, ago, TextAnchor.MiddleRight);
                    word.width -= Mathf.Ceil(SlopWidgets.Wide(ago)) + AgoGap;
                }
                GUI.color = SlopWidgets.Faint;
                SlopWidgets.RowLabel(word, Word(state));

                GUI.color = Color.white;
                Click(row, info);
            }
        }

        public static void DrawFront()
        {
            if (!Drawing) return;
            try
            {
                if (Layout.AgentScrollOpen)
                {
                    DrawRows();
                    Patch_SidebarPortraitDraw.DrawDeferredSelection();
                    EndAgentScroll();
                    DrawAgentShadow();
                    Absorb();
                    return;
                }

                DrawRows();

                Absorb();
            }
            finally
            {
                EndAgentScroll();
                // This cleanup must also happen when label drawing throws.
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                Drawing = false;
            }
        }

        // A soft edge marks the fixed add strip over the scrolling body without reserving a scrollbar gutter.
    }
}
