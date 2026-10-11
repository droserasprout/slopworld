using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // ActiveTip owns lifetime and text getters; this owner shares one frozen layout between
    // sizing and drawing. Mouse position and GUI translation must never invalidate that layout.
    internal sealed class TooltipLayout
    {
        static readonly ConditionalWeakTable<ActiveTip, TooltipLayout> Layouts =
            new ConditionalWeakTable<ActiveTip, TooltipLayout>();
        static readonly Func<ActiveTip, string> ReadText =
            AccessTools.MethodDelegate<Func<ActiveTip, string>>(AccessTools.PropertyGetter(typeof(ActiveTip), "FinalText"));

        string _source;
        int _fontRevision, _atlasRevision;
        float _scale;
        Font _font;
        int _fontSize;
        FontStyle _fontStyle;
        bool _richText;
        public string Text { get; private set; }
        public GUIStyle Style { get; private set; }
        public Vector2 Size { get; private set; }

        internal static TooltipLayout Get(ActiveTip tip) => Get(tip, ReadText(tip));

        internal static TooltipLayout Get(ActiveTip tip, string text)
        {
            var layout = Layouts.GetValue(tip, _ => new TooltipLayout());
            var native = Verse.Text.fontStyles[(int)GameFont.Small];
            if (layout._source == text && layout.Style != null &&
                layout._fontRevision == UiFont.Revision && layout._atlasRevision == UiTheme.AtlasRevision &&
                layout._scale == Prefs.UIScale && layout._font == native.font &&
                layout._fontSize == native.fontSize && layout._fontStyle == native.fontStyle &&
                layout._richText == native.richText)
                return layout;

            var style = new GUIStyle(native) { wordWrap = true, alignment = TextAnchor.UpperLeft };
            var content = new GUIContent(text);
            var rect = new Rect(0f, 0f, 260f, style.CalcHeight(content, 260f));
            string frozen = TooltipLineBreaks.Freeze(text,
                offset => style.GetCursorPixelPosition(rect, content, offset).y, style.richText);
            // Explicit breaks are authoritative even when Unity's moving/scaled label would
            // choose a different break. CalcSize and GUI.Label use this same no-wrap style.
            style.wordWrap = false;
            layout.Text = frozen;
            layout.Style = style;
            layout.Size = style.CalcSize(new GUIContent(frozen));
            layout._source = text;
            layout._fontRevision = UiFont.Revision;
            layout._atlasRevision = UiTheme.AtlasRevision;
            layout._scale = Prefs.UIScale;
            layout._font = native.font;
            layout._fontSize = native.fontSize;
            layout._fontStyle = native.fontStyle;
            layout._richText = native.richText;
            return layout;
        }
    }
}
