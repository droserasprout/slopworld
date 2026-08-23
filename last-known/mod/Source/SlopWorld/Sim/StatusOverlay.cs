using Verse;

namespace SlopWorld
{
    // Retained as an empty component for saves and older map-component ordering. Agent state
    // now belongs to the pawn name color, so a second word above the pawn is noise.
    public class StatusOverlay : MapComponent
    {
        public StatusOverlay(Map map) : base(map) { }

        public override void MapComponentOnGUI() { }
    }
}
