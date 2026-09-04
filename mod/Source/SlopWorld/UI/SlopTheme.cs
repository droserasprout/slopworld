using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Shared scheme-driven opaque chrome; Slab owns fills/edges and fixed gaps keep controls
    // on the screen pixel grid.
    public abstract class SlopTheme
    {
        // ---- Surfaces and semantic colors. These are named for SlopWorld's jobs rather
        // than for a borrowed toolkit's widgets, and they are the only names anything else
        // in the mod knows: the values behind them belong to the scheme the player picked,
        // and are read through here so a scheme lands everywhere at once. See UIScheme.

        public static Color Accent => UIScheme.Current.Accent;
        public static Color Destructive => UIScheme.Current.Destructive;

        public static Color WindowBg => UIScheme.Current.WindowBg;
        public static Color ViewBg => UIScheme.Current.ViewBg;
        public static Color PopoverBg => UIScheme.Current.PopoverBg;

        public static Color Lead => UIScheme.Current.Lead;
        public static Color Name => UIScheme.Current.Name;
        public static Color Dim => UIScheme.Current.Dim;
        public static Color Faint => UIScheme.Current.Faint;
        public static Color Off => UIScheme.Current.Off;

        public static Color Bad => UIScheme.Current.Bad;
        public static Color Warn => UIScheme.Current.Warn;
        public static Color Global => UIScheme.Current.Global;

        // Panel is the one surface allowed to show a trace of the map beneath it. All
        // controls and popovers use the opaque surfaces above.
        public static Color Panel => UIScheme.Current.Panel;
        public static Color OfflineBg => UIScheme.Current.OfflineBg;

        // A wash under text that stands on the map, where there is no surface to put it on.
        public static Color Scrim => UIScheme.Current.Scrim;

        // Structural lines are a restrained wash of the scheme's own light. EdgeLit is
        // reserved for the resize grip and other places where the pointer is actively on
        // the structure.
        public static Color Edge => UIScheme.Current.Edge;
        public static Color EdgeLit => UIScheme.Current.EdgeLit;
        public static Color ScrollTrough => UIScheme.Current.ScrollTrough;
        public static Color ScrollThumb => UIScheme.Current.ScrollThumb;
        public static Color ScrollThumbHover => UIScheme.Current.ScrollThumbHover;
        public static Color ScrollThumbHeld => UIScheme.Current.ScrollThumbHeld;

        public static Color Well => UIScheme.Current.Well;

        // One green for "this is up" and "this is on"; Yes is the name the forms ask for it
        // by, and the status marker is the same color saying the same thing.
        public static Color Yes => UIScheme.Current.Yes;

        public static Color RowBg => UIScheme.Current.RowBg;
        public static Color RowOn => UIScheme.Current.RowOn;
        public static Color Sel => UIScheme.Current.Sel;
        public static Color Hover => UIScheme.Current.Hover;

        public static Color StateWorking => UIScheme.Current.StateWorking;
        public static Color StateWaiting => UIScheme.Current.StateWaiting;
        public static Color StateIdle => UIScheme.Current.StateIdle;
        public static Color StateDown => UIScheme.Current.StateDown;
        public static Color Info => UIScheme.Current.StateWorking;

        public static readonly Color Clear = new Color(0f, 0f, 0f, 0f);

        protected static Color Lighten(Color c, float by)
        {
            var to = by >= 0f ? Color.white : Color.black;
            float t = Mathf.Abs(by);
            return new Color(Mathf.Lerp(c.r, to.r, t), Mathf.Lerp(c.g, to.g, t),
                Mathf.Lerp(c.b, to.b, t), c.a);
        }

        // Opacity is used only for disabled or overlaid states; base surfaces stay opaque.
        public static Color Fade(Color c, float by) =>
            new Color(c.r, c.g, c.b, c.a * by);

        // Accent and destructive buttons use a lightness step; ordinary buttons use the
        // same translucent white faces over every shared surface.
        protected static Color Step(Color c, bool over, bool held) =>
            held ? Lighten(c, -0.15f) : over ? Lighten(c, 0.10f) : c;

        public static float LineH => LineHOf(GameFont.Small);

        public static float FieldH => CompactH;
        public static float RowH => LineH + GapXS + 2f;

        public static float HeaderH => LineHOf(GameFont.Medium) + GapS;

        public static float LineHOf(GameFont font) =>
            Mathf.Ceil(Verse.Text.LineHeightOf(Real(font)));

        // Verse silently promotes Tiny when the current language or display cannot support it.
        public static GameFont Real(GameFont font) =>
            font == GameFont.Tiny && !Verse.Text.TinyFontSupported ? GameFont.Small : font;

        public static float TinyH => LineHOf(GameFont.Tiny);
        public static float TinyRowH => TinyH + 2f;

        public static float Wide(string text)
        {
            using (WidgetState.Save())
            {
                Verse.Text.WordWrap = false;
                return Verse.Text.CalcSize(text ?? "").x;
            }
        }

        public enum Btn
        {
            Default,   // the ordinary press: Reload, Browse, Edit.
            Primary,   // what the window was opened to do. One per bar, or it means nothing.
            Danger,    // takes something away. Still asks first; this is so it is read first.
            Ghost,     // there, but not competing - a press beside a press that matters more.
        }

        protected static Color BtnEdge => Edge;

        protected static Color BtnFace => UIScheme.Current.BtnFace;
        protected static Color BtnHover => UIScheme.Current.BtnHover;
        protected static Color BtnDown => UIScheme.Current.BtnDown;

        // A ghost button has no face at rest; its rectangular hit area appears on hover.
        protected static Color GhostFace => Clear;

        protected static Color FocusRing => Accent;

        protected static Color KnobFace => UIScheme.Current.Knob;
        protected static Color CheckFace => UIScheme.Current.CheckFace;

        protected static Color PrimeFace => Accent;
        protected static Color DangerFace => Destructive;

        public const float BtnH = 30f;
        public const float ButtonPadX = 12f;
        public const float ButtonMinW = 76f;
        public const float FieldPadX = 6f;
        public const float FieldPadY = 2f;
        public const float IconInset = 2f;
        public const float IconW = 18f;
        public const float ScrollbarW = 18f;
        public const float ScrollTrackW = 10f;
        public const float ScrollThumbInset = 2f;
        public const float MenuPadX = 12f;
        public const float MenuPadY = 0f;
        public const float StatusMarker = 8f;

        // Single-line controls share one compact hit target. Menus, fields and small row
        // buttons used to differ by a pixel, which was enough to make a form and the menu
        // opened from it feel like two widget kits.
        public static float CompactH => Mathf.Max(LineH + GapXS, 22f);
        public static float RowBtnH => CompactH;

        // A dropdown's rows carry one line each and are read as a block, so they sit as close
        // as the line will let them - a gap step tighter than the palette's, which is a list
        // scrolled and stepped through with the keyboard and wants the hit target.
        public static float MenuRowH => CompactH;
        public static float PaletteRowH => Mathf.Max(LineH + GapS, 26f);

        public const float GapXS = 4f;   // a label and the box it names
        public const float GapS = 8f;    // one control and the next
        public const float GapM = 16f;   // one group of controls and the next
        public const float GapL = 24f;   // one section and the next
    }
}
