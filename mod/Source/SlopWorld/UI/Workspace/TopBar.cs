using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Draw the bar from the map layer so it stays behind windows and remains interactive over
    // them. The readout itself is only the quota/clock renderer. Keeping this component here
    // leaves the dependency pointing from the bar to the readout.
    public class TopBarMapComponent : MapComponent
    {
        public TopBarMapComponent(Map map)
            : base(map) { }

        public override void MapComponentOnGUI()
        {
            if (Cutscene.Playing)
                return;
            TopBar.DrawOnMap();
        }
    }

    public static class TopBar
    {
        // Fit the current row height while preserving the shipped 26px minimum.
        public static float H => Mathf.Max(UiTheme.RowH, UiMetrics.Compact ? 24f : 26f);

        static float Pad => UiTheme.GapS;

        // Door icons use the shared glyph size. ThingIcon already fills its slot more densely.
        const float IconW = UiTheme.IconW;

        // Give the jukebox tip a stable id so changing song text does not restart its fade.
        const int JukeboxTipId = 0x51_0C_02;

        struct DoorLayout
        {
            public Rect Config;
            public Rect Jukebox;
            public Rect Core;
            public bool HasConfig;
            public bool HasJukebox;
            public bool HasCore;
            public float Right;
            public string JukeboxTip;
        }

        static DoorLayout _doors;
        static int _doorsFrame = -1;
        static Rect _doorsRect;
        static Map _doorsMap;
        static bool _doorsShowJukebox,
            _doorsShowCore;
        static bool _doorsReady;

        public static Rect Rect => WorkspaceLayout.Current.TopBar;

        // From the map component above, which sits behind every window.
        public static void DrawOnMap()
        {
            if (Find.WindowStack?.WindowOfType<WorkspaceWindow>() != null)
                return;
            Draw(true);
        }

        public static void Draw(bool interactive)
        {
            long started = PerfTrace.Start();
            try
            {
                using (WidgetState.Save())
                    DrawCore(interactive);
            }
            finally
            {
                PerfTrace.End("topbar", started, 1);
            }
        }

        static void DrawCore(bool interactive)
        {
            if (!WorkspaceVisibility.Shown || WorkspaceVisibility.Hidden)
                return;
            if (Event.current.type == EventType.Layout)
                return;

            var r = Rect;
            Slab.Fill(r, UiTheme.Panel);
            // Keep the hairline inside the bar so adjoining chrome shares its boundary pixel.
            Slab.Hairline(new Rect(r.x, r.yMax - 1f, r.width, 1f), UiTheme.Edge);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;

            // Lay out fixed doors first. Usage rows then consume the remaining width.
            float right = Doors(r, interactive);

            string clockPosition = Settings.StatusbarClockPosition;
            float statusRight = r.center.x - Pad;
            float resources = r.center.x + Pad;
            bool centeredClock = false;

            if (clockPosition == StatusbarClockMode.Center)
            {
                DateTime now = UsageReadout.ClockNow();
                float clockWidth = UsageReadout.ClockWidth(now);
                float clockX = r.center.x - clockWidth / 2f;
                if (clockX >= r.x + Pad && clockX + clockWidth <= right - Pad)
                {
                    var clockColor = GUI.color;
                    GUI.color = UiTheme.Name;
                    UsageReadout.DrawClock(new Rect(clockX, r.y, clockWidth, r.height), now);
                    GUI.color = clockColor;
                    statusRight = clockX - Pad;
                    resources = clockX + clockWidth + Pad;
                    centeredClock = true;
                }
            }

            // Omit usage when the doors leave no room. A centered clock owns its own gap.
            // right mode keeps the original quota-strip layout.
            float quota = right - resources;
            bool showResources =
                quota > 0f
                && (Settings.StatusbarUsage || clockPosition == StatusbarClockMode.Right);
            if (showResources)
                UsageReadout.DrawStrip(
                    new Rect(resources, r.y, quota, r.height),
                    Settings.StatusbarUsage,
                    clockPosition == StatusbarClockMode.Right
                );

            bool centeredSummary = Settings.StatusbarSummaryPosition == StatusbarSummaryMode.Center;
            float summaryRight =
                centeredClock ? statusRight
                : showResources ? resources
                : right;
            Status(
                interactive,
                new Rect(r.x + Pad, r.y, Mathf.Max(0f, statusRight - r.x - Pad), r.height),
                centeredSummary,
                summaryRight
            );

            // The line is drawn over the map, and the map takes whatever the buttons did not.
            // Without this a press here starts a drag-selection on the ground behind it. Last, so
            // the buttons have already had their refusal.
            if (interactive)
                Absorb(r);
        }

        // Consume only the initial press. The drag and release belong to its original target.
        static void Absorb(Rect r)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown)
                return;
            if (!Mouse.IsOver(r))
                return;
            e.Use();
        }

        // Place config and any map objects with menus right-to-left. Return quota's limit.
        // Map object lookup and geometry are shared by all IMGUI events in a frame. The door
        // draw still runs for every event below so hover, tooltips, and clicks remain live.
        static float Doors(Rect r, bool live)
        {
            PrepareDoors(r);
            if (_doors.HasConfig)
                Door(_doors.Config, Icons.Config, "Settings", ModOptions.Toggle, live);
            if (_doors.HasCore)
                Thing(_doors.Core, ModDefOf.Ship_ComputerCore, default, CoreTip.OpenMenu, live);
            if (_doors.HasJukebox)
                Thing(
                    _doors.Jukebox,
                    ModDefOf.SlopJukebox,
                    new TipSignal(_doors.JukeboxTip, JukeboxTipId),
                    Jukebox.OpenMenu,
                    live
                );
            return _doors.Right;
        }

        static void PrepareDoors(Rect r)
        {
            var map = Find.CurrentMap;
            bool showJukebox = Settings.StatusbarJukebox;
            bool showCore = Settings.StatusbarGM;
            if (
                _doorsReady
                && _doorsFrame == Time.frameCount
                && _doorsRect == r
                && _doorsMap == map
                && _doorsShowJukebox == showJukebox
                && _doorsShowCore == showCore
            )
                return;

            float x = r.xMax;
            _doors = new DoorLayout();
            _doors.HasConfig = TrySlot(r, ref x, out _doors.Config);

            if (showCore && CoreTip.On(map) && TrySlot(r, ref x, out _doors.Core))
            {
                _doors.HasCore = true;
            }

            if (showJukebox && Jukebox.On(map) && TrySlot(r, ref x, out _doors.Jukebox))
            {
                _doors.JukeboxTip = Jukebox.IconTip();
                _doors.HasJukebox = true;
            }

            _doors.Right = Mathf.Max(r.x, x - UiTheme.GapM);
            _doorsFrame = Time.frameCount;
            _doorsRect = r;
            _doorsMap = map;
            _doorsShowJukebox = showJukebox;
            _doorsShowCore = showCore;
            _doorsReady = true;
        }

        // Omit doors that cannot fit; drawing and hit testing share these bounded slots.
        static bool TrySlot(Rect bar, ref float right, out Rect slot)
        {
            slot = default;
            float x = right - bar.height;
            if (x < bar.x || bar.height < IconW)
                return false;
            slot = new Rect(x, bar.y, bar.height, bar.height);
            right = x;
            return true;
        }

        // Full-height targets keep the shared glyph size centered inside their hover face.
        static Rect DoorIconRect(Rect slot) =>
            new Rect(slot.center.x - IconW / 2f, slot.center.y - IconW / 2f, IconW, IconW);

        // Keep map-layer hit testing while sharing the standard icon button styling.
        static void Door(Rect r, Texture2D icon, string tip, System.Action go, bool live)
        {
            TooltipHandler.TipRegion(r, tip);

            bool over = ColonistBarStrip.Hover(r);
            bool repaint = Event.current != null && Event.current.type == EventType.Repaint;

            var was = GUI.color;
            if (repaint)
            {
                UiLayout.IconButtonBackground(r, over, over && Input.GetMouseButton(0));
                GUI.color = over ? UiTheme.Lead : UiTheme.Off;
                GUI.DrawTexture(DoorIconRect(r), icon);
                GUI.color = was;
            }

            Press(over, go, live);
        }

        // ThingIcon supplies its own tint. This method adds hover, input, and optional tips.
        static void Thing(Rect r, ThingDef def, TipSignal tip, System.Action go, bool live)
        {
            if (def == null)
                return;

            if (!string.IsNullOrEmpty(tip.text))
                TooltipHandler.TipRegion(r, tip);

            bool over = ColonistBarStrip.Hover(r);
            bool repaint = Event.current != null && Event.current.type == EventType.Repaint;

            // Both of these read the ambient color and only one of them puts it back, so the pair
            // is bracketed. The highlight would wear whatever the last thing on the line left
            // behind, and ThingIcon hands back the def's own tint.
            var was = GUI.color;
            if (repaint)
            {
                GUI.color = Color.white;
                UiLayout.IconButtonBackground(r, over, over && Input.GetMouseButton(0));
                Widgets.ThingIcon(DoorIconRect(r), def);
                GUI.color = was;
            }

            Press(over, go, live);
        }

        // Use direct rect hit-testing because this map component may draw over an absorbing window.
        static void Press(bool over, System.Action go, bool live)
        {
            if (!over || !live)
                return;

            var e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0)
                return;

            e.Use();
            go();
        }

        // Show the pane's session, or the selected inspect-pane agent when no pane is open.
        static void Status(bool interactive, Rect r, bool centered, float summaryRight)
        {
            if (r.width <= 40f)
                return;

            // Content views replace the session status while visible.
            var view = WorkspaceWindow.Showing;
            if (view != null)
            {
                var native = FilesView.CurrentViewerInfo;
                if (native != null)
                {
                    ContentStatus(
                        r,
                        native,
                        centered,
                        summaryRight,
                        interactive,
                        hasDaemonSession: false
                    );
                    return;
                }
                GUI.color = UiTheme.Lead;
                UiText.RowLabel(
                    r,
                    view.Title,
                    centered ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft
                );
                GUI.color = Color.white;
                return;
            }

            string session = WorkspaceWindow.CurrentName ?? InspectPaneAgent.SelectedSession();
            var hub = SessionHub.Instance;

            if (session == null)
            {
                GUI.color = UiTheme.Dim;
                UiText.RowLabel(
                    r,
                    hub.Online
                        ? $"{hub.Sessions.Count} agent{(hub.Sessions.Count == 1 ? "" : "s")}"
                        : $"daemon {hub.Status}",
                    centered ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft
                );
                GUI.color = Color.white;
                return;
            }

            var info = hub.Get(session);
            if (info != null && ContentStatus(r, info, centered, summaryRight, interactive))
                return;
            StatusMenu(r, centered, summaryRight, interactive, info);
            var state = info?.State ?? AgentState.Down;
            string tail = Tail(session, state);

            if (centered)
            {
                CenteredSummary(r, session, state, tail, summaryRight);
                return;
            }

            float iconW = ReaderRowChrome.IconW;
            var mark = new Rect(r.x, r.y + (r.height - iconW) / 2f, iconW, iconW);
            GUI.color = AgentPresentation.AgentStateColor(state);
            GUI.DrawTexture(mark, Icons.Agents);
            float w = Mathf.Min(
                UiTheme.Wide(session) + UiTheme.GapXS,
                Mathf.Max(0f, r.width - iconW - UiTheme.GapXS - UiTheme.GapS)
            );
            var name = new Rect(mark.xMax + UiTheme.GapXS, r.y, w, r.height);
            UiText.RowLabel(name, session);

            var rest = new Rect(
                name.xMax + UiTheme.GapS,
                r.y,
                Mathf.Max(0f, r.xMax - name.xMax - UiTheme.GapS),
                r.height
            );
            if (rest.width <= 20f)
            {
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                return;
            }

            Text.Font = GameFont.Tiny;
            GUI.color = UiTheme.Dim;
            UiText.RowLabel(rest, tail);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        struct CenteredStatusLayout
        {
            internal Rect Icon,
                Title,
                Summary;
        }

        // Geometry is shared; agent and reader callers own their colors and summary drawing.
        static CenteredStatusLayout CenteredLayout(
            Rect r,
            string title,
            float summaryWidth,
            float summaryRight
        )
        {
            float iconW = ReaderRowChrome.IconW;
            float titleW = Mathf.Min(
                UiTheme.Wide(title) + UiTheme.GapXS,
                Mathf.Max(0f, r.width - iconW - UiTheme.GapXS - UiTheme.GapS)
            );
            float summaryLeft = r.x + iconW + UiTheme.GapXS + titleW + UiTheme.GapS;
            float width = Mathf.Min(summaryWidth, Mathf.Max(0f, summaryRight - summaryLeft));
            float center = Mathf.Clamp(
                UI.screenWidth / 2f,
                summaryLeft + width / 2f,
                summaryRight - width / 2f
            );
            return new CenteredStatusLayout
            {
                Icon = new Rect(r.x, r.y + (r.height - iconW) / 2f, iconW, iconW),
                Title = new Rect(r.x + iconW + UiTheme.GapXS, r.y, titleW, r.height),
                Summary = new Rect(center - width / 2f, r.y, width, r.height),
            };
        }

        // Center only the summary; the agent icon and name stay anchored at the left.
        static void CenteredSummary(
            Rect r,
            string session,
            AgentState state,
            string tail,
            float summaryRight
        )
        {
            Text.Font = GameFont.Tiny;
            float summaryWidth = UiTheme.Wide(tail);
            Text.Font = GameFont.Small;
            var layout = CenteredLayout(r, session, summaryWidth, summaryRight);
            GUI.color = AgentPresentation.AgentStateColor(state);
            GUI.DrawTexture(layout.Icon, Icons.Agents);
            UiText.RowLabel(layout.Title, session);
            if (layout.Summary.width > 0f)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = UiTheme.Dim;
                UiText.RowLabel(layout.Summary, tail, TextAnchor.MiddleCenter);
                Text.Font = GameFont.Small;
            }
            GUI.color = Color.white;
        }

        static void CenteredContent(
            Rect r,
            PanelStatus status,
            float summaryRight,
            Texture2D icon,
            Color tint
        )
        {
            Text.Font = GameFont.Tiny;
            float summaryWidth = UiTheme.Wide(status.Context) + RootWidth(status.ShowMainCheckout);
            Text.Font = GameFont.Small;
            var layout = CenteredLayout(r, status.Title, summaryWidth, summaryRight);
            GUI.color = tint;
            GUI.DrawTexture(layout.Icon, icon);
            GUI.color = UiTheme.Lead;
            UiText.RowLabel(layout.Title, status.Title);
            Text.Font = GameFont.Tiny;
            GUI.color = UiTheme.Dim;
            ContentContext(layout.Summary, status.Context, status.ShowMainCheckout);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        // Content labels use captured source metadata, including native readers that have no
        // daemon session. Agents retain their state-colored icon and runtime summary above.
        static bool ContentStatus(
            Rect r,
            SessionInfo info,
            bool centered,
            float summaryRight,
            bool interactive,
            bool hasDaemonSession = true
        )
        {
            var action = SessionRowAction.Of(info);
            if (action == RowAct.None && !info.Host && !info.Ephemeral)
                return false;
            var project = SidebarScopes.Project(
                string.IsNullOrEmpty(info.ReaderScope) ? info.Project : info.ReaderScope
            );
            var scope = SidebarScopes.Find(PanelStatus.ScopeKey(info, project));
            var status = PanelStatus.From(
                info,
                action,
                project,
                scope,
                hasDaemonSession,
                soleMainCheckout: SoleMainCheckout(project)
            );
            if (status == null)
                return false;
            var reader = FileReaders.Describe(info);
            Texture2D icon = ReaderRowChrome.Icon(reader) ?? Icons.Terminal;
            Color tint =
                status.Kind == PanelStatusKind.Terminal
                    ? UiTheme.Off
                    : ReaderRowChrome.Tint(action);
            var hit = StatusMenu(r, centered, summaryRight, interactive, info);
            TooltipHandler.TipRegion(hit, status.Tooltip);
            if (centered)
            {
                CenteredContent(r, status, summaryRight, icon, tint);
                return true;
            }
            float iconW = ReaderRowChrome.IconW;
            GUI.color = tint;
            GUI.DrawTexture(new Rect(r.x, r.y + (r.height - iconW) / 2f, iconW, iconW), icon);
            GUI.color = UiTheme.Lead;
            float left = r.x + iconW + UiTheme.GapXS;
            // Reserve context room when available, while keeping the content name dominant.
            float available = Mathf.Max(0f, r.xMax - left);
            float titleW = Mathf.Min(
                UiTheme.Wide(status.Title) + UiTheme.GapXS,
                Mathf.Max(0f, available - RootWidth(status.ShowMainCheckout) - UiTheme.GapS)
                    * (string.IsNullOrEmpty(status.Context) ? 1f : 0.6f)
            );
            UiText.RowLabel(new Rect(left, r.y, titleW, r.height), status.Title);
            Text.Font = GameFont.Tiny;
            GUI.color = UiTheme.Dim;
            left += titleW + UiTheme.GapS;
            ContentContext(
                new Rect(left, r.y, Mathf.Max(0f, r.xMax - left), r.height),
                status.Context,
                status.ShowMainCheckout
            );
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            return true;
        }

        // Match the workspace filter's selected scopes, independently for each project.
        static bool SoleMainCheckout(ProjectInfo project)
        {
            if (project == null)
                return false;
            bool main = false;
            foreach (var scope in SidebarScopes.EnabledScopes())
            {
                if (scope.ProjectId != project.Id)
                    continue;
                if (scope.Worktree != "main")
                    return false;
                main = true;
            }
            return main;
        }

        static Rect StatusMenu(
            Rect r,
            bool centered,
            float summaryRight,
            bool interactive,
            SessionInfo info
        )
        {
            var hit = new Rect(
                r.x,
                r.y,
                Mathf.Max(0f, (centered ? summaryRight : r.xMax) - r.x),
                r.height
            );
            var e = Event.current;
            if (
                interactive
                && info != null
                && e.type == EventType.MouseDown
                && e.button == 1
                && hit.Contains(e.mousePosition)
            )
            {
                e.Use();
                Sidebar.OpenItemMenu(info);
            }
            return hit;
        }

        static float RootWidth(bool main) => main ? UiTheme.RootIconW + UiTheme.GapXS : 0f;

        static void ContentContext(Rect r, string text, bool main)
        {
            float rootW = RootWidth(main);
            if (r.width < rootW || r.width <= 0f)
                return;
            if (main)
            {
                float side = rootW - UiTheme.GapXS;
                GUI.DrawTexture(
                    new Rect(r.x, r.y + (r.height - side) / 2f, side, side),
                    Icons.Root
                );
                r.x += rootW;
                r.width -= rootW;
            }
            UiText.RowLabel(r, text);
        }

        // Prefer slopd's task title, falling back to the pane's OSC title.
        static string Tail(string session, AgentState state)
        {
            string path = FilesView.ViewerPath(session);
            if (!string.IsNullOrEmpty(path))
            {
                var hub = SessionHub.Instance;
                string project = hub.Get(session)?.Project;
                string root = string.IsNullOrEmpty(project)
                    ? null
                    : hub.Project(project)?.ExpandedDir;
                return PagerCommands.RelativeFilePath(root, path);
            }
            string title = SessionHub.Instance.Get(session)?.Title;
            return string.IsNullOrEmpty(title) ? state.ToString().ToLowerInvariant() : title;
        }
    }
}
