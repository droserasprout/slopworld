using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The colonist bar as a left-hand column: agents gathered under the project they run in,
    // a portrait and three lines apiece - the name, what it is doing and for how long, and
    // what the app calls itself. The other half of ColonistBarStrip, which owns the
    // swap - this file is only the shape, and everything said there about pointing the bar's
    // own cached layout at ours holds here too.
    //
    // So the portraits, their state icons, the selection brackets and the click that opens a
    // pane are all still the bar's own; what is drawn *around* them is ours, from the same
    // ColonistBarOnGUI call, which is what keeps the column in one piece over a pane as well
    // as on the map.
    //
    // The name under each portrait is vanilla's, centred in a cell 26 pixels wide, and there
    // is no room for it beside the text this draws. Patch_SidebarPawnLabel declines it while
    // the column is up.
    public static class AgentSidebar
    {
        // Wide enough for a session name and a title beside a portrait, and dragged from
        // there by the edge. Clamped on the way out rather than on the way in: a setting
        // written on a wide screen and read on a narrow one is a column with no map beside
        // it, and this comes off the map for as long as the layout is on.
        public const float MinWidth = 150f;
        public const float MaxWidth = 460f;

        public static float Width => Mathf.Clamp(Settings.SidebarWidth, MinWidth,
            Mathf.Max(MinWidth, Mathf.Min(MaxWidth, UI.screenWidth * 0.4f)));

        // The cell, as a fraction of the bar's nominal 48x48. Shrinks further to fit the
        // column, never below Floor - past that a portrait is a smudge and the column should
        // run off the bottom instead, which at least says there are more.
        const float Nominal = 0.5f;
        const float Floor = 0.3f;

        // The heading band is a tiny line and follows the font: a figure here is one that crops
        // the band's own label on any face taller than the one it was written against. The add
        // strip below holds a glyph rather than a word, so it keeps a figure.
        static float HeadH => SlopWidgets.TinyRowH;
        const float AddH = 26f;

        // The plus in that strip. Larger than the tab icons on purpose: this is the one
        // button on the panel that is not about the view you are in.
        const float AddIcon = 16f;
        const float Pad = 6f;
        const float CellX = 8f;
        const float TextGap = 7f;

        // The view selector across the top of the panel, and the size of an icon in it.
        // Reserved in every view, so switching moves nothing below it.
        public const float TabH = 24f;
        const float TabIcon = 18f;

        // Clearance between the text of one row and the next, where the pitch is the labels'
        // rather than the portraits'.
        const float RowGap = 4f;

        // A ghost row is one line of one font and does not shrink with the portraits, having
        // none: what it costs the column is the same however crowded the column is.
        static float GhostH => NameH + 2f;

        // The two label lines are as tall as their fonts actually draw, asked rather than
        // written down: Widgets.Label ends in GUI.Label, which clips glyphs to the rect, and
        // Verse.Text measures lineHeights off the font at startup (CalcHeight("W", 999)). So a
        // figure here is one that crops descenders on any font but the one it was eyeballed
        // against - which is what cost the labels their bottom pixel rows. Vanilla sizes its
        // own Widgets.Label(x, ref curY, ...) rects with CalcHeight for the same reason.
        //
        // Through SlopWidgets, so the sub-line is measured at the tier it will *land* on:
        // asking Tiny for its height and then being handed Small to draw in is the same
        // cropped row arrived at from the other end - see SlopWidgets.Real.
        static float NameH => SlopWidgets.LineHOf(GameFont.Small);
        static float SubH => SlopWidgets.TinyH;

        // Three of them: the name, what it is doing and for how long, and what it calls
        // itself. One figure rather than the sum written out at each of the four places that
        // needs it - the row height, the pitch floor, the text rect and the draw - since a
        // line added or taken away here must move all four together or rows overlap.
        static float TextH => NameH + SubH * 2f;

        // What the bell takes off the end of the name line, and what the elapsed takes off
        // the end of the state line.
        const float BellW = 13f;
        const float AgoGap = 6f;

        // And what the mark in front of a ghost's name takes off the front of it: a pager, an
        // editor or a diff, said as the icon the tree it was opened from puts on that row.
        const float GhostMarkW = 12f;

        // The arrow before a heading, and how much of the edge answers a drag.
        const float ArrowW = 12f;
        const float GripW = 5f;

        // The panel, its edge and its greys are SlopWidgets' - this column was where most of
        // them were first mixed, and three other files had since mixed their own within a
        // hundredth of these. An age and a directory sit a rung below the line they share,
        // both being there to be glanced at rather than read.

        // The same amber Waiting wears, that being what an unanswered bell means whatever the
        // rules made of the screen.
        static readonly Color BellColor = new Color(0.98f, 0.80f, 0.30f);

        // A loc nothing draws and nothing can be clicked at: the bar hit-tests against the
        // same list it draws from, so parking one is how an entry is taken out of both.
        static readonly Vector2 Parked = new Vector2(-9999f, -9999f);

        // A group of one project's agents, and one agent's place in the column. Rebuilt by
        // Place, read by both draw passes and by the click handler - three things that must
        // never disagree about where a row is, hence one table rather than the arithmetic
        // three times.
        struct Row
        {
            public string Session;
            public Pawn Pawn;
            public Rect Line;   // the whole row, for the highlight and the hover
            public Rect Text;   // beside the portrait: the three labels, and what a click takes
            public Rect Face;   // the square the close-up is drawn in, read by the drawer patch

            // A session with no colonist behind it: the viewer's `less`, the editor, a shell
            // on the host. One line and a name, no portrait and no state - see GhostH.
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

        // Reused rather than rebuilt: this runs every frame, and a dictionary of lists per
        // frame is garbage the game does not need. Keys stay behind when a project empties;
        // Order skips the ones with nothing in them.
        static readonly Dictionary<string, List<int>> Buckets =
            new Dictionary<string, List<int>>();
        static readonly List<string> Order = new List<string>();

        // The session each bucketed entry belongs to. Only Rows carries that in the agents
        // view, and the files view lays out no rows - so the bucket pass writes it down,
        // that being the pass both views run.
        static readonly Dictionary<int, string> Named = new Dictionary<int, string>();

        // The ephemeral sessions, which have no colonist and so are in none of the tables
        // above: the bar knows nothing about them. A viewer's `less`, an editor, a shell on
        // the host - things a person opened and will close, rather than agents the colony
        // has. They are laid out from the hub's own list, under the project they run in and
        // ahead of that project's agents, because they are the transient thing and the
        // agents are what the group is about.
        static readonly Dictionary<string, List<SessionInfo>> Ghosts =
            new Dictionary<string, List<SessionInfo>>();

        // The ones belonging to no project at all - an adopted tmux session, a host shell in
        // a directory the daemon has no entry for. Nothing to file them under, so they go at
        // the very top of the column, above the first heading.
        static readonly List<SessionInfo> TopGhosts = new List<SessionInfo>();

        // The bucket for an agent whose project has gone, and for a pawn that is not an
        // agent at all. Last in the column, and named rather than blank: an unheaded run of
        // portraits reads as belonging to the project above it.
        const string Loose = "no project";

        // Which headings are rolled up. Parsed from the setting once and written back on the
        // click, one name per line - a project is the daemon's rather than a colony's, so a
        // fold is not something a save should carry, and the settings file is where the rest
        // of what this screen looks like already lives.
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
            // ModSettings.Write rather than Mod.WriteSettings: the latter reconnects the
            // socket, and this is a fold.
            s.Write();
        }

        // Top to bottom, and the top bar starts where this ends rather than crossing it: a
        // panel hung under the bar leaves the corner above it showing the map through a hole
        // the width of the column.
        public static Rect Panel => new Rect(0f, 0f, Width, UI.screenHeight);

        // What the column is showing. Three shapes over one panel: the chrome, the width, the
        // edge and the click-eating are the panel's and are drawn once, whichever view has
        // the body.
        //
        // An unknown tab is the agents view. The setting is a string in a file a person can
        // edit, and the column has to draw something.
        public const string TabAgents = "agents", TabFiles = "files", TabGit = "git",
            TabShortcuts = "shortcuts";

        public static bool Files => Settings.SidebarTab == TabFiles;
        public static bool Git => Settings.SidebarTab == TabGit;
        public static bool Shortcuts => Settings.SidebarTab == TabShortcuts;
        public static bool Agents => !Files && !Git && !Shortcuts;

        static void Show(string tab)
        {
            // Clicking the tab already up is not a change, and the file is a file.
            if (Settings.SidebarTab == tab) return;

            // Leaving a view is a focus change: whatever pager it opened - the files view's
            // `less` on a file, the git view's diff - is no longer being looked at, so it
            // goes. Both are asked, the one being left not being worth working out.
            if (tab != TabFiles) FilesView.ReleaseViewer();
            if (tab != TabGit) GitView.ReleaseViewer();

            var s = Settings.S;
            s.sidebarTab = tab;
            // ModSettings.Write rather than Mod.WriteSettings, the same as a fold: the latter
            // reconnects the socket, and this is a tab.
            s.Write();

            // Arriving in the git view is what asks: a working tree changes under this column
            // all day and nothing tells it so, the agents being the ones doing the changing.
            // Only what has never been read - the refresh button is how a reader asks again.
            //
            // The files view asks for the same reading: a row there grows a diff button where
            // the working tree has a change, and that is the git view's answer to give.
            if (tab == TabGit || tab == TabFiles) GitView.Entered();

            // The shortcuts list is read from the daemon each time the view is entered, and
            // also refreshed by the button on its tab or the footer.
            if (tab == TabShortcuts) SessionHub.Instance.RefreshShortcuts();
        }

        // The column's answer to a terminal being summoned: F12 opening a pane, or Alt+Num
        // pointing one at a portrait, are both about an agent, and the other two views are
        // not. The pagers their bodies opened go with them.
        public static void FocusTerminal() => Show(TabAgents);

        // F3 / F4 / F5: focus the other sidebar views without leaving the terminal chrome,
        // the same way FocusTerminal switches back to the agents. See TerminalWindow.HandleFunctionKey.
        public static void ShowFiles() => Show(TabFiles);
        public static void ShowGit() => Show(TabGit);
        public static void ShowShortcuts() => Show(TabShortcuts);

        // The add strip, pinned to the foot of the panel. The column's own button rather
        // than any one view's: what it adds is asked on the click, so the four views share
        // one slot in one place and switching never moves it.
        public static Rect AddBar =>
            new Rect(CellX, UI.screenHeight - Pad - AddH, Width - CellX * 2f, AddH);

        // Everything between the selector and that strip, which is where a view draws.
        // Taken off the body rather than drawn over it, so the last row of a full list is
        // still reachable in every view.
        public static Rect Body =>
            new Rect(0f, TabH, Width,
                Mathf.Max(0f, UI.screenHeight - TabH - AddH - Pad * 2f));

        // The column's order is the column's own, so Alt+3 is the third portrait down rather
        // than the third the bar would have drawn.
        public static List<string> Sessions()
        {
            var order = new List<string>();

            if (Agents)
            {
                // Agents only. A number here is a portrait - Alt+3 is the third face down,
                // and on the map it selects that colonist, which a ghost row has none of.
                foreach (var row in Rows)
                    if (row.Session != null && !row.Ghost) order.Add(row.Session);
                return order;
            }

            // With a tree up there are no rows, and Alt+Num is the way back to an agent
            // from a view that draws none - so it is answered off the buckets instead, folds
            // and all. A fold takes an agent off the numbers because it takes it off the
            // column; switching views is not a fold, and hides every agent equally.
            foreach (var key in Order)
                foreach (int i in Buckets[key])
                    if (Named.TryGetValue(i, out var session) && session != null)
                        order.Add(session);
            return order;
        }

        /// <summary>
        /// The visual order for the comma/dot walk: every visible row, ghosts included
        /// and folds respected, because a walk follows the eye.
        /// Rows is empty in the files and git views, so the caller falls back to the hub.
        /// </summary>
        public static List<string> WalkOrder()
        {
            var order = new List<string>();
            foreach (var row in Rows)
                if (row.Session != null) order.Add(row.Session);
            return order;
        }

        // True for the length of the two draw passes, which is when the pawn label is
        // declined.
        public static bool Drawing { get; private set; }

        // The square Place laid out for this pawn's close-up. Read by
        // Patch_SidebarPortraitDraw rather than derived there: Place is the only thing that
        // knows where a row is, and a portrait drawn off its own arithmetic is a fourth
        // answer to disagree with the three the Row table already keeps in step. No row means
        // the column laid none out - a caravan group entry, parked off screen - and there is
        // nothing to draw a face in.
        public static bool FaceBox(Pawn pawn, out Rect box)
        {
            if (pawn != null)
                foreach (var row in Rows)
                    if (row.Pawn == pawn) { box = row.Face; return true; }

            box = Rect.zero;
            return false;
        }

        // Lays the whole column out and answers the scale the portraits are drawn at.
        // Entries the bar carries with no pawn (a caravan's group row) are parked off screen:
        // there is a loc for every entry whether it draws or not.
        public static float Place(
            List<ColonistBar.Entry> entries, List<Vector2> locs, int count, bool plus)
        {
            Rows.Clear();
            Heads.Clear();
            // Whichever view is up: the buckets are what Sessions() answers from, and Alt+Num
            // still means an agent with the tree on screen. Cheap - it lays nothing out.
            Bucket(entries, locs, count);

            if (!Agents)
            {
                // Nothing of the bar is on the panel, so every loc goes off screen: the bar
                // draws from this list and hit-tests against it, and a portrait left where it
                // was would be an invisible click target under the tree. No Rows either, so
                // the label pass and the click handler have nothing to find.
                for (int i = 0; i < count && i < locs.Count; i++) locs[i] = Parked;
                return Nominal;
            }

            // Below the selector, which is reserved in both views. The top bar is beside the
            // column rather than over it, so the rest of the height is the column's.
            float top = TabH + Pad;
            float room = UI.screenHeight - top - Pad;

            // Only what is on show competes for the room: folding a project is how a column
            // that had shrunk to fit gets its portraits back.
            int rows = 0;
            foreach (var key in Order)
                if (!Folded.Contains(key) && Buckets.TryGetValue(key, out var b))
                    rows += b.Count;

            float s = Fit(rows, Order.Count, plus, room, GhostRoom());
            float pitch = Pitch(s);
            float cell = ColonistBar.BaseSize.y * s;
            // The row is as tall as vanilla's portrait, and the face box is that square. The
            // close-up has no overhang to leave room for - the camera is centred on the head
            // rather than on a body cropped at the hips - so the box is the whole row and the
            // row is the whole portrait. Everything below is laid out off this one figure,
            // which is what keeps Patch_SidebarPortraitDraw from having a second opinion.
            float face = ColonistBarColonistDrawer.PawnTextureSize.y * s;
            // Only the portraits shrink; the labels cannot, the fonts being fixed. So on a
            // crowded column the three lines beside a row are taller than the row, and the row
            // is the taller of the two - with Pitch floored to match, or neighbours write over
            // each other. The face box keeps its own square and is centred in what is left.
            float rowH = Mathf.Max(face, TextH);

            float width = Width;
            float y = top;

            // Above the first heading, because they belong under none of them: a shell or a
            // viewer the daemon has no project for is not the loose bucket's business either,
            // that being agents whose project has gone.
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
                    // Parked rather than merely skipped: the bar draws from this list and
                    // hit-tests against it, and a portrait left where it was would be an
                    // invisible click target under whatever the fold pulled up over it. No
                    // row goes in either, which is what keeps a folded agent off Alt+Num.
                    foreach (int i in bucket) locs[i] = Parked;
                    continue;
                }

                // Ahead of the project's agents: what somebody opened to look at something is
                // the transient thing on this list, and the agents are what the group is for.
                foreach (var g in ghosts) y = GhostRow(g, width, y);

                foreach (int i in bucket)
                {
                    // The loc is the cell's corner, and the cell is the bar's own hit-test box
                    // at a fixed BaseSize*s - smaller than the face box, and not ours to
                    // resize, Size being one figure for the whole bar. Centred in the box, so
                    // the middle of a face is what answers a click.
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

            // AddBar is where the button goes and this pass does not lay it out: it is the
            // column's own and is drawn in every view. What is reserved here is the *room*
            // for it - Fit takes it off the height, so the rows never run under it.
            return s;
        }

        // Vanilla's own vertical pitch, floored at what the labels need. The portraits shrink
        // with s and the fonts do not, so past some scale vanilla's figure is narrower than the
        // three lines beside a row: the column runs off the bottom sooner, which is the failure
        // Fit already prefers to a smudge, rather than rows overwriting each other. With three
        // lines that floor is the usual answer rather than the crowded one, which is the price
        // of the third line and is paid in rows on screen.
        static float Pitch(float s) => Mathf.Max(
            (ColonistBar.BaseSize.y + ColonistBar.BaseSpaceBetweenColonistsVertical) * s,
            TextH + RowGap);

        // Headings and the "+" are a fixed cost, so only the portraits shrink. Solved rather
        // than stepped down, this being a straight line in s.
        //
        // Only while the portraits are what the spacing is measured off, though: below the
        // scale where Pitch hits its floor the rows stop closing up and every pixel taken off
        // a face buys nothing. So that scale is the real floor and Floor is only the backstop
        // - asked of the fonts rather than written down, three lines of them being enough to
        // put it above Nominal on any ordinary screen. The column then runs off the bottom
        // with its portraits legible, which is the failure this already prefers to a smudge.
        static float Fit(int rows, int groups, bool plus, float room, float ghosts)
        {
            if (rows <= 0) return Nominal;
            float fixedH = groups * HeadH + (plus ? AddH + 2f : 0f) + ghosts;
            float each = ColonistBar.BaseSize.y + ColonistBar.BaseSpaceBetweenColonistsVertical;
            float s = (room - fixedH) / (rows * each);
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
                    // No pawn, nothing to draw, and a loc left where it was would land the
                    // group frame in the middle of the column.
                    locs[i] = Parked;
                    continue;
                }

                var session = Session(pawn);
                var info = session == null ? null : SessionHub.Instance.Get(session);
                string key = string.IsNullOrEmpty(info?.Project) ? Loose : info.Project;

                if (!Buckets.TryGetValue(key, out var list))
                    Buckets[key] = list = new List<int>();
                list.Add(i);
                Named[i] = session;
            }

            // The ephemeral ones, off the hub: no colonist was spawned for them (see
            // AgentColony.Reconcile), so there is no bar entry to walk. A project of its own
            // gets a heading even when every session under it is one of these - the group is
            // the project, not the colony's share of it.
            foreach (var s in SessionHub.Instance.Sessions)
            {
                if (!s.Ephemeral) continue;
                if (string.IsNullOrEmpty(s.Project)) { TopGhosts.Add(s); continue; }

                if (!Ghosts.TryGetValue(s.Project, out var list))
                    Ghosts[s.Project] = list = new List<SessionInfo>();
                list.Add(s);
            }

            foreach (var kv in Buckets)
                if (kv.Value.Count > 0) Order.Add(kv.Key);
            foreach (var kv in Ghosts)
                if (kv.Value.Count > 0 && !Order.Contains(kv.Key)) Order.Add(kv.Key);

            // Alphabetical, with the loose ones last: the column has to come back the same
            // way every frame, and a dictionary's own order does not.
            Order.Sort((a, b) =>
                a == Loose ? (b == Loose ? 0 : 1)
                : b == Loose ? -1
                : string.CompareOrdinal(a, b));

            // Same reason, one rung down: the hub's list is the daemon's order, which moves
            // when a session is added or dropped.
            TopGhosts.Sort(ByName);
            foreach (var list in Ghosts.Values) list.Sort(ByName);
        }

        // Handed out where a project has one table filled and not the other, so the layout
        // loop reads the same either way.
        static readonly List<int> Empty = new List<int>();
        static readonly List<SessionInfo> EmptyGhosts = new List<SessionInfo>();

        // One line, the width of the panel, and no portrait to leave room for: the text
        // starts where a heading's does rather than where an agent's does, which is what
        // makes these read as belonging to the group instead of as agents in it.
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

        // What the ghosts cost the column, which is fixed: they are text and text does not
        // shrink. Folded groups are free, the same as their portraits.
        static float GhostRoom()
        {
            float h = TopGhosts.Count * GhostH;
            foreach (var kv in Ghosts)
                if (!Folded.Contains(kv.Key)) h += kv.Value.Count * GhostH;
            return h;
        }

        static string Session(Pawn pawn) =>
            pawn == null ? null : AgentColony.Current?.SessionOf(pawn);

        // ------------------------------------------------------------------ drawing
        //
        // Two passes around the bar's own, from ColonistBarOnGUI: the panel and the headings
        // go under the portraits, the labels and the clicks over them.

        public static void DrawBack()
        {
            // Ahead of the two below, which decline a Layout event: a drag is only ever half
            // over by the time one of those arrives, and forgetting it there would end every
            // resize on the frame it started.
            if (Event.current.type == EventType.Layout) return;

            // The map-layer call under a pane, which the terminal makes again from inside its
            // own contents. Silently, and *without* touching the drag: this pass runs first
            // and would clear a resize the pane's pass had started on the frame before, which
            // is the whole of why the edge would not drag with a terminal open. Whether the
            // column is on screen is the other call's answer to give.
            if (ColonistBarStrip.Suppressed) return;

            if (!Wanted())
            {
                // Whatever was being dragged, the column is not on screen to drag it by.
                _resizing = false;
                return;
            }
            Drawing = true;

            var panel = Panel;
            Widgets.DrawBoxSolid(panel, SlopWidgets.Panel);

            if (Files)
            {
                FilesView.Draw(Body);
            }
            else if (Git)
            {
                GitView.Draw(Body);
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
                    // The current session is highlighted, whatever its kind.
                    bool current = row.Session != null && row.Session == currentSession;

                    if (current) Widgets.DrawBoxSolid(row.Line, SlopWidgets.RowOn);
                    else if (ColonistBarStrip.MouseOver(row.Line)) Widgets.DrawHighlight(row.Line);
                }

                foreach (var head in Heads) DrawHead(head);
            }

            // Over whichever body just drew, so the selector is never under a row, and last
            // of the drawing so it takes its own clicks first. The add strip goes down with
            // it, being the panel's own furniture at the other end of the column.
            Tabs();
            DrawAdd();

            // Before the bar's own pass rather than after it, because these take clicks the
            // bar would otherwise have eaten: vanilla swallows a right-click over a portrait
            // to keep it off the map, and the heading band is the strip's own ground.
            //
            // The edge first. Headings, rows and the tree are all the full width of the panel,
            // so asked second the grip would be reachable only in the gaps between them, which
            // on a full column is nowhere.
            // The add strip before any body: on a column that has run off the bottom a row
            // is laid out under it, and the button is what a press down there means.
            Grip();
            if (AddClick()) return;
            if (Files) FilesView.Clicks();
            else if (Git) GitView.Clicks();
            else if (Shortcuts) ShortcutsView.Clicks();
            else Menus();
        }

        // One button for the whole column, whichever view has the body: what it adds is
        // asked on the click rather than answered by the tab that happens to be up, so
        // there is one slot in one place and every kind of thing is three pixels from
        // wherever you are.
        static void DrawAdd()
        {
            var r = AddBar;
            bool over = ColonistBarStrip.MouseOver(r);

            if (over)
            {
                Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.08f));
                TooltipHandler.TipRegion(r, "Add a project, an agent or a shortcut");
            }
            Widgets.DrawBoxSolid(new Rect(r.x, r.y, r.width, 1f), SlopWidgets.Edge);

            // A glyph rather than a "+" in GameFont.Medium, which was as large as vanilla's
            // fonts go and still a thin hairline in a 26px strip. Codicons' plus is drawn on
            // the same grid as every other icon here and takes whatever size it is given.
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

            // A project first: it is what the other two hang off, and the answer for an
            // empty daemon is always this one.
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Project...", () =>
                    TerminalWindow.OpenOverPane(new EditProjectDialog(null))),
                new FloatMenuOption("Agent...", () =>
                    TerminalWindow.OpenOverPane(new EditSessionDialog(null))),
                new FloatMenuOption("Shortcut...", () =>
                    TerminalWindow.OpenOverPane(new EditShortcutDialog(null))),
            };
            TerminalWindow.OpenOverPane(new FloatMenu(opts));
            return true;
        }

        // The view selector: three icons top left, mono grey for the view you are not in and
        // white with a line under it for the one you are. Icon only - the column is narrow
        // and a word here is a word taken off every agent's name below it - so the tooltips
        // carry what they mean.
        static void Tabs()
        {
            var strip = new Rect(0f, 0f, Width, TabH);
            Widgets.DrawBoxSolid(new Rect(CellX, TabH - 1f, Width - CellX * 2f, 1f),
                new Color(1f, 1f, 1f, 0.08f));

            float y = (TabH - TabIcon) / 2f;

            // The selector, from the left. The gap is narrower than it was with three, four
            // icons and a switch being what the strip now has to hold on a narrow column.
            const float Gap = 5f;
            float x = CellX;
            Tab(new Rect(x, y, TabIcon, TabIcon), Icons.Agents, Agents,
                "Agents - every session, under the project it runs in", () => Show(TabAgents));
            x += TabIcon + Gap;
            Tab(new Rect(x, y, TabIcon, TabIcon), Icons.Files, Files,
                "Files - every project's directory, as a tree", () => Show(TabFiles));
            x += TabIcon + Gap;
            Tab(new Rect(x, y, TabIcon, TabIcon), Icons.Git, Git,
                "Git - what every working tree has that its last commit does not",
                () => Show(TabGit));
            x += TabIcon + Gap;
            Tab(new Rect(x, y, TabIcon, TabIcon), Icons.Shortcuts, Shortcuts,
                "Shortcuts - one-shot errands you can run against any project",
                () => Show(TabShortcuts));

            // Each tree's one switch, from the right so neither ever shuffles the selector
            // sideways, and drawn only in the view it means something in. The cog and the
            // hamburger that used to sit beside the first of them are on the status bar now:
            // neither was about a view, and this strip is the selector.
            float right = Width - CellX - TabIcon;

            if (Files)
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
                        // What was listed was listed under the old answer, so the tree has to
                        // ask again; the expansions are what the reader wants kept.
                        FilesView.Reload();
                    });
            }
            else if (Git)
            {
                // Not a switch but a button, and the only one on the strip: nothing tells
                // this column that a working tree moved, so asking again is the reader's.
                // Never lit - there is no state here to be in.
                Tab(new Rect(right, y, TabIcon, TabIcon), Icons.Refresh, false,
                    "Read every working tree again.", GitView.Refresh);
            }

            // The shortcuts view has no switch on the strip: the "+" at the foot of the
            // panel is how you add one, and the list stays up to date from the daemon.

            // The strip is the panel's, so a press anywhere along it is the panel's too. Not
            // the last few pixels of it: that is the edge, and Grip - which is asked after
            // this - is what a press there is for.
            if (ColonistBarStrip.MouseOver(strip) && Event.current.rawType == EventType.MouseDown
                && ColonistBarStrip.Interactive
                && Event.current.mousePosition.x < Width - GripW)
                Event.current.Use();
        }

        static void Tab(Rect r, Texture2D icon, bool on, string tip, System.Action go)
        {
            TooltipHandler.TipRegion(r, tip);
            if (Widgets.ButtonImage(r, icon, on ? Color.white : SlopWidgets.Off, Color.white)
                && ColonistBarStrip.Interactive)
                go();

            if (on)
                Widgets.DrawBoxSolid(new Rect(r.x, TabH - 2f, r.width, 2f),
                    new Color(1f, 1f, 1f, 0.55f));
        }

        static void DrawHead(Head head)
        {
            var r = head.Rect;
            if (ColonistBarStrip.MouseOver(r)) Widgets.DrawHighlight(r);

            GUI.color = SlopWidgets.Faint;
            var arrow = new Rect(CellX, r.y + (HeadH - ArrowW) / 2f, ArrowW, ArrowW);
            GUI.DrawTexture(arrow, head.Folded ? TexButton.Reveal : TexButton.Collapse);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerLeft;

            float lx = arrow.xMax + 4f;
            // What the fold hides, said where it was hidden. Only when it is hidden: the
            // portraits are the count the rest of the time.
            string tail = head.Folded ? "  " + head.Count : "";
            var label = new Rect(lx, r.y, r.width - lx - CellX, HeadH);
            SlopWidgets.RowLabel(label, head.Label + tail);

            Widgets.DrawBoxSolid(new Rect(CellX, r.yMax - 1f, r.width - CellX * 2f, 1f),
                new Color(1f, 1f, 1f, 0.08f));

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

                    // A ghost is a name and nothing else. No portrait, because there is no
                    // colonist; no state and no age, because "idle" of a `less` is a fact
                    // about nothing - it is open or it is gone, and gone takes the row with
                    // it. Dim, the way the third line of an agent's row is: this is something
                    // the person opened, not something the colony is doing.
                    if (row.Ghost)
                    {
                        Text.Font = GameFont.Small;
                        var text = row.Text;

                        // What this one is, where it is one of the three the trees open: the
                        // same mark the row it was opened from wears ([RowActions]). In front
                        // of the name rather than after it, because the name is a title the
                        // app wrote and runs long enough to be cut - a mark at the end of it
                        // would be the first thing to go, and this is the half of the row that
                        // says what it is at a glance. Nothing for a host shell or anything
                        // else run by hand: the name is the whole of what those are.
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

                    // Line one is the name, in the colour of what it is doing. A bell rung and
                    // not yet answered takes the end of it: the mark belongs beside the name
                    // rather than beside the state, being about the agent and not its posture.
                    Text.Font = GameFont.Small;
                    var name = new Rect(row.Text.x, row.Text.y, row.Text.width, NameH);
                    if (info != null && info.Bell)
                    {
                        float d = Mathf.Min(BellW, NameH);
                        GUI.color = BellColor;
                        GUI.DrawTexture(
                            new Rect(name.xMax - d, name.y + (NameH - d) / 2f, d, d),
                            Icons.Bell);
                        name.width -= d + 3f;
                    }
                    GUI.color = tint;
                    SlopWidgets.RowLabel(name, row.Session ?? row.Pawn?.LabelShort ?? "?");

                    // Line two is what it is doing and for how long. The elapsed is laid out
                    // from the right so the times line up down the column and the word keeps
                    // whatever is left - the pair reads as one line either way, and "working
                    // 40m" is a different animal from "working 12s". A down agent wears no
                    // age: it is stopped, and how long it has stood there is a fact about
                    // nobody's clock, so the row is just the state.
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

                    // Line three is what the app calls itself, and failing that where it is.
                    // Dimmer for the fallback: a title is this agent's own word for what it is
                    // up to, a directory is only the ground it stands on.
                    string title = Title(info);
                    GUI.color = title.Length > 0 ? SlopWidgets.Dim : SlopWidgets.Off;
                    if (title.Length == 0) title = Ground(info);
                    var line3 = new Rect(row.Text.x, row.Text.y + NameH + SubH,
                        row.Text.width, SubH);
                    SlopWidgets.RowLabel(line3, title);
                    // Only the third line runs long enough to lose anything to the truncation,
                    // so only there is a hover worth answering: the name is the one the agents
                    // window shows and the state is a word.
                    if (title.Length > 0 && SlopWidgets.Wide(title) > line3.width)
                        TooltipHandler.TipRegion(line3, title);

                    GUI.color = Color.white;
                    Click(row, info);
                }

                Absorb();
            }
            finally
            {
                // The anchor with them: line two sets it to the right for the elapsed, and a
                // throw between there and the line that puts it back leaves every label in the
                // frame right-aligned - and vanilla's own StartOfOnGUI logs it once and
                // straightens it, which is a report about this and not about what threw.
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                Drawing = false;
            }
        }

        // The daemon's word for what the agent is doing. A pawn in the column that is not an
        // agent at all reads as down, which is what it is as far as this half is concerned.
        static string Word(AgentState state) => state.ToString().ToLower();

        // How long it has been that way. Off state_since and not last_change: the pane's own
        // clock is reset by every redraw, and a working agent redraws several times a second,
        // so an age taken off it says "0s" for as long as the agent is busy - which is the one
        // state the figure was there for. Nothing before the first move: a session that has
        // always been down has no age, only a state.
        static string Ago(SessionInfo info)
        {
            if (info == null || info.StateSince <= 0) return "";
            long s = (SessionInfo.NowMs - info.StateSince) / 1000L;
            if (s < 0L) return "";
            // A figure that moves every frame is one the eye follows instead of reading, and
            // under a minute the answer is "just now" however it is spelled.
            if (s < 60L) return "<1m";
            if (s < 3600L) return s / 60L + "m";
            if (s < 86400L) return s / 3600L + "h";
            return s / 86400L + "d";
        }

        // What a ghost row says it is. The app's own title first - `less` names the file it
        // is showing, which is the whole of what that row is about - and the session's name
        // when it says nothing, that being what the daemon called it.
        static string Label(SessionInfo info)
        {
            string t = Title(info);
            return t.Length > 0 ? t : info?.Name;
        }

        // What the app called itself over OSC 0/2. Most TUIs state something; the ones that do
        // not get the line below instead, and nothing here parses a pane to guess.
        static string Title(SessionInfo info)
        {
            if (info == null) return "";
            var t = info.Title ?? "";
            var clean = new System.Text.StringBuilder(t.Length);
            // A title arrives as whatever the app wrote. Controls would draw as boxes and a
            // tab would draw as nothing, so both come out as the space they stand for.
            foreach (char c in t)
                clean.Append(char.IsControl(c) ? ' ' : c);
            return clean.ToString().Trim();
        }

        // Where it is, for an agent that says nothing about itself. A temporary agent names no
        // directory it is about to lose.
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

        // The label beside a portrait does what the portrait does, or half the row would be
        // scenery. Over a pane that is switching to the agent - the same thing
        // Patch_BarClickSwitchesTerminal does with the portrait itself - and on the map it is
        // what vanilla does with a bar click, which is to go and look at it.
        static void Click(Row row, SessionInfo info)
        {
            if (row.Session == null || !ColonistBarStrip.Interactive) return;
            if (!Widgets.ButtonInvisible(row.Text, false)) return;

            // The session is what is selected, not the pawn. A row click always sets
            // the current session. On the map a row with a pawn also jumps the camera.
            SessionSelectable.Current = row.Session;

            if (!ColonistBarStrip.Drawing)
            {
                // A ghost has no Thing for Selector to hold. Clear an old pawn selection or
                // MapUIOnGUI would synchronize the session back to that pawn on the next draw.
                Find.Selector.ClearSelection();
                if (row.Pawn == null) return;
                // Cleared first: the brackets' jump-out is an animation off the select time,
                // so a pawn already selected would never replay it.
                CameraJumper.TryJumpAndSelect(row.Pawn);
                return;
            }

            if (info != null && info.Gone) SessionHub.Instance.Start(row.Session);
            else if (row.Session != TerminalWindow.CurrentName) TerminalWindow.Open(row.Session);
        }

        // Belt and braces for the flag DrawFront clears: see Patch_ColonistBarStripLayout.
        public static void EndDraw() => Drawing = false;

        // ------------------------------------------------------------------ menus
        //
        // A heading folds on the left button and opens its project on the right; a row opens
        // its agent's on the right. Both are taken here, from the back pass, because the bar
        // draws between the two and swallows a right-click over a portrait to keep it off the
        // map - so a menu asked for after it would be a menu that never opened over half the
        // row.

        static void Menus()
        {
            if (!ColonistBarStrip.Interactive) return;

            var e = Event.current;
            if (e.rawType != EventType.MouseDown) return;
            if (e.button != 0 && e.button != 1) return;

            // This bypasses Mouse.IsOver's blocked-input gate (see ColonistBarStrip.MouseOver)
            // and keeps a menu from opening under a window stacked over the column.
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
                RowMenu(row.Session);
                e.Use();
                return;
            }
        }

        // Everything the agents window does to one row, where the row already is. Read off
        // the hub rather than off the Row, which carries only what the column draws.
        static void RowMenu(string name)
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
            // A down agent has no pane to show. Start is the row above.
            term.Disabled = !alive;
            opts.Add(term);

            // Nothing in config.toml stands behind a temporary agent, so an edit would write
            // an entry the daemon has never had, and Stop is what removes one.
            if (info != null && !info.Ephemeral)
                opts.Add(new FloatMenuOption("Edit...", () =>
                    TerminalWindow.OpenOverPane(new EditSessionDialog(info))));

            // The one of the two that still means something for a temporary agent: keep this
            // one. Its project is what the copy is for, so with none there is nothing to copy.
            if (info != null && !string.IsNullOrEmpty(info.Project))
                opts.Add(new FloatMenuOption("Duplicate...", () =>
                    TerminalWindow.OpenOverPane(EditSessionDialog.Copy(info))));

            if (info != null && !info.Ephemeral)
                opts.Add(new FloatMenuOption("Remove", () =>
                    TerminalWindow.OpenOverPane(Dialog_MessageBox.CreateConfirmation(
                        $"Remove session '{name}'? This kills the tmux session and drops it " +
                        "from config.toml.",
                        () => hub.Remove(name, SlopWidgets.Fail),
                        destructive: true))));

            TerminalWindow.OpenOverPane(new FloatMenu(opts));
        }

        static void HeadMenu(Head head)
        {
            var hub = SessionHub.Instance;
            var p = hub.Project(head.Label);
            // The loose bucket names no project, so there is nothing to open.
            if (p == null) return;

            string name = p.Name;
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Edit...", () =>
                    TerminalWindow.OpenOverPane(new EditProjectDialog(p))),
                new FloatMenuOption("Duplicate...", () =>
                    TerminalWindow.OpenOverPane(EditProjectDialog.Copy(p))),
                // A shell in the project's directory and *not* in its sandbox: the one thing
                // here that is about the machine rather than about the entry, which is why it
                // says so. The same option is on the heading in the files view.
                new FloatMenuOption("Terminal (host)", () =>
                    hub.RunHostShell(name, session => TerminalWindow.Open(session),
                        SlopWidgets.Fail)),
            };

            // The whole project's agents, not the column's: a down agent is still an entry in
            // config.toml, and the daemon refuses the delete while any of them is.
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

            TerminalWindow.OpenOverPane(new FloatMenu(opts));
        }


        // ------------------------------------------------------------------ resizing
        //
        // The column's own edge is the handle. Held in the settings rather than in a colony,
        // and written on the release rather than on every drag frame - the file is small but
        // it is a file.

        static bool _resizing;
        // Where the edge was relative to the pointer when it was grabbed, so the edge does
        // not jump to the cursor on the first drag frame.
        static float _grab;

        static void Grip()
        {
            float w = Width;
            var grip = new Rect(w - GripW, 0f, GripW * 2f, UI.screenHeight);
            // A dialog opened over the pane mid-drag takes the mouse, and the release lands in
            // it: end the drag here rather than leave the edge stuck to a pointer that has
            // moved on. The width itself was written on every drag frame; only the file was
            // waiting on the release.
            if (!ColonistBarStrip.Interactive && _resizing)
            {
                _resizing = false;
                Settings.S.Write();
            }

            bool over = ColonistBarStrip.Interactive && ColonistBarStrip.MouseOver(grip);
            bool lit = over || _resizing;

            // The panel's edge, and the whole of what says this one can be moved: a pointer
            // the game draws itself is the Tame designator's hand (see DeadCursor), so there
            // is no arrow to swap in.
            Widgets.DrawBoxSolid(new Rect(w - 1f, 0f, lit ? 2f : 1f, UI.screenHeight),
                lit ? SlopWidgets.EdgeLit : SlopWidgets.Edge);

            if (!ColonistBarStrip.Interactive) return;

            var e = Event.current;

            // None of this gesture is read off an event, and the options menu is why. With a
            // dialog over the pane the *press* never reaches this method at all - not Used, not
            // as a stale `rawType`, not at all: the window under an absorbing one is simply not
            // called for a MouseDown, and only MouseUp and the repaints still arrive here. That
            // is what a `rawType` MouseDown latch could never fix, having nothing to latch on.
            //
            // `Input` is the way through. Its flags are the frame's own and nothing on the window
            // stack can Use them - `Patch_Root_Update` already reads `GetMouseButtonDown` for the
            // cursor for exactly that reason - and Grip runs every frame the column is drawn, so
            // the press is *sampled* rather than received. The pointer comes off the event, which
            // is right on any of them, repaints included.
            //
            // The rest follows from the same rule, and it is the shape vanilla's own resize grip
            // (`WindowResizer`) uses: latch, follow the pointer, end when the button is up. Which
            // also closes a release out past the edge of the screen, that being an event nobody
            // ever gets. `MouseDrag` is not consulted anywhere: the game will not trust it either,
            // `UnityGUIBugsFixer.MouseDrag` polling `GetMouseButton` outright on a Linux build.
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

            // The press and the release still get Used when they *do* arrive, so a grab on the
            // edge is not also a click on the map behind the column. On the frames they never
            // arrive there is nothing below to take them.
            if (e.rawType == EventType.MouseDown || e.rawType == EventType.MouseUp
                || e.rawType == EventType.MouseDrag)
                e.Use();
        }

        static void SetWidth(float w)
        {
            Settings.S.sidebarWidth = Mathf.Clamp(w, MinWidth, MaxWidth);
            // The inspect pane is moved on open and on a resolution change, so with one up it
            // has to be told, or the column crosses it until it is next opened.
            Patch_MainTabWindowShift.Reposition();
            // The options menu needs no telling: it is content in the chrome's own body now,
            // and that rect is measured off this width every frame.
        }

        // The column is a fifth of the screen taken off the map, and the map is what handles
        // whatever the portraits and the rows did not: without this, a press on the panel
        // starts a drag-selection over the ground behind it and a right-click orders a
        // colonist to walk there. Last, so the rows have already had their refusal, and both
        // buttons, since a move order is the more surprising of the two.
        //
        // The press and nothing else. A drag and its release belong to whoever the press went
        // to - ReorderableWidget settles a dragged portrait on a later pass of its own, and a
        // MouseUp eaten here would strand one mid-drag.
        //
        // The add strip needs no exception any more: AddClick is asked in the back pass,
        // which is before this one, and Uses the press it takes.
        //
        // Not while something is stacked over a pane - that is the same frame the strip stops
        // listening in, and the window on top is the one the click belongs to. The options
        // menu is the exception: it opens inside the chrome, and the sidebar stays visible
        // and interactive.
        static void Absorb()
        {
            if (!ColonistBarStrip.Interactive) return;

            var e = Event.current;
            if (e.rawType != EventType.MouseDown) return;
            if (!ColonistBarStrip.MouseOver(Panel)) return;
            e.Use();
        }

        // Asked only past the Suppressed check in DrawBack, that being the one condition a
        // pass declines to draw under without the column having gone anywhere.
        static bool Wanted()
        {
            // The bar hides itself on a small screen and behind the tile picker, and a panel
            // with no portraits in it is worse than no panel.
            return ColonistBarStrip.BarShown && !Cutscene.Playing;
        }
    }

    // Vanilla writes the pawn's name across the bottom of its cell. In the column the cell is
    // a portrait's width and the name lives beside it, so the two would be drawn over each
    // other. Only while our own passes are up: this is the same call the map labels go
    // through.
    [HarmonyLib.HarmonyPatch(typeof(GenMapUI), nameof(GenMapUI.DrawPawnLabel),
        new[] { typeof(Pawn), typeof(Vector2), typeof(float), typeof(float),
                typeof(Dictionary<string, string>), typeof(GameFont), typeof(bool),
                typeof(bool) })]
    public static class Patch_SidebarPawnLabel
    {
        static bool Prefix() => !AgentSidebar.Drawing;
    }
}
