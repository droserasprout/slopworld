using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public static partial class AgentSidebar
    {
        public const float MinWidth = 150f;
        public const float MaxWidth = 460f;

        public static float Width => Mathf.Clamp(Settings.SidebarWidth, MinWidth,
            Mathf.Max(MinWidth, Mathf.Min(MaxWidth, UI.screenWidth * 0.4f)));

        // Size square portraits from the text row, then clamp them so the cached texture is
        // neither upsampled nor reduced to a thumbnail.
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
        // Portraits touch the screen edge; CellX remains the inset for labels and chrome.
        const float PortraitX = 0f;
        const float CellX = SlopWidgets.GapS;
        const float TextGap = SlopWidgets.GapS;

        // The shared tabs/filter row is followed by an optional right-aligned view-action row;
        // `TabH` includes both when present.
        static float TabRowH => TopBar.H;
        public static float TabH => TabRowH + (HasActions ? TabRowH : 0f);
        const float TabIcon = 20f;

        const float RowGap = SlopWidgets.GapXS;

        static float GhostH => NameH + 2f;

        static float NameH => SlopWidgets.LineHOf(GameFont.Small);
        static float SubH => SlopWidgets.TinyH;

        static float TextH => NameH + SubH * 2f;

        const float BellW = 13f;

        // The state badge is a share of the portrait rather than a fixed size: the column
        // shrinks to fit and a marker that did not would swallow a small face. Keep its own
        // smaller floor so the circle does not dominate a compact portrait.
        const float BadgeShare = 0.18f;
        const float BadgeMin = 6f;
        const float BadgeInset = 1f;
        const float BadgeRing = 1.5f;
        const float BadgeAlpha = 0.8f;

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
            public Rect Face;   // the square face box, read by the drawer patch

            public bool Ghost;
        }

        struct Head
        {
            public string Label;
            public Rect Rect;   // the whole band, so the arrow and the name click as one
            public int Count;   // what is under it, which is the only thing a fold hides
            public bool Folded;
        }

        static readonly SidebarLayout Layout = new SidebarLayout();

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

        public static SidebarTab CurrentTab => ParseTab(Settings.SidebarTab);

        static SidebarTab ParseTab(string value)
        {
            switch (value)
            {
                case "files": return SidebarTab.Files;
                case "search": return SidebarTab.Search;
                case "git": return SidebarTab.Git;
                case "shortcuts": return SidebarTab.Shortcuts;
                default: return SidebarTab.Agents;
            }
        }

        static string TabName(SidebarTab tab)
        {
            switch (tab)
            {
                case SidebarTab.Files: return "files";
                case SidebarTab.Search: return "search";
                case SidebarTab.Git: return "git";
                case SidebarTab.Shortcuts: return "shortcuts";
                default: return "agents";
            }
        }

        // Which views own a second row. Keep in step with what [Actions] draws.
        static bool HasActions => CurrentTab == SidebarTab.Files
            || CurrentTab == SidebarTab.Search
            || CurrentTab == SidebarTab.Git;

        // Empty means all projects. Unknown project keys show no rows while the daemon list is
        // incomplete; the no-project bucket is a normal filter key.
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
            if (CurrentTab == SidebarTab.Search) SearchView.Search();
            else if (CurrentTab == SidebarTab.Git) GitView.Refresh();
        }

        static void Show(SidebarTab tab)
        {
            // Focusing Git is also the user's way to ask what changed since the last
            // focus, including when Git is already the selected tab.
            if (CurrentTab == tab)
            {
                if (tab == SidebarTab.Git) GitView.Refresh();
                return;
            }

            if (tab != SidebarTab.Files) FilesView.ClearFocus();
            if (tab != SidebarTab.Files) FilesView.ReleaseViewer();
            if (tab != SidebarTab.Search)
            {
                SearchView.ReleaseViewer();
                SearchView.ReleaseFocus();
            }
            if (tab != SidebarTab.Git) GitView.ReleaseViewer();

            var s = Settings.S;
            s.sidebarTab = TabName(tab);
            s.Write();

            if (tab == SidebarTab.Git) GitView.Refresh();
            else if (tab == SidebarTab.Files) GitView.Entered();

            if (tab == SidebarTab.Search) SearchView.Entered();

            if (tab == SidebarTab.Shortcuts) SessionHub.Instance.RefreshShortcuts();
        }

        static RowAct RoutedAction(SessionInfo info) => RowActions.Of(info);

        public static bool IsRouted(SessionInfo info)
        {
            RowAct act = RoutedAction(info);
            return (act & (RowAct.View | RowAct.Edit | RowAct.Diff)) != 0;
        }

        static bool InTab(SessionInfo info, SidebarTab tab)
        {
            RowAct act = RoutedAction(info);
            return tab == SidebarTab.Files
                ? (act & (RowAct.View | RowAct.Edit)) != 0
                : tab == SidebarTab.Git && (act & RowAct.Diff) != 0;
        }

        static List<SessionInfo> RoutedFor(SidebarTab tab)
        {
            Layout.Routed.Clear();
            foreach (var info in SessionHub.Instance.Sessions)
                if (InTab(info, tab) && Passes(info.Project)) Layout.Routed.Add(info);
            Layout.Routed.Sort(ByName);
            return Layout.Routed;
        }

        public static float RoutedHeight(SidebarTab tab) => RoutedFor(tab).Count * GhostH;

        public static Rect TreeBody(Rect body, SidebarTab tab) =>
            new Rect(body.x, body.y + RoutedHeight(tab), body.width,
                Mathf.Max(0f, body.height - RoutedHeight(tab)));

        static void DrawAgentShadow()
        {
            if (Layout.AgentContentH <= Body.height) return;

            const int Steps = 4;
            const float Height = 12f;
            float band = Height / Steps;
            var add = AddBar;
            for (int i = 0; i < Steps; i++)
            {
                float strength = 0.12f + 0.12f * i;
                Slab.Fill(new Rect(0f, add.y - Height + i * band, Width, band),
                    SlopWidgets.Fade(SlopWidgets.Scrim, strength));
            }
        }

        // Draw the status badge at the portrait's right edge, vertically aligned with the
        // lower text band; the dark ring keeps it legible over hair and clothing.
        static void DrawStateBadge(Rect face, Rect text, AgentState state)
        {
            if (face.width <= 0f) return;

            float d = Mathf.Max(BadgeMin,
                Mathf.Round(face.width * BadgeShare));
            var portrait = Patch_SidebarPortraitDraw.PortraitRect(face);
            var center = new Vector2(portrait.xMax - d / 2f - BadgeInset,
                text.y + NameH + SubH * 1.5f + 3f);

            GUI.color = SlopWidgets.ViewBg;
            GUI.DrawTexture(Icons.DotBox(center, d + BadgeRing * 2f), Icons.Dot);
            var stateColor = TerminalWindow.StateColor(state);
            GUI.color = new Color(stateColor.r, stateColor.g, stateColor.b, BadgeAlpha);
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

        // Ghosts have only one line, so give the eye one strong answer and one quiet piece of
        // context instead of dimming the whole row. Routed file actions do not use the pane's
        // native title: a shell commonly calls itself `bash` or `less`, which says less than
        // the action and the session name the daemon already gave us.
        static void DrawGhostLabel(Rect r, SessionInfo info, string fallback,
                                    bool hostIcon = false)
        {
            RowAct act = RowActions.Of(info);
            string title = GhostTitle(info, fallback, act);
            string context = GhostContext(info, title, act);

            if (hostIcon && info?.Ephemeral == true)
            {
                float d = Mathf.Min(GhostMarkW, r.height);
                var icon = new Rect(r.x, r.y + (r.height - d) / 2f, d, d);
                GUI.color = SlopWidgets.Off;
                GUI.DrawTexture(icon, Icons.Terminal);
                string project = info.Project ?? "";
                TooltipHandler.TipRegion(icon, project.Length > 0
                    ? "Host session in " + project
                    : "Host session");
                r.x += d + 4f;
                r.width -= d + 4f;
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            if (context.Length == 0)
            {
                GUI.color = SlopWidgets.Lead;
                SlopWidgets.RowLabel(r, title);
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            float contextW = Mathf.Min(SlopWidgets.Wide(context), r.width * 0.42f);
            var quiet = new Rect(r.xMax - contextW, r.y, contextW, r.height);
            var strong = new Rect(r.x, r.y, Mathf.Max(0f, quiet.x - SlopWidgets.GapS - r.x),
                r.height);

            GUI.color = SlopWidgets.Lead;
            SlopWidgets.RowLabel(strong, title);
            GUI.color = SlopWidgets.Dim;
            SlopWidgets.RowLabel(quiet, context);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        static string GhostTitle(SessionInfo info, string fallback, RowAct act)
        {
            if (act != RowAct.None)
            {
                string name = info?.Name ?? fallback;
                string prefix = ActionWord(act) + "-";
                string subject = name.StartsWith(prefix, System.StringComparison.Ordinal)
                    ? name.Substring(prefix.Length)
                    : name;
                return ActionWord(act) + " " + subject;
            }

            string title = Title(info);
            return title.Length > 0 ? title : info?.Name ?? fallback;
        }

        static string GhostContext(SessionInfo info, string title, RowAct act)
        {
            string project = info?.Project ?? "";
            if (act != RowAct.None)
                return project;

            string name = info?.Name ?? "";
            if (title != name && name.Length > 0)
            {
                if (project.Length > 0) return name + "  ·  " + project;
                return name;
            }
            if (info?.Ephemeral == true) return project;
            return project;
        }

        static string ActionWord(RowAct act)
        {
            switch (act)
            {
                case RowAct.View: return "view";
                case RowAct.Edit: return "edit";
                case RowAct.Diff: return "diff";
                default: return "";
            }
        }

        // Replace control or unsupported title characters before drawing; Unity otherwise
        // advances for missing glyphs and leaves unexplained gaps.
        static string Title(SessionInfo info)
        {
            if (info == null) return "";
            var t = string.IsNullOrWhiteSpace(info.Label) ? info.Title : info.Label;
            t = t ?? "";
            var font = Text.CurFontStyle?.font;
            var clean = new System.Text.StringBuilder(t.Length);
            foreach (char c in t)
                clean.Append(char.IsControl(c) || (font != null && !font.HasCharacter(c))
                    ? ' '
                    : c);
            var title = clean.ToString().Trim();
            // Ignore default OSC host titles: they are environment metadata, not task titles.
            if (title.Length == 0 || IsHostTitle(title)) return "";
            return title;
        }

        static bool IsHostTitle(string title)
        {
            return string.Equals(title, System.Environment.MachineName,
                       System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(title, "localhost", System.StringComparison.OrdinalIgnoreCase);
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
        public static void EndDraw()
        {
            EndAgentScroll();
            Patch_SidebarPortraitDraw.ClearDeferredSelection();
            Drawing = false;
        }


        static void Menus()
        {
            if (!ColonistBarStrip.Interactive) return;

            var e = Event.current;
            // Fixed chrome runs first. Respect a press it consumed; rawType intentionally
            // survives Use(), and could otherwise be reinterpreted after entering the
            // agent scroll group's local coordinate space.
            if (e.type != EventType.MouseDown) return;
            if (e.button != 0 && e.button != 1) return;

            foreach (var head in Layout.Heads)
            {
                if (!ColonistBarStrip.MouseOver(head.Rect)) continue;
                if (e.button == 0) Fold(head.Label, !head.Folded);
                else HeadMenu(head);
                e.Use();
                return;
            }

            if (e.button != 1) return;
            foreach (var row in Layout.Rows)
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

            if (info != null)
                opts.Add(new FloatMenuOption("Label", () =>
                {
                    var current = hub.Get(name);
                    if (current != null) LabelDialog.Open(name, current.Label);
                }));

            if (info != null && !info.Ephemeral)
                opts.Add(new FloatMenuOption("Edit...", () =>
                    TerminalWindow.OpenOverPane(new EditSessionDialog(info))));

            if (info != null && !string.IsNullOrEmpty(info.Project))
                opts.Add(new FloatMenuOption("Duplicate...", () =>
                    TerminalWindow.OpenOverPane(EditSessionDialog.Copy(info))));

            if (info != null && !info.Ephemeral)
                opts.Add(new FloatMenuOption("Remove", () =>
                    TerminalWindow.OpenOverPane(SlopConfirmDialog.Create(
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
                () => TerminalWindow.OpenOverPane(SlopConfirmDialog.Create(
                    $"Remove project '{name}'? The directory is left alone; only the entry " +
                    "in config.toml goes.",
                    () => hub.RemoveProject(name, SlopWidgets.Fail),
                    destructive: true)));
            del.Disabled = agents > 0;
            opts.Add(del);

            TerminalWindow.OpenOverPane(new SlopMenu(opts));
        }



        static void Grip()
        {
            float w = Width;
            var grip = new Rect(w - GripW, 0f, GripW * 2f, UI.screenHeight);
            if (!ColonistBarStrip.Interactive && Layout.Resizing)
            {
                Layout.Resizing = false;
                Settings.S.Write();
            }

            bool over = ColonistBarStrip.SidebarHover(grip);
            bool lit = over || Layout.Resizing;

            // The panel's right edge and the grip's own tell are the same line, and it is
            // drawn here alone: a second draw of [SlopWidgets.Edge] over this one composites
            // into a heavier boundary than the palette's, on the sidebar only. At rest it is
            // one screen pixel like every other rule; lit it is a bar and may be a GUI one.
            if (lit) Slab.Fill(new Rect(w - 1f, 0f, 2f, UI.screenHeight), SlopWidgets.EdgeLit);
            else Slab.VHairline(new Rect(w - 1f, 0f, 1f, UI.screenHeight), SlopWidgets.Edge);

            if (!ColonistBarStrip.Interactive) return;

            var e = Event.current;

            // Poll Input: an absorbing window can prevent this layer from receiving MouseDown.
            if (!Layout.Resizing)
            {
                if (!over || !Input.GetMouseButtonDown(0)) return;
                Layout.Resizing = true;
                Layout.Grab = w - e.mousePosition.x;
            }
            else if (Input.GetMouseButton(0))
            {
                SetWidth(e.mousePosition.x + Layout.Grab);
            }
            else
            {
                Layout.Resizing = false;
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
