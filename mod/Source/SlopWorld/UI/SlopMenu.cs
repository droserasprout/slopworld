using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SlopWorld
{
    // The dropdown, in the mod's own chrome. `FloatMenu` was the last vanilla surface left:
    // every picker here - fonts, themes, projects, stations, network modes, the sidebar's
    // context menus - opened one, and it arrived textured, rounded at the corners and lit in
    // vanilla's colours in the middle of a flat dark form.
    //
    // A **drop-in**, taking the same `List<FloatMenuOption>` the call sites already build, so
    // switching one over is a changed type name and nothing else. That is also the limit of
    // it: this reads the four things those options actually carry - the label, the action,
    // `Disabled`, and the right-justified extra part that [SlopWidgets.MenuToggle] hangs a
    // tick on - and ignores the two dozen fields vanilla's own menus use for pawn orders,
    // which nothing in this mod builds.
    public class SlopMenu : Window
    {
        readonly List<FloatMenuOption> _options;
        readonly SmoothScroll _scroll = new SmoothScroll();

        // Vanilla's own ceiling on a menu's width, kept: a label longer than this is a path
        // or a URL, and past three hundred pixels a wider menu does not make it readable.
        const float MaxW = 300f;
        const float MinW = 160f;

        // The clear space either side of a label, and above and below the whole list.
        const float PadX = SlopWidgets.MenuPadX;
        const float PadY = SlopWidgets.MenuPadY;

        // How much of the screen a menu may take before it scrolls instead of growing. The
        // jukebox's station list is the one that reaches it.
        const float MaxScreen = 0.6f;

        static float RowH => SlopWidgets.MenuRowH;

        public SlopMenu(List<FloatMenuOption> options)
        {
            _options = options ?? new List<FloatMenuOption>();

            doWindowBackground = false;
            doCloseX = false;
            doCloseButton = false;
            closeOnClickedOutside = true;
            drawShadow = false;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
            layer = WindowLayer.Super;
        }

        protected override float Margin => 0f;

        public static void Open(List<FloatMenuOption> options) =>
            Find.WindowStack.Add(new SlopMenu(options));

        float ContentH => _options.Count * RowH + PadY * 2f;

        float WidestLabel()
        {
            var was = Text.Font;
            Text.Font = GameFont.Small;
            float w = 0f;
            foreach (var o in _options)
                w = Mathf.Max(w, SlopWidgets.Wide(o.Label) + o.extraPartWidth);
            Text.Font = was;
            return w;
        }

        public override Vector2 InitialSize =>
            new Vector2(Mathf.Clamp(WidestLabel() + PadX * 2f, MinW, MaxW),
                Mathf.Min(ContentH, UI.screenHeight * MaxScreen));

        // At the mouse, and shoved back onto the screen rather than off the bottom of it -
        // which is where a menu opened from a row near the foot of a tall list would go.
        protected override void SetInitialSizeAndPosition()
        {
            var size = InitialSize;
            var at = UI.MousePositionOnUIInverted;
            windowRect = new Rect(
                Mathf.Min(at.x, UI.screenWidth - size.x),
                Mathf.Min(at.y, UI.screenHeight - size.y),
                size.x, size.y);
        }

        public override void DoWindowContents(Rect rect)
        {
            Slab.Box(rect, SlopWidgets.PopoverBg, SlopWidgets.Edge);

            var inner = new Rect(rect.x, rect.y + PadY, rect.width, rect.height - PadY * 2f);
            bool scrolls = ContentH > rect.height;
            var view = new Rect(0f, 0f, inner.width - (scrolls ? SlopWidgets.ScrollbarW : 0f),
                _options.Count * RowH);

            Text.Font = GameFont.Small;
            _scroll.Begin(inner, view);

            // Stops at the row that was pressed. The press closes the menu and may open
            // another one, and drawing the rest of a list that is already gone is at best
            // wasted and at worst a second option answering the same click.
            float y = 0f;
            for (int i = 0; i < _options.Count; i++)
            {
                if (Row(new Rect(0f, y, view.width, RowH), _options[i])) break;
                y += RowH;
            }

            _scroll.End();
        }

        // True if this row took the press.
        bool Row(Rect r, FloatMenuOption o)
        {
            bool on = !o.Disabled;
            bool over = on && Mouse.IsOver(r);

            if (over) Slab.Fill(r, SlopWidgets.Hover);
            if (o.tooltip.HasValue) TooltipHandler.TipRegion(r, o.tooltip.Value);

            // The extra part is the tick [SlopWidgets.MenuToggle] draws, and it is always
            // right-justified here - the one caller that asks for it asks for that too.
            float extra = o.extraPartWidth;
            if (o.extraPartOnGUI != null && extra > 0f)
                o.extraPartOnGUI(new Rect(r.xMax - extra, r.y, extra, r.height));

            var label = new Rect(r.x + PadX, r.y, r.width - PadX * 2f - extra, r.height);
            var wasAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = !on ? SlopWidgets.Off : over ? SlopWidgets.Lead : SlopWidgets.Name;
            SlopWidgets.RowLabel(label, o.Label);
            GUI.color = Color.white;
            Text.Anchor = wasAnchor;

            if (!on || !Widgets.ButtonInvisible(r)) return false;

            SoundDefOf.Click.PlayOneShotOnCamera();
            // Closed before the action runs: an action that opens a second menu would
            // otherwise have this one shut on top of it.
            Close(false);
            if (o.action != null) o.action();
            return true;
        }
    }
}
