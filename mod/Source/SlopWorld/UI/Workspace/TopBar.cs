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
        public TopBarMapComponent(Map map) : base(map) { }

        public override void MapComponentOnGUI()
        {
            if (Cutscene.Playing) return;
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
        static bool _doorsShowJukebox, _doorsShowCore;
        static bool _doorsReady;

        public static Rect Rect => WorkspaceLayout.Current.TopBar;

        // From the map component above, which sits behind every window.
        public static void DrawOnMap()
        {
            if (Find.WindowStack?.WindowOfType<TerminalWindow>() != null) return;
            Draw(true);
        }

        public static void Draw(bool interactive)
        {
            long started = PerfTrace.Start();
            try
            {
                using (WidgetState.Save()) DrawCore(interactive);
            }
            finally
            {
                PerfTrace.End("topbar", started, 1);
            }
        }

        static void DrawCore(bool interactive)
        {
            if (!UiLayout.Shown || UiLayout.Hidden) return;
            if (Event.current.type == EventType.Layout) return;

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
            bool showResources = quota > 0f && (Settings.StatusbarUsage
                || clockPosition == StatusbarClockMode.Right);
            if (showResources)
                UsageReadout.DrawStrip(new Rect(resources, r.y, quota, r.height),
                    Settings.StatusbarUsage, clockPosition == StatusbarClockMode.Right);

            bool centeredSummary = Settings.StatusbarSummaryPosition == StatusbarSummaryMode.Center;
            float summaryRight = centeredClock ? statusRight : showResources ? resources : right;
            Status(new Rect(r.x + Pad, r.y, Mathf.Max(0f, statusRight - r.x - Pad), r.height),
                centeredSummary, summaryRight);

            // The line is drawn over the map, and the map takes whatever the buttons did not.
            // Without this a press here starts a drag-selection on the ground behind it. Last, so
            // the buttons have already had their refusal.
            if (interactive) Absorb(r);

        }

        // Consume only the initial press. The drag and release belong to its original target.
        static void Absorb(Rect r)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown) return;
            if (!Mouse.IsOver(r)) return;
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
                Thing(_doors.Core, ModDefOf.Ship_ComputerCore,
                    default, CoreTip.OpenMenu, live);
            if (_doors.HasJukebox)
                Thing(_doors.Jukebox, ModDefOf.SlopJukebox,
                    new TipSignal(_doors.JukeboxTip, JukeboxTipId), Jukebox.OpenMenu, live);
            return _doors.Right;
        }

        static void PrepareDoors(Rect r)
        {
            var map = Find.CurrentMap;
            bool showJukebox = Settings.StatusbarJukebox;
            bool showCore = Settings.StatusbarGM;
            if (_doorsReady && _doorsFrame == Time.frameCount && _doorsRect == r &&
                _doorsMap == map && _doorsShowJukebox == showJukebox &&
                _doorsShowCore == showCore) return;

            float x = r.xMax - Pad;
            _doors = new DoorLayout();
            _doors.HasConfig = TrySlot(r, ref x, UiTheme.GapS, out _doors.Config);
            // The settings cog is this interface's door. What is left of the line is the
            // colony's.
            float gap = UiTheme.GapM;

            if (showCore && CoreTip.On(map) && TrySlot(r, ref x, gap, out _doors.Core))
            {
                _doors.HasCore = true;
                gap = UiTheme.GapS;
            }

            if (showJukebox && Jukebox.On(map) && TrySlot(r, ref x, gap, out _doors.Jukebox))
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
        static bool TrySlot(Rect bar, ref float right, float gap, out Rect slot)
        {
            slot = default;
            float x = right - gap - IconW;
            if (x < bar.x || bar.height < IconW) return false;
            slot = new Rect(x, bar.y + (bar.height - IconW) / 2f, IconW, IconW);
            right = x;
            return true;
        }

        // Off-grey until the pointer is on it, the way the strip's own icons were.
        static void Door(Rect r, Texture2D icon, string tip, System.Action go, bool live)
        {
            TooltipHandler.TipRegion(r, tip);

            bool over = ColonistBarStrip.Hover(r);
            bool repaint = Event.current != null && Event.current.type == EventType.Repaint;

            var was = GUI.color;
            if (repaint)
            {
                if (over) Slab.Fill(r, UiTheme.Hover);
                GUI.color = over ? Color.white : UiTheme.Off;
                GUI.DrawTexture(r, icon);
                GUI.color = was;
            }

            Press(over, go, live);
        }

        // ThingIcon supplies its own tint. This method adds hover, input, and optional tips.
        static void Thing(Rect r, ThingDef def, TipSignal tip, System.Action go, bool live)
        {
            if (def == null) return;

            if (!string.IsNullOrEmpty(tip.text)) TooltipHandler.TipRegion(r, tip);

            bool over = ColonistBarStrip.Hover(r);
            bool repaint = Event.current != null && Event.current.type == EventType.Repaint;

            // Both of these read the ambient color and only one of them puts it back, so the pair
            // is bracketed. The highlight would wear whatever the last thing on the line left
            // behind, and ThingIcon hands back the def's own tint.
            var was = GUI.color;
            if (repaint)
            {
                GUI.color = Color.white;
                if (over) Slab.Fill(r, UiTheme.Hover);
                Widgets.ThingIcon(r, def);
                GUI.color = was;
            }

            Press(over, go, live);
        }

        // Use direct rect hit-testing because this map component may draw over an absorbing window.
        static void Press(bool over, System.Action go, bool live)
        {
            if (!over || !live) return;

            var e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0) return;

            e.Use();
            go();
        }

        // Show the pane's session, or the selected inspect-pane agent when no pane is open.
        static void Status(Rect r, bool centered, float summaryRight)
        {
            if (r.width <= 40f) return;

            // Content views replace the session status while visible.
            var view = TerminalWindow.Showing;
            if (view != null)
            {
                GUI.color = UiTheme.Lead;
                UiText.RowLabel(r, view.Title,
                    centered ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft);
                GUI.color = Color.white;
                return;
            }

            string session = TerminalWindow.CurrentName ?? InspectPaneAgent.SelectedSession();
            var hub = SessionHub.Instance;

            if (session == null)
            {
                GUI.color = UiTheme.Dim;
                UiText.RowLabel(r, hub.Online
                    ? $"{hub.Sessions.Count} agent{(hub.Sessions.Count == 1 ? "" : "s")}"
                    : $"daemon {hub.Status}",
                    centered ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft);
                GUI.color = Color.white;
                return;
            }

            var info = hub.Get(session);
            var state = info?.State ?? AgentState.Down;
            string tail = Tail(session, state);

            if (centered)
            {
                CenteredSummary(r, session, state, tail, summaryRight);
                return;
            }

            // Scale the status marker inset with the row height.
            float inset = Mathf.Round(r.height * 0.27f);
            var chip = new Rect(r.x, r.y + inset, UiTheme.StatusMarker,
                r.height - inset * 2f);
            Slab.Fill(chip, UiTheme.AgentStateColor(state));

            GUI.color = UiTheme.AgentStateColor(state);
            float w = Mathf.Min(UiTheme.Wide(session) + UiTheme.GapXS,
                Mathf.Max(0f, r.width - UiTheme.StatusMarker - UiTheme.GapS * 2f));
            var name = new Rect(chip.xMax + UiTheme.GapS, r.y, w, r.height);
            UiText.RowLabel(name, session);

            var rest = new Rect(name.xMax + UiTheme.GapS, r.y,
                Mathf.Max(0f, r.xMax - name.xMax - UiTheme.GapS), r.height);
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

        // Center only the dim summary. The colored marker and terminal name remain the
        // status anchor at the left edge of the bar.
        static void CenteredSummary(Rect r, string session, AgentState state, string tail,
                                    float summaryRight)
        {
            float markerW = UiTheme.StatusMarker;
            float nameW = UiTheme.Wide(session) + UiTheme.GapXS;
            Text.Font = GameFont.Tiny;
            float tailW = UiTheme.Wide(tail);
            Text.Font = GameFont.Small;

            float gaps = UiTheme.GapS * 2f;
            float nameDrawW = Mathf.Min(nameW,
                Mathf.Max(0f, r.width - markerW - gaps));
            float x = r.x;

            float inset = Mathf.Round(r.height * 0.27f);
            Slab.Fill(new Rect(x, r.y + inset, markerW, r.height - inset * 2f),
                UiTheme.AgentStateColor(state));

            GUI.color = UiTheme.AgentStateColor(state);
            UiText.RowLabel(new Rect(x + markerW + UiTheme.GapS, r.y, nameDrawW, r.height),
                session);

            float summaryLeft = x + markerW + UiTheme.GapS + nameDrawW + UiTheme.GapS;
            float available = Mathf.Max(0f, summaryRight - summaryLeft);
            float tailDrawW = Mathf.Min(tailW, available);
            if (tailDrawW > 0f)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = UiTheme.Dim;
                float center = Mathf.Clamp(UI.screenWidth / 2f,
                    summaryLeft + tailDrawW / 2f, summaryRight - tailDrawW / 2f);
                UiText.RowLabel(new Rect(center - tailDrawW / 2f, r.y, tailDrawW, r.height), tail,
                    TextAnchor.MiddleCenter);
                Text.Font = GameFont.Small;
            }
            GUI.color = Color.white;
        }

        // Prefer slopd's task title, falling back to the pane's OSC title.
        static string Tail(string session, AgentState state)
        {
            string path = FilesView.ViewerPath(session);
            if (!string.IsNullOrEmpty(path))
            {
                var hub = SessionHub.Instance;
                string project = hub.Get(session)?.Project;
                string root = string.IsNullOrEmpty(project) ? null : hub.Project(project)?.ExpandedDir;
                return PagerCommands.RelativeFilePath(root, path);
            }
            string title = SessionHub.Instance.Get(session)?.Title;
            return string.IsNullOrEmpty(title) ? state.ToString().ToLowerInvariant() : title;
        }
    }
}
