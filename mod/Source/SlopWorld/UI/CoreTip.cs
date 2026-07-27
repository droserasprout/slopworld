using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// The persona core, hovered, says something - one of the loading screen's own
    /// tips. The list is <see cref="Patch_LoadingTips"/>'s and deliberately not a
    /// second one: those lines are the machine talking at you while it thinks, and
    /// the core is the machine. It is also the only thing on this map worth
    /// pointing at that the player can neither select nor click, so a bubble is
    /// the whole of what it can be given.
    ///
    /// A tooltip rather than anything drawn here, so it reads as every other hover
    /// in the game and places itself wherever the cursor has room. Registering it
    /// from the map layer is what makes an open terminal hide it: Mouse.IsOver is
    /// false whenever a window sits under the cursor and the map is what is being
    /// drawn, so the tip can never surface over a pane that fills the screen.
    ///
    /// The line is rolled when the cursor arrives and held until it leaves:
    /// TooltipHandler.TipRegion writes the text into the live tip on every frame,
    /// so rolling per frame would be a box of static rather than a sentence.
    ///
    /// MapComponents are constructed for every subclass, so this needs no def.
    /// </summary>
    public class CoreTip : MapComponent
    {
        /// Ours alone. TooltipHandler keys its live tips on this, so a colliding
        /// id would be two hovers sharing one box.
        const int TipId = 0x51_0F_C0DE;

        /// What this hover is showing, or null while the cursor is elsewhere.
        string _tip;

        public CoreTip(Map map) : base(map) { }

        public override void MapComponentOnGUI()
        {
            if (IntroDirector.UiHidden) return; // the opening scene plays bare

            // ThingAt answers for every cell the core stands on and takes an
            // out-of-bounds cell without complaint, which the mouse regularly is.
            var core = map.thingGrid.ThingAt(UI.MouseCell(), SlopDefOf.Ship_ComputerCore);
            if (core == null)
            {
                _tip = null;
                return;
            }

            if (_tip == null) _tip = Patch_LoadingTips.RandomTip;

            // A rect on the cursor. The tooltip positions itself from the mouse
            // either way, so this is only the hit test - and the cell under the
            // mouse has already answered that.
            var at = Event.current.mousePosition;
            TooltipHandler.TipRegion(new Rect(at.x - 1f, at.y - 1f, 2f, 2f),
                new TipSignal(_tip, TipId));
        }
    }
}
