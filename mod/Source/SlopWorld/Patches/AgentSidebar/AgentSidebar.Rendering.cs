using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Mechanical split: AgentSidebar.Rendering methods.
    public static partial class AgentSidebar
    {
        public static void FocusTerminal() => Show(SidebarTab.Agents);

        public static void ShowAgents() => Show(SidebarTab.Agents);
        public static void ShowFiles() => Show(SidebarTab.Files);
        public static void ShowSearch() => Show(SidebarTab.Search);
        public static void ShowGit() => Show(SidebarTab.Git);
        public static void ShowLibrary() => Show(SidebarTab.Library);
        public static void ShowTasks() => Show(SidebarTab.Tasks);

        public static bool CanFoldCurrent => CurrentTab != SidebarTab.Search &&
            CurrentTab != SidebarTab.Tasks;
        public static bool CurrentViewAllFolded => AllFolded();
        public static bool CanToggleDotfiles => CurrentTab == SidebarTab.Files ||
            CurrentTab == SidebarTab.Search;

        public static void ToggleDotfiles()
        {
            if (!CanToggleDotfiles) return;
            Settings.S.sidebarShowHidden = !Settings.S.sidebarShowHidden;
            Settings.S.Write();
            ReloadAfterVisibilityChange();
        }

        public static void ToggleGitignored()
        {
            if (!CanToggleDotfiles) return;
            Settings.S.sidebarShowGitignored = !Settings.S.sidebarShowGitignored;
            Settings.S.Write();
            ReloadAfterVisibilityChange();
        }

        static void ReloadAfterVisibilityChange()
        {
            if (CurrentTab == SidebarTab.Files) FilesView.Reload();
            else SearchView.Search();
        }

        static Rect _visibilityRect;

        static void OpenVisibilityMenu()
        {
            var opts = new List<FloatMenuOption>
            {
                SlopWidgets.MenuToggle("Dotfiles", Settings.SidebarShowHidden,
                    () => { ToggleDotfiles(); OpenVisibilityMenu(); }),
                SlopWidgets.MenuToggle("Gitignored", Settings.SidebarShowGitignored,
                    () => { ToggleGitignored(); OpenVisibilityMenu(); }),
            };
            TerminalWindow.OpenOverPane(
                new SlopMenu(opts, new Vector2(_visibilityRect.x, _visibilityRect.yMax)));
        }

        public static void SetAllFolds(bool folded)
        {
            if (!CanFoldCurrent) return;
            switch (CurrentTab)
            {
                case SidebarTab.Agents:
                    foreach (var key in Layout.Order) Fold(key, folded);
                    break;
                case SidebarTab.Files: FilesView.SetAllFolded(folded); break;
                case SidebarTab.Git: GitView.SetAllFolded(folded); break;
                case SidebarTab.Library: LibraryView.SetAllFolded(folded); break;
            }
        }

        public static void RefreshCurrentView()
        {
            switch (CurrentTab)
            {
                case SidebarTab.Agents: SessionHub.Instance.Refresh(); break;
                case SidebarTab.Files: FilesView.Reload(); break;
                case SidebarTab.Search: SearchView.Search(); break;
                case SidebarTab.Git: GitView.Refresh(); break;
                case SidebarTab.Library:
                    SessionHub.Instance.RefreshLibrary(SlopWidgets.Fail);
                    break;
                case SidebarTab.Tasks:
                    SessionHub.Instance.RefreshTasks(SlopWidgets.Fail);
                    break;
            }
        }

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

        public static bool AgentScrollOpen => Interaction.AgentScrollOpen;

        public static bool FaceBox(Pawn pawn, out Rect box)
        {
            if (pawn != null && Layout.Faces.TryGetValue(pawn, out box)) return true;

            box = Rect.zero;
            return false;
        }

        struct PlacementMeasure
        {
            public float Scale;
            public float Pitch;
            public float Cell;
            public float Face;
            public float RowH;
        }

        public static float Place(
            List<ColonistBar.Entry> entries, List<Vector2> locs, int count, bool plus)
        {
            Layout.BeginFrame();
            Interaction.BeginFrame();
            Bucket(entries, locs, count);

            if (CurrentTab != SidebarTab.Agents)
            {
                // Skipping entries would leave invisible vanilla hit targets over the tree.
                for (int i = 0; i < count && i < locs.Count; i++) locs[i] = Parked;
                return Nominal;
            }

            // Measure.
            var measure = MeasurePlacement();

            // Layout.
            float y = LayoutAgents(entries, locs, measure);
            Layout.AgentContentH = Mathf.Max(Body.height, y + Pad);

            return measure.Scale;
        }

        static PlacementMeasure MeasurePlacement()
        {
            // Agent rows are content coordinates: DrawBack/DrawFront put them inside the
            // shared scroll view, whose screen origin is Body. The chrome below is still
            // screen-fixed and never enters this coordinate space.
            float room = Body.height;
            int rows = 0;
            foreach (var key in Layout.Order)
                if (!Folded.Contains(key) && Layout.Buckets.TryGetValue(key, out var b))
                    rows += b.Count;

            // Add is outside this viewport now, so the body already accounts for its height.
            float scale = Fit(rows, Layout.Order.Count, false, room, GhostRoom());
            float face = ColonistBarColonistDrawer.PawnTextureSize.y * scale;
            return new PlacementMeasure
            {
                Scale = scale,
                Pitch = Pitch(scale),
                Cell = ColonistBar.BaseSize.y * scale,
                Face = face,
                RowH = Mathf.Max(face, TextH),
            };
        }

        static float LayoutAgents(
            List<ColonistBar.Entry> entries, List<Vector2> locs, PlacementMeasure measure)
        {
            float width = Width;
            float y = Pad;
            foreach (var w in Layout.TopWorkers) y = WorkerRow(w, width, y, 0);
            foreach (var g in Layout.TopGhosts) y = GhostRow(g, width, y);

            foreach (var key in Layout.Order)
                y = LayoutGroup(key, entries, locs, width, y, measure);

            return y;
        }

        static float LayoutGroup(
            string key, List<ColonistBar.Entry> entries, List<Vector2> locs,
            float width, float y, PlacementMeasure measure)
        {
            var bucket = Layout.Buckets.TryGetValue(key, out var b) ? b : Empty;
            bool folded = Folded.Contains(key);
            var ghosts = Layout.Ghosts.TryGetValue(key, out var gs) ? gs : EmptyGhosts;
            int workers = WorkerCount(key);

            Layout.Heads.Add(new Head
            {
                Label = key,
                Rect = new Rect(0f, y, width, HeadH),
                Count = bucket.Count + ghosts.Count + workers,
                Folded = folded,
            });
            y += HeadH;

            if (folded)
            {
                foreach (int i in bucket) locs[i] = Parked;
                return y;
            }

            foreach (var g in ghosts) y = GhostRow(g, width, y);
            foreach (int i in bucket)
            {
                LayoutAgent(entries, locs, i, width, y, measure);
                y += measure.Pitch;
                y = LayoutWorkers(Layout.Named[i], width, y, 0);
            }
            return y;
        }

        static int WorkerCount(string project)
        {
            int count = 0;
            foreach (var workers in Layout.Workers.Values)
                foreach (var worker in workers)
                    if (worker != null && worker.Project == project) count++;
            return count;
        }

        static float LayoutWorkers(string parent, float width, float y, int depth)
        {
            if (depth > 32 || !Layout.Workers.TryGetValue(parent, out var workers)) return y;
            foreach (var worker in workers)
            {
                y = WorkerRow(worker, width, y, depth + 1);
                y = LayoutWorkers(worker.Name, width, y, depth + 1);
            }
            return y;
        }

        static void LayoutAgent(
            List<ColonistBar.Entry> entries, List<Vector2> locs, int index,
            float width, float y, PlacementMeasure measure)
        {
            locs[index] = new Vector2(PortraitX + (measure.Face - measure.Cell) / 2f,
                y + (measure.RowH - measure.Cell) / 2f);

            // Keep the three text lines at the same gap from the portrait after the
            // portrait column moves to the screen edge; headings and chrome retain
            // their CellX inset.
            float tx = PortraitX + measure.Face + TextGap;
            var line = new Rect(0f, y, width, measure.RowH);
            var face = new Rect(PortraitX, y + (measure.RowH - measure.Face) / 2f,
                measure.Face, measure.Face);
            Layout.Rows.Add(new Row
            {
                Session = Session(entries[index].pawn),
                Pawn = entries[index].pawn,
                Line = line,
                Text = new Rect(tx, y + (measure.RowH - TextH) / 2f,
                    width - tx - Pad, TextH),
                Face = face,
            });
            // Match FaceBox's old first-row behavior if vanilla ever supplies a duplicate
            // pawn entry.
            if (!Layout.Faces.ContainsKey(entries[index].pawn))
                Layout.Faces.Add(entries[index].pawn, face);
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
                if (info == null)
                {
                    // The daemon's list can drop a removed session before the colony sweep
                    // unbinds its pawn. Do not mistake that stale pawn for a real no-project
                    // agent while the two views catch up.
                    locs[i] = Parked;
                    continue;
                }
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

            // Vanilla's entry order is only refreshed when the colony reconciles. A rename,
            // project move, or add can therefore leave the visible rows in the old order for
            // several seconds. Sort the indices here rather than waiting for displayOrder;
            // the indices still point at the original entries, so vanilla draws the right
            // pawn at each new location and hit-testing remains aligned.
            foreach (var list in Layout.Buckets.Values)
                list.Sort((a, b) => AgentColony.CompareNames(Layout.Named[a], Layout.Named[b]));

            foreach (var s in SessionHub.Instance.Sessions)
            {
                if (s.Worker)
                {
                    if (!Passes(s.Project)) continue;
                    // A child is nested only when its explicit parent has a visible normal row.
                    // Missing parents are surfaced at the top instead of being silently lost.
                    var parent = SessionHub.Instance.Get(s.Parent);
                    bool parentVisible = parent != null && !parent.Worker && Passes(parent.Project)
                        && Layout.Named.ContainsValue(parent.Name);
                    if (!parentVisible)
                    {
                        Layout.TopWorkers.Add(s);
                        continue;
                    }
                    if (!Layout.Workers.TryGetValue(s.Parent, out var children))
                        Layout.Workers[s.Parent] = children = new List<SessionInfo>();
                    children.Add(s);
                    continue;
                }
                if ((!s.Ephemeral && !s.Host) || IsRouted(s) || !Passes(s.Project)) continue;
                if (string.IsNullOrEmpty(s.Project)) { Layout.TopGhosts.Add(s); continue; }

                if (!Layout.Ghosts.TryGetValue(s.Project, out var list))
                    Layout.Ghosts[s.Project] = list = new List<SessionInfo>();
                list.Add(s);
            }

            foreach (var kv in Layout.Buckets)
                if (kv.Value.Count > 0) Layout.Order.Add(kv.Key);
            foreach (var kv in Layout.Ghosts)
                if (kv.Value.Count > 0 && !Layout.Order.Contains(kv.Key)) Layout.Order.Add(kv.Key);
            foreach (var p in SessionHub.Instance.Projects)
                if (!Layout.Order.Contains(p.Name) && Passes(p.Name)) Layout.Order.Add(p.Name);

            Layout.Order.Sort((a, b) =>
                a == Loose ? (b == Loose ? 0 : 1)
                : b == Loose ? -1
                : string.CompareOrdinal(a, b));

            Layout.TopGhosts.Sort(ByName);
            Layout.TopWorkers.Sort(ByName);
            foreach (var list in Layout.Ghosts.Values) list.Sort(ByName);
            foreach (var list in Layout.Workers.Values) list.Sort(ByName);
        }

        static readonly List<int> Empty = new List<int>();
        static readonly List<SessionInfo> EmptyGhosts = new List<SessionInfo>();

        static float GhostRow(SessionInfo s, float width, float y)
        {
            // Agent ghost rows draw their own leading mark, if any. The routed view owns the
            // separate action-slot layout; reserving it here needlessly shortens host paths.
            float tx = CellX;
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

        static float WorkerRow(SessionInfo s, float width, float y, int depth)
        {
            float tx = CellX + Mathf.Min(depth, 8) * 12f;
            Layout.Rows.Add(new Row
            {
                Session = s.Name,
                Pawn = null,
                Ghost = false,
                Worker = true,
                Line = new Rect(0f, y, width, WorkerH),
                Text = new Rect(tx, y + 1f, width - tx - Pad, WorkerH - 1f),
                Face = Rect.zero,
            });
            return y + WorkerH;
        }

        static int ByName(SessionInfo a, SessionInfo b) =>
            string.CompareOrdinal(a?.Name ?? "", b?.Name ?? "");

        static float GhostRoom()
        {
            float h = Layout.TopGhosts.Count * GhostH + Layout.TopWorkers.Count * WorkerH;
            foreach (var kv in Layout.Ghosts)
                if (!Folded.Contains(kv.Key)) h += kv.Value.Count * GhostH;
            foreach (var kv in Layout.Workers)
                if (!Folded.Contains(kv.Key)) h += WorkerCount(kv.Key) * WorkerH;
            return h;
        }

        static string Session(Pawn pawn) =>
            pawn == null ? null : AgentColony.Current?.SessionOf(pawn);

        static void BeginAgentScroll()
        {
            var body = Body;
            // The full-width view keeps SmoothScroll active while omitting the gutter that narrows every row.
            var view = new Rect(0f, 0f, body.width, Layout.AgentContentH);
            Interaction.AgentScroll.Begin(body, view, false);
            Interaction.AgentScrollOpen = true;
        }

        static void EndAgentScroll()
        {
            if (!Interaction.AgentScrollOpen) return;
            Interaction.AgentScrollOpen = false;
            Interaction.AgentScroll.End();
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
                case SidebarTab.Library:
                    LibraryView.Clicks();
                    break;
                case SidebarTab.Tasks:
                    TasksView.Clicks();
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
                Interaction.Resizing = false;
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
                case SidebarTab.Library:
                    LibraryView.Draw(Body);
                    break;
                case SidebarTab.Tasks:
                    TasksView.Draw(Body);
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
                "Add a project, an agent, a library item, a sandbox preset, a command or a host shell");
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
                new FloatMenuOption("Task", () =>
                    TerminalWindow.OpenOverPane(new DelegateTaskDialog(null))),
                new SlopSubmenu("Library", LibraryItemOptions),
                new FloatMenuOption("Sandbox preset...", SlopOptions.OpenNewSandboxPreset),
                new FloatMenuOption("Command...", SlopOptions.OpenNewCommand),
                new SlopSubmenu("Host shell", HostShellOptions),
            };
            TerminalWindow.OpenOverPane(new SlopMenu(opts));
            return true;
        }

        static List<FloatMenuOption> LibraryItemOptions() => new List<FloatMenuOption>
        {
            new FloatMenuOption("Prompt...", () =>
                TerminalWindow.OpenOverPane(new EditLibraryItemDialog(LibraryItemKind.Prompt))),
            new FloatMenuOption("Breadcrumb...", () =>
                TerminalWindow.OpenOverPane(new EditLibraryItemDialog(LibraryItemKind.Breadcrumb))),
            new FloatMenuOption("Shell...", () =>
                TerminalWindow.OpenOverPane(new EditLibraryItemDialog(LibraryItemKind.Shell))),
            new FloatMenuOption("File Action...", () =>
                TerminalWindow.OpenOverPane(new EditLibraryItemDialog(LibraryItemKind.FileAction))),
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
                if (!Passes(p.Name)) continue;
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
            Tab(new Rect(x, y, TabIcon, TabIcon), Icons.Tasks,
                CurrentTab == SidebarTab.Tasks,
                "Tasks - delegate work and inspect the agent mailbox",
                () => Show(SidebarTab.Tasks));
            x += TabIcon + Gap;
            Tab(new Rect(x, y, TabIcon, TabIcon), Icons.Library,
                CurrentTab == SidebarTab.Library,
                "Library - one-shot errands you can run against any project",
                () => Show(SidebarTab.Library));

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
            if (CurrentTab != SidebarTab.Search && CurrentTab != SidebarTab.Tasks)
            {
                bool folded = AllFolded();
                Tab(r, folded ? TexButton.Reveal : TexButton.Collapse, folded,
                    folded ? "Unfold all." : "Fold all.", () => SetAllFolds(!folded));
                r.x -= TabIcon + 3f;
            }

            if (CurrentTab == SidebarTab.Files || CurrentTab == SidebarTab.Search)
            {
                bool active = Settings.SidebarShowHidden || Settings.SidebarShowGitignored;
                _visibilityRect = r;
                Tab(r, Icons.Hidden, active,
                    active
                        ? "Visibility filters active. Click to change."
                        : "All files shown. Click to filter.",
                    OpenVisibilityMenu);
            }
            else if (CurrentTab == SidebarTab.Git)
            {
                Tab(r, Icons.Refresh, false,
                    "Read every working tree again.", GitView.Refresh);
            }
            else if (CurrentTab == SidebarTab.Tasks)
            {
                Tab(r, Icons.Refresh, false,
                    "Read the task mailbox again.", () =>
                        SessionHub.Instance.RefreshTasks(SlopWidgets.Fail));
                r.x -= TabIcon + 3f;
                TasksView.FilterButton(r);
            }
        }

        static bool AllFolded()
        {
            switch (CurrentTab)
            {
                case SidebarTab.Agents:
                    return Layout.Order.Count > 0 && Layout.Order.TrueForAll(Folded.Contains);
                case SidebarTab.Files: return FilesView.AllFolded;
                case SidebarTab.Git: return GitView.AllFolded;
                case SidebarTab.Library: return LibraryView.AllFolded;
                case SidebarTab.Tasks: return false;
                default: return false;
            }
        }

        static void ToggleAllFolds()
        {
            SetAllFolds(!AllFolded());
        }

        // Where the filter button is. One rect, so the menu comes out under the button
        // whether the button or the command palette opened it - and, being fixed rather
        // than taken from the mouse, so a menu that reopens itself after each tick reopens
        // in the place it was.
        static Rect FilterRect =>
            new Rect(Width - CellX - TabIcon,
                (TabRowH - TabIcon) / 2f, TabIcon, TabIcon);

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

        // A soft edge marks the fixed add strip over the scrolling body without reserving a scrollbar gutter.
    }
}
