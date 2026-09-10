using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Shared scheme-driven opaque chrome; Slab owns fills/edges and fixed gaps keep controls
    // on the screen pixel grid.
    public abstract class UiTheme
    {
        const int TextCacheLimit = 512;
        static readonly Dictionary<TextCacheKey, float> WidthCache =
            new Dictionary<TextCacheKey, float>();
        static readonly Dictionary<TextCacheKey, string> TruncateCache =
            new Dictionary<TextCacheKey, string>();
        static int _atlasRevision;

        static UiTheme()
        {
            Font.textureRebuilt += _ => _atlasRevision++;
        }

        public static int AtlasRevision => _atlasRevision;

        readonly struct TextCacheKey : IEquatable<TextCacheKey>
        {
            readonly string _text;
            readonly float _width;
            readonly GameFont _gameFont;
            readonly GUIStyle _style;
            readonly Font _font;
            readonly int _fontSize;
            readonly FontStyle _fontStyle;
            readonly float _scale;
            readonly int _atlas;

            public TextCacheKey(string text, float width)
            {
                _text = text;
                _width = width;
                _gameFont = Text.Font;
                _style = Text.CurFontStyle;
                _font = _style == null ? null : _style.font;
                _fontSize = _style == null ? 0 : _style.fontSize;
                _fontStyle = _style == null ? FontStyle.Normal : _style.fontStyle;
                _scale = Prefs.UIScale;
                _atlas = _atlasRevision;
            }

            public bool Equals(TextCacheKey other) =>
                _width == other._width && _gameFont == other._gameFont
                && ReferenceEquals(_style, other._style)
                && ReferenceEquals(_font, other._font)
                && _fontSize == other._fontSize && _fontStyle == other._fontStyle
                && _scale == other._scale && _atlas == other._atlas
                && string.Equals(_text, other._text, StringComparison.Ordinal);

            public override bool Equals(object obj) =>
                obj is TextCacheKey && Equals((TextCacheKey)obj);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _text == null ? 0 : _text.GetHashCode();
                    hash = hash * 397 ^ _width.GetHashCode();
                    hash = hash * 397 ^ (int)_gameFont;
                    hash = hash * 397 ^ (_style == null ? 0 : _style.GetHashCode());
                    hash = hash * 397 ^ (_font == null ? 0 : _font.GetHashCode());
                    hash = hash * 397 ^ _fontSize;
                    hash = hash * 397 ^ (int)_fontStyle;
                    hash = hash * 397 ^ _scale.GetHashCode();
                    return hash * 397 ^ _atlas;
                }
            }
        }

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

        public static float LineHOf(GameFont font) => UiMetrics.Current.LineH(font);

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
                string value = text ?? "";
                var key = new TextCacheKey(value, 0f);
                if (WidthCache.TryGetValue(key, out float cached)) return cached;
                if (WidthCache.Count >= TextCacheLimit) WidthCache.Clear();
                return WidthCache[key] = Verse.Text.CalcSize(value).x;
            }
        }

        // RimWorld's Truncate repeatedly measures the current font while it removes
        // characters. Keep the result tied to every input that can change that measurement,
        // including dynamic-font atlas rebuilds and UI scale.
        public static string TruncateText(string text, float width)
        {
            using (WidgetState.Save())
            {
                Verse.Text.WordWrap = false;
                string value = text ?? "";
                float max = Mathf.Max(1f, width);
                var key = new TextCacheKey(value, max);
                if (TruncateCache.TryGetValue(key, out string cached)) return cached;
                if (TruncateCache.Count >= TextCacheLimit) TruncateCache.Clear();
                return TruncateCache[key] = value.Truncate(max);
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

        public static float BtnH => UiMetrics.Current.ButtonH(LineH);
        public static float ButtonPadX => UiMetrics.Compact ? 10f : 12f;
        public static float ButtonMinW => UiMetrics.Compact ? 72f : 76f;
        public static float FieldPadX => UiMetrics.Compact ? 5f : 6f;
        public static float FieldPadY => UiMetrics.Compact ? 1f : 2f;
        public const float IconInset = 2f;
        public const float IconW = 18f;
        public const float PickerCell = 38f;
        public const float PickerIcon = 30f;
        public const float ListInset = 4f;
        public const float DisclosureW = 11f;
        public const float ScrollbarW = 18f;
        public const float ScrollTrackW = 10f;
        public const float ScrollThumbInset = 2f;
        public const float MenuPadX = 12f;
        public const float MenuPadY = 0f;
        public const float StatusMarker = 8f;

        // Single-line controls share one compact hit target. Menus, fields and small row
        // buttons used to differ by a pixel, which was enough to make a form and the menu
        // opened from it feel like two widget kits.
        public static float CompactH => Mathf.Max(LineH + GapXS, UiMetrics.CompactMinH);
        public static float RowBtnH => CompactH;

        // A dropdown's rows carry one line each and are read as a block, so they sit as close
        // as the line will let them - a gap step tighter than the palette's, which is a list
        // scrolled and stepped through with the keyboard and wants the hit target.
        public static float MenuRowH => CompactH;
        public static float PaletteRowH => Mathf.Max(LineH + GapS, UiMetrics.PaletteMinH);

        public static float GapXS => UiMetrics.GapXS;   // a label and the box it names
        public static float GapS => UiMetrics.GapS;     // one control and the next
        public static float GapM => UiMetrics.GapM;     // one group of controls and the next
        public static float GapL => UiMetrics.GapL;     // one section and the next
    }
}
