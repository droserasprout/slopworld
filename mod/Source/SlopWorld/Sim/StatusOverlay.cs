using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// Floats each agent's state over its colonist, so the map reads as a status
    /// board at a glance. MapComponents are instantiated for every subclass, so
    /// this needs no def either.
    /// </summary>
    public class StatusOverlay : MapComponent
    {
        public StatusOverlay(Map map) : base(map) { }

        public override void MapComponentOnGUI()
        {
            if (!Settings.Overlay) return;
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
                var state = info?.State ?? AgentState.Dead;

                var pos = GenMapUI.LabelDrawPosFor(pawn, -0.85f);
                var box = new Rect(pos.x - 40f, pos.y - 2f, 80f, 20f);

                Widgets.DrawBoxSolid(new Rect(box.center.x - 30f, box.y, 60f, 16f),
                    new Color(0f, 0f, 0f, 0.55f));

                GUI.color = TerminalWindow.StateColor(state);
                Widgets.Label(box, Glyph(state));
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
                default: return "dead";
            }
        }
    }
}
