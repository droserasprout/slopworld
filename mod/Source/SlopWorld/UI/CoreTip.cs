using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The persona core, hovered, says one of the loading screen's own tips - the same
    // list, because those lines are the machine talking at you while it thinks and
    // the core is the machine. It is also the one thing on this map worth pointing at
    // that can be neither selected nor clicked.
    //
    // Registering it from the map layer is what makes an open terminal hide it:
    // Mouse.IsOver is false whenever a window sits under the cursor. The line is
    // rolled when the cursor arrives and held until it leaves, because TipRegion
    // writes the text into the live tip on every frame it is called.
    public class CoreTip : MapComponent
    {
        // TooltipHandler keys its live tips on this, so a colliding id would be two
        // hovers sharing one box.
        const int TipId = 0x51_0F_C0DE;

        // What this hover is showing, or null while the cursor is elsewhere.
        string _tip;

        public CoreTip(Map map) : base(map) { }

        public override void MapComponentOnGUI()
        {
            if (Cutscene.Playing) return; // a scene plays bare

            // ThingAt answers for every cell the core stands on and takes an out-of-bounds
            // cell without complaint, which the mouse regularly is.
            var core = map.thingGrid.ThingAt(UI.MouseCell(), SlopDefOf.Ship_ComputerCore);
            if (core == null)
            {
                _tip = null;
                return;
            }

            if (_tip == null) _tip = Patch_LoadingTips.RandomTip;

            // The tooltip positions itself from the mouse either way, so this is only the hit
            // test - and the cell under the mouse has already answered that.
            var at = Event.current.mousePosition;
            TooltipHandler.TipRegion(new Rect(at.x - 1f, at.y - 1f, 2f, 2f),
                new TipSignal(_tip, TipId));
        }
    }
}
