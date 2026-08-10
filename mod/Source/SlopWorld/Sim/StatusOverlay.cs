using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Floats each agent's state over its colonist, so the map reads at a glance.
    public class StatusOverlay : MapComponent
    {
        public StatusOverlay(Map map) : base(map) { }

        public override void MapComponentOnGUI()
        {
            if (Eco.Bare) return; // no board under the words
            if (Find.CameraDriver.CurrentZoom > CameraZoomRange.Far) return;

            var colony = AgentColony.Current;
            if (colony == null) return;

            var hub = SessionHub.Instance;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;

            foreach (var kv in colony.All)
            {
                var pawn = kv.Value;
                if (pawn == null || !pawn.Spawned || pawn.Map != map) continue;

                var info = hub.Get(kv.Key);
                var state = info?.State ?? AgentState.Down;

                // The plate is measured off the word it is behind rather than written down:
                // at 60x16 under an 80x20 box it was already the narrower of the two, so a
                // larger font hung the word off both ends of its own backing.
                string glyph = Glyph(state);
                float w = SlopWidgets.Wide(glyph) + SlopWidgets.GapS;
                float h = SlopWidgets.TinyH;

                var pos = GenMapUI.LabelDrawPosFor(pawn, -0.85f);
                var box = new Rect(pos.x - w / 2f, pos.y - 2f, w, h);

                Widgets.DrawBoxSolid(box, new Color(0f, 0f, 0f, 0.55f));

                GUI.color = TerminalWindow.StateColor(state);
                Widgets.Label(box, glyph);
                GUI.color = Color.white;
            }

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        static string Glyph(AgentState s)
        {
            switch (s)
            {
                case AgentState.Working: return "working";
                case AgentState.Waiting: return "! INPUT";
                case AgentState.Idle: return "idle";
                default: return "down";
            }
        }
    }
}
