using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public static class TopBar
    {
        public const float H = 26f;

        const float Pad = 8f;
        const float ClockW = 76f;

        static readonly Color Bg = new Color(0.09f, 0.10f, 0.12f, 0.93f);
        static readonly Color Edge = new Color(0f, 0f, 0f, 0.55f);
        static readonly Color Dim = new Color(0.58f, 0.60f, 0.64f);
        static readonly Color IconIdle = new Color(0.62f, 0.64f, 0.66f);

        public static Rect Rect =>
            new Rect(SlopLayout.LeftInset, 0f, UI.screenWidth - SlopLayout.LeftInset, H);

        // From UsageReadout, which is a MapComponent and so sits behind every window.
        public static void DrawOnMap()
        {
            if (Find.WindowStack?.WindowOfType<TerminalWindow>() != null) return;
            Draw(null, true);
        }

        public static void Draw(TerminalWindow pane, bool interactive)
        {
            if (!SlopLayout.Shown || SlopLayout.Hidden) return;
            if (Event.current.type == EventType.Layout) return;

            var r = Rect;
            Widgets.DrawBoxSolid(r, Bg);
            Widgets.DrawBoxSolid(new Rect(r.x, r.yMax - 1f, r.width, 1f), Edge);

            var was = GUI.color;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;

            float right = r.xMax - Pad;

            var clock = new Rect(r.center.x - ClockW / 2f, r.y, ClockW, r.height);
            UsageReadout.DrawClock(clock, TextAnchor.MiddleCenter);

            UsageReadout.DrawStrip(
                new Rect(clock.xMax + Pad, r.y, right - clock.xMax - Pad, r.height));

            Status(new Rect(r.x + Pad, r.y, clock.x - r.x - Pad * 2f, r.height), pane);

            // The line is drawn over the map, and the map takes whatever the buttons did not:
            // without this a press here starts a drag-selection on the ground behind it. Last,
            // so the buttons have already had their refusal.
            if (interactive) Absorb(r);

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = was;
        }

        // The press and nothing else: a drag and its release belong to whoever the press went
        // to. Same reason as AgentSidebar.Absorb.
        static void Absorb(Rect r)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown) return;
            if (!Mouse.IsOver(r)) return;
            e.Use();
        }

        // No buttons any more: the close cross is Shift+Esc, and the gear that sat beside
        // it moved to a tab of the options dialog. The corner is all quota strip now.

        // The current agent is whichever pane is open, or with none the agent the map is
        // looking at. Its own title is the only thing here the agent itself wrote - Claude
        // Code keeps what it is doing in the terminal title - so it is said when there is one
        // and the state stands in when there is not.
        static void Status(Rect r, TerminalWindow pane)
        {
            if (r.width <= 40f) return;

            string session = TerminalWindow.CurrentName ?? InspectPaneAgent.Selected();
            var hub = SessionHub.Instance;

            if (session == null)
            {
                GUI.color = Dim;
                Widgets.Label(r, hub.Online
                    ? $"{hub.Sessions.Count} agent{(hub.Sessions.Count == 1 ? "" : "s")}"
                    : $"daemon {hub.Status}");
                GUI.color = Color.white;
                return;
            }

            var info = hub.Get(session);
            var state = info?.State ?? AgentState.Down;

            var chip = new Rect(r.x, r.y + 7f, 8f, r.height - 14f);
            Widgets.DrawBoxSolid(chip, TerminalWindow.StateColor(state));

            GUI.color = TerminalWindow.StateColor(state);
            float w = Mathf.Min(Text.CalcSize(session).x + 4f, r.width - 16f);
            var name = new Rect(chip.xMax + 6f, r.y, w, r.height);
            Widgets.Label(name, session);

            var rest = new Rect(name.xMax + 8f, r.y, r.xMax - name.xMax - 8f, r.height);
            if (rest.width <= 20f) { GUI.color = Color.white; return; }

            // The pane's shape rides along while one is open, that being the number worth
            // seeing when an app has drawn itself the wrong width.
            string tail = Tail(session, state);
            if (pane != null && !string.IsNullOrEmpty(pane.Shape))
                tail = pane.Shape + "   " + tail;

            Text.Font = GameFont.Tiny;
            GUI.color = Dim;
            Widgets.Label(rest, tail.Truncate(rest.width));
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        // Only a subscribed session has a screen here, which in practice means the one whose
        // pane is open; everything else falls back to the word for its state.
        static string Tail(string session, AgentState state)
        {
            string title = SessionHub.Instance.Screen(session)?.Title;
            return string.IsNullOrEmpty(title) ? state.ToString().ToLower() : title;
        }
    }
}
