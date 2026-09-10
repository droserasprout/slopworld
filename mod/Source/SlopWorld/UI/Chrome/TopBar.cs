using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Draw the bar from the map layer so it stays behind windows and remains interactive over
    // them. The readout itself is only the quota/clock renderer; keeping this component here
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

        // Door icons use the shared glyph size; ThingIcon already fills its slot more densely.
        const float IconW = UiTheme.IconW;

        // Give the jukebox tip a stable id so changing song text does not restart its fade.
        const int JukeboxTipId = 0x51_0C_02;

        public static Rect Rect => WorkspaceLayout.Current.TopBar;

        // From the map component above, which sits behind every window.
        public static void DrawOnMap()
        {
            if (Find.WindowStack?.WindowOfType<TerminalWindow>() != null) return;
            Draw(true);
        }

        public static void Draw(bool interactive)
        {
            using (WidgetState.Save()) DrawCore(interactive);
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

            // Lay out fixed doors first; usage rows then consume the remaining width.
            float right = Doors(r, interactive);

            string clockPosition = Settings.StatusbarClockPosition;
            float statusRight = r.center.x - Pad;
            float resources = r.center.x + Pad;

            if (clockPosition == StatusbarClockMode.Center)
            {
                DateTime now = DateTime.Now;
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
                }
            }

            // Omit usage when the doors leave no room. A centered clock owns its own gap;
            // right mode keeps the original quota-strip layout.
            float quota = right - resources;
            if (quota > 0f && (Settings.StatusbarUsage
                || clockPosition == StatusbarClockMode.Right))
                UsageReadout.DrawStrip(new Rect(resources, r.y, quota, r.height),
                    Settings.StatusbarUsage, clockPosition == StatusbarClockMode.Right);

            Status(new Rect(r.x + Pad, r.y, Mathf.Max(0f, statusRight - r.x - Pad), r.height));

            // The line is drawn over the map, and the map takes whatever the buttons did not:
            // without this a press here starts a drag-selection on the ground behind it. Last,
            // so the buttons have already had their refusal.
            if (interactive) Absorb(r);

        }

        // Consume only the initial press; the drag and release belong to its original target.
        static void Absorb(Rect r)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown) return;
            if (!Mouse.IsOver(r)) return;
            e.Use();
        }

        // Place config and any map objects with menus right-to-left; return quota's limit.
        static float Doors(Rect r, bool live)
        {
            float x = r.xMax - Pad;
            var map = Find.CurrentMap;

            x -= UiTheme.GapS + IconW;
            Door(Slot(r, x, IconW), Icons.Config, "Settings", ModOptions.Toggle, live);

            // The settings cog is this interface's door; what is left of the line is the
            // colony's.
            float gap = UiTheme.GapM;

            if (Settings.StatusbarGM && CoreTip.On(map))
            {
                x -= gap + IconW;
                Thing(Slot(r, x, IconW), ModDefOf.Ship_ComputerCore,
                    default, CoreTip.OpenMenu, live);
                gap = UiTheme.GapS;
            }

            if (Settings.StatusbarJukebox && Jukebox.On(map))
            {
                x -= gap + IconW;
                Thing(Slot(r, x, IconW), ModDefOf.SlopJukebox,
                    new TipSignal(Jukebox.IconTip(), JukeboxTipId), Jukebox.OpenMenu, live);
            }

            return x - UiTheme.GapM;
        }

        // Centre the glyph-sized hit slot inside the taller bar.
        static Rect Slot(Rect r, float x, float w) =>
            new Rect(x, r.y + (r.height - w) / 2f, w, w);

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

        // ThingIcon supplies its own tint; this method adds hover, input, and optional tips.
        static void Thing(Rect r, ThingDef def, TipSignal tip, System.Action go, bool live)
        {
            if (def == null) return;

            if (!string.IsNullOrEmpty(tip.text)) TooltipHandler.TipRegion(r, tip);

            bool over = ColonistBarStrip.Hover(r);
            bool repaint = Event.current != null && Event.current.type == EventType.Repaint;

            // Both of these read the ambient color and only one of them puts it back, so the
            // pair is bracketed: the highlight would wear whatever the last thing on the line
            // left behind, and ThingIcon hands back the def's own tint.
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
        static void Status(Rect r)
        {
            if (r.width <= 40f) return;

            // Content views replace the session status while visible.
            var view = TerminalWindow.Showing;
            if (view != null)
            {
                GUI.color = UiTheme.Lead;
                UiText.RowLabel(r, view.Title);
                GUI.color = Color.white;
                return;
            }

            string session = TerminalWindow.CurrentName ?? InspectPaneAgent.Selected();
            var hub = SessionHub.Instance;

            if (session == null)
            {
                GUI.color = UiTheme.Dim;
                UiText.RowLabel(r, hub.Online
                    ? $"{hub.Sessions.Count} agent{(hub.Sessions.Count == 1 ? "" : "s")}"
                    : $"daemon {hub.Status}");
                GUI.color = Color.white;
                return;
            }

            var info = hub.Get(session);
            var state = info?.State ?? AgentState.Down;

            // Scale the status marker inset with the row height.
            float inset = Mathf.Round(r.height * 0.27f);
            var chip = new Rect(r.x, r.y + inset, UiTheme.StatusMarker,
                r.height - inset * 2f);
            Slab.Fill(chip, TerminalWindow.StateColor(state));

            GUI.color = TerminalWindow.StateColor(state);
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

            string tail = Tail(session, state);

            Text.Font = GameFont.Tiny;
            GUI.color = UiTheme.Dim;
            UiText.RowLabel(rest, tail);
            Text.Font = GameFont.Small;
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
                string root = string.IsNullOrEmpty(project) ? null : hub.Project(project)?.Dir;
                return PagerCommands.RelativeFilePath(root, path);
            }
            string title = SessionHub.Instance.Get(session)?.Title;
            return string.IsNullOrEmpty(title) ? state.ToString().ToLower() : title;
        }
    }
}
