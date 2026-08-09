using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public static class TopBar
    {
        // A line of the small font with room round it, not a figure: everything on this bar is
        // text or an 18-pixel glyph beside text, and 26 was a line of the shipped face. A taller
        // font on a bar that stayed 26 is a clock with its top and bottom rows cut off, which is
        // what a middle anchor does when the line does not fit - it crops both ends at once.
        // Floored at the old height so the map is not handed back pixels on the shipped font.
        public static float H => Mathf.Max(SlopWidgets.LineH + 6f, 26f);

        const float Pad = 8f;

        // Room for the clock, measured: "88:88" is as wide as the widest time there is.
        static float ClockW => Mathf.Max(SlopWidgets.Wide("88:88") + SlopWidgets.GapM, 76f);

        // Every door on the line at the size the drawn glyphs were on the strip they came
        // off. The two things on the map had four pixels more for a while, on the grounds
        // that a building shrunk to a tab-bar glyph is a smudge - but a `ThingIcon` fills
        // its rect where a glyph keeps a margin inside one, so the same figure already
        // draws them bigger than their neighbours and a larger one made them loom.
        const float IconW = 18f;

        // A `TipSignal` with no id of its own is keyed on its text, and the jukebox's names
        // what is playing - so the bubble would restart its fade every time the station moved
        // on. Both things carry an id instead, which only has to be theirs alone.
        const int CoreTipId = 0x51_0C_01;
        const int JukeboxTipId = 0x51_0C_02;

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
            Widgets.DrawBoxSolid(r, SlopWidgets.Panel);
            Widgets.DrawBoxSolid(new Rect(r.x, r.yMax - 1f, r.width, 1f), SlopWidgets.Edge);

            var was = GUI.color;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;

            var clock = new Rect(r.center.x - ClockW / 2f, r.y, ClockW, r.height);
            UsageReadout.DrawClock(clock, TextAnchor.MiddleCenter);

            // The doors first: they own the end of the line, and the quota takes what is left
            // of it. Laid out that way round because the quota is already right-aligned within
            // whatever room it gets, so a window coming or going never moves a button.
            //
            // The options menu used to be excepted here, being the one window laid out below
            // this line rather than over it - and the exception was worth nothing, a window
            // under an absorbing one never being called for a MouseDown. It is a content view
            // now, so the line is live whenever the window it is drawn from is.
            float right = Doors(r, interactive);

            // Nothing where there is no room: the doors take well over a hundred pixels off
            // this end with all four up, and a strip handed a negative width right-aligns its
            // first chip off the left of the clock rather than declining to draw.
            float quota = right - clock.xMax - Pad;
            if (quota > 0f)
                UsageReadout.DrawStrip(new Rect(clock.xMax + Pad, r.y, quota, r.height));

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

        // The end of the line, right to left: the menu, the settings cog, and the two things
        // standing on the map that have a menu of their own. Hands back the x the quota strip
        // may run up to.
        //
        // The cog and the hamburger came off the sidebar's tab strip. They are not about a
        // view - one opens the options dialog and the other opens every window this mod has -
        // so a selector two icons wide was the wrong place to keep them, and this end of the
        // bar is where the rest of the doors already are.
        //
        // The core and the jukebox are drawn only where they exist, an icon onto a thing that
        // is not on the map being no door at all. They are the game's own icons rather than
        // ones drawn in code, which is also what makes them read as the resources' neighbours
        // instead of as two more grey glyphs beside the cog.
        static float Doors(Rect r, bool live)
        {
            float x = r.xMax - Pad;
            var map = Find.CurrentMap;

            x -= IconW;
            Door(Slot(r, x, IconW), Icons.Menu, "Menu", Menu, live);

            x -= SlopWidgets.GapS + IconW;
            Door(Slot(r, x, IconW), Icons.Config, "Config", SlopOptions.Toggle, live);

            // A group of two and a group of two, so the gap between them is the wider one -
            // the cog and the menu are this interface's, and what is left of the line is the
            // colony's.
            float gap = SlopWidgets.GapM;

            if (CoreTip.On(map))
            {
                x -= gap + IconW;
                Thing(Slot(r, x, IconW), SlopDefOf.Ship_ComputerCore,
                    new TipSignal("Persona core", CoreTipId), CoreTip.OpenMenu, live);
                gap = SlopWidgets.GapS;
            }

            if (Jukebox.On(map))
            {
                x -= gap + IconW;
                Thing(Slot(r, x, IconW), SlopDefOf.SlopJukebox,
                    new TipSignal(Jukebox.IconTip(), JukeboxTipId), Jukebox.OpenMenu, live);
            }

            return x - SlopWidgets.GapM;
        }

        // Centred in the bar's height rather than filling it: 26 pixels of hit box for an
        // 18-pixel glyph is a press that lands on the hairline under the line.
        static Rect Slot(Rect r, float x, float w) =>
            new Rect(x, r.y + (r.height - w) / 2f, w, w);

        // Off-grey until the pointer is on it, the way the strip's own icons were.
        static void Door(Rect r, Texture2D icon, string tip, System.Action go, bool live)
        {
            TooltipHandler.TipRegion(r, tip);

            bool over = ColonistBarStrip.MouseOver(r);

            var was = GUI.color;
            GUI.color = over ? Color.white : SlopWidgets.Off;
            GUI.DrawTexture(r, icon);
            GUI.color = was;

            Press(over, go, live);
        }

        // ThingIcon carries the def's own colour and gives nothing back on a hover, so the
        // highlight and the press are drawn and taken here.
        static void Thing(Rect r, ThingDef def, TipSignal tip, System.Action go, bool live)
        {
            if (def == null) return;

            TooltipHandler.TipRegion(r, tip);
            bool over = ColonistBarStrip.MouseOver(r);

            // Both of these read the ambient colour and only one of them puts it back, so the
            // pair is bracketed: the highlight would wear whatever the last thing on the line
            // left behind, and ThingIcon hands back the def's own tint.
            var was = GUI.color;
            GUI.color = Color.white;
            if (over) Widgets.DrawHighlight(r);
            Widgets.ThingIcon(r, def);
            GUI.color = was;

            Press(over, go, live);
        }

        // Neither the hover nor the press goes through `Widgets.ButtonImage`, and for the
        // reason ColonistBarStrip.MouseOver states: every vanilla road to a click passes
        // `Mouse.IsOver`, which answers false whenever the window being drawn is not getting
        // input - and with the options dialog up over a pane, that is this line. Drawn from a
        // MapComponent it is not a window at all. So the rect is asked directly and the press
        // is taken here.
        static void Press(bool over, System.Action go, bool live)
        {
            if (!over || !live) return;

            var e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0) return;

            e.Use();
            go();
        }

        // Everything the bottom button row used to hold. Same list the sidebar's hamburger
        // opened, moved with it.
        static void Menu()
        {
            TerminalWindow.OpenOverPane(new FloatMenu(new List<FloatMenuOption>
            {
                new FloatMenuOption("Quit to OS", Root.Shutdown),
            }));
        }

        // The current agent is whichever pane is open, or with none the agent the map is
        // looking at. Its own title is the only thing here the agent itself wrote - Claude
        // Code keeps what it is doing in the terminal title - so it is said when there is one
        // and the state stands in when there is not.
        static void Status(Rect r, TerminalWindow pane)
        {
            if (r.width <= 40f) return;

            // A view in the body is what this line is about while it is up: the options
            // menu, or one of the three lists. No agent is being looked at, so naming one
            // here would be naming the thing behind what is on screen.
            var view = TerminalWindow.Showing;
            if (view != null)
            {
                GUI.color = SlopWidgets.Lead;
                SlopWidgets.RowLabel(r, view.Title);
                GUI.color = Color.white;
                return;
            }

            string session = TerminalWindow.CurrentName ?? InspectPaneAgent.Selected();
            var hub = SessionHub.Instance;

            if (session == null)
            {
                GUI.color = SlopWidgets.Dim;
                SlopWidgets.RowLabel(r, hub.Online
                    ? $"{hub.Sessions.Count} agent{(hub.Sessions.Count == 1 ? "" : "s")}"
                    : $"daemon {hub.Status}");
                GUI.color = Color.white;
                return;
            }

            var info = hub.Get(session);
            var state = info?.State ?? AgentState.Down;

            // A quarter of the line clear at each end rather than seven pixels: the bar grows
            // with the font and a fixed inset would leave the chip a sliver in the middle of it.
            float inset = Mathf.Round(r.height * 0.27f);
            var chip = new Rect(r.x, r.y + inset, 8f, r.height - inset * 2f);
            Widgets.DrawBoxSolid(chip, TerminalWindow.StateColor(state));

            GUI.color = TerminalWindow.StateColor(state);
            float w = Mathf.Min(SlopWidgets.Wide(session) + 4f, r.width - 16f);
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
            GUI.color = SlopWidgets.Dim;
            SlopWidgets.RowLabel(rest, tail);
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
