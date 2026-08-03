using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The persona core: LMB opens a context menu. "Hint" shows a tip bubble that
    // fades after three seconds; "Next planet" burns the map and lands on a new one.
    public class CoreTip : MapComponent
    {
        // How long the sticky hint stays up before dismissing itself.
        const float HintTimeout = 3f;

        // A sticky hint pinned to the core by the context menu.
        bool _sticky;
        string _stickyTip;
        IntVec3 _stickyCell;

        // Where on screen the bubble was pinned, and when.
        Vector2 _stickyAt;
        float _stickyAtTime;

        // The mouse position when the context menu was opened, used by HintAction
        // to place the bubble where the cursor was.
        Vector2 _clickPos;

        // The core's cell when the context menu was opened, so HintAction can record
        // it without the mouse having moved to the menu item.
        IntVec3 _menuCell;

        public CoreTip(Map map) : base(map) { }

        public override void MapComponentOnGUI()
        {
            if (Cutscene.Playing) return; // a scene plays bare

            var cell = UI.MouseCell();
            var core = map.thingGrid.ThingAt(cell, SlopDefOf.Ship_ComputerCore);
            bool over = core != null;

            // LMB on the core opens the context menu. Only if no float menu is already up.
            if (over && Event.current.type == EventType.MouseDown && Event.current.button == 0
                && Find.WindowStack.FloatMenu == null)
            {
                _clickPos = Event.current.mousePosition;
                _menuCell = cell;
                Event.current.Use();
                var options = new List<FloatMenuOption>
                {
                    new FloatMenuOption("Hint", HintAction),
                    new FloatMenuOption("Next planet", NextPlanet.Begin),
                };
                Find.WindowStack.Add(new FloatMenu(options));
            }

            // Sticky hint: draw the tip bubble near the click position.
            if (_sticky && _stickyTip != null)
            {
                // Timed out.
                if (Time.realtimeSinceStartup - _stickyAtTime >= HintTimeout)
                {
                    _sticky = false;
                    _stickyTip = null;
                    return;
                }

                // The core's own cell, not the cell under the mouse: the hint must
                // survive the cursor leaving it.
                var still = map.thingGrid.ThingAt(_stickyCell, SlopDefOf.Ship_ComputerCore);
                if (still == null)
                {
                    _sticky = false;
                    _stickyTip = null;
                    return;
                }

                Text.Font = GameFont.Small;
                Text.WordWrap = true;
                float w = 320f;
                float h = Text.CalcHeight(_stickyTip, w - 16f) + 20f;
                float margin = 8f;

                // Place above the click position; if it hits the top edge, place below.
                float y = _stickyAt.y - h - 12f;
                if (y < margin) y = _stickyAt.y + 12f;
                // Clamp bottom edge too.
                if (y + h > UI.screenHeight - margin) y = UI.screenHeight - margin - h;
                if (y < margin) y = margin; // last resort: top margin

                var tipRect = new Rect(_stickyAt.x - w / 2f, y, w, h);
                if (tipRect.x < margin) tipRect.x = margin;
                if (tipRect.xMax > UI.screenWidth - margin) tipRect.x = UI.screenWidth - margin - w;

                Widgets.DrawWindowBackground(tipRect);
                var inner = tipRect.ContractedBy(8f);
                Widgets.Label(inner, _stickyTip);
                Text.WordWrap = false;
            }
        }

        // Toggle the sticky hint on or off. Called from the context menu.
        void HintAction()
        {
            if (_sticky)
            {
                _sticky = false;
                _stickyTip = null;
                return;
            }

            _sticky = true;
            _stickyTip = Patch_LoadingTips.RandomTip;
            _stickyAt = _clickPos;
            _stickyAtTime = Time.realtimeSinceStartup;
            _stickyCell = _menuCell;
        }
    }
}