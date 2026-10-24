using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The same renderer serves every editor's daemon-resolved settings preview.
    public static class SandboxPreviewPanel
    {
        public static void Draw(Rect outer, ref SmoothScroll scroll, Wire.SettingsPreview data)
        {
            Slab.Box(outer, UiTheme.Well, UiTheme.Edge);
            var pad = outer.ContractedBy(UiTheme.GapS);
            var view = new Rect(
                0f,
                0f,
                pad.width - UiTheme.ScrollbarW,
                Mathf.Max(Height(data, pad.width - UiTheme.ScrollbarW), pad.height)
            );
            using (scroll.Scope(pad, view))
            {
                float y = 0f;
                y = TextBlock(view.width, y, data.Title, UiTheme.Lead);
                y += UiTheme.GapXS;
                if (!string.IsNullOrEmpty(data.Subtitle))
                {
                    y = TextBlock(view.width, y, data.Subtitle, UiTheme.Dim);
                    y += UiTheme.GapXS;
                }
                if (data.Notes.Count > 0)
                {
                    y = TextBlock(
                        view.width,
                        y,
                        string.Join("\n", data.Notes.ToArray()),
                        UiTheme.Faint
                    );
                    y += UiTheme.GapS;
                }

                foreach (var field in data.Fields)
                    y = Field(view.width, y, field);
            }
        }

        static float Height(Wire.SettingsPreview data, float width)
        {
            float y = 0f;
            y += UiText.PlainStatusLabelHeight(data.Title, width) + UiTheme.GapXS;
            if (!string.IsNullOrEmpty(data.Subtitle))
                y += UiText.PlainStatusLabelHeight(data.Subtitle, width) + UiTheme.GapXS;
            if (data.Notes.Count > 0)
                y +=
                    UiText.PlainStatusLabelHeight(string.Join("\n", data.Notes.ToArray()), width)
                    + UiTheme.GapS;
            foreach (var field in data.Fields)
            {
                string text =
                    field.Values.Count == 0
                        ? "(nothing)"
                        : string.Join("\n", field.Values.ToArray());
                y +=
                    UiTheme.RowH
                    + UiTheme.GapXS
                    + UiText.PlainStatusLabelHeight(text, width)
                    + UiTheme.GapM;
            }
            return y + UiTheme.GapM;
        }

        static float Field(float width, float y, Wire.PreviewField field)
        {
            UiLayout.SectionHeading(new Rect(0f, y, width, UiTheme.RowH), field.Label);
            y += UiTheme.RowH + UiTheme.GapXS;
            string text =
                field.Values.Count == 0 ? "(nothing)" : string.Join("\n", field.Values.ToArray());
            float h = UiText.PlainStatusLabelHeight(text, width);
            UiText.PlainStatusLabel(
                new Rect(0f, y, width, h),
                text,
                field.Values.Count == 0 ? UiTheme.Faint : UiTheme.Name
            );
            return y + h + UiTheme.GapM;
        }

        static float TextBlock(float width, float y, string text, Color color)
        {
            float h = UiText.PlainStatusLabelHeight(text, width);
            UiText.PlainStatusLabel(new Rect(0f, y, width, h), text, color);
            return y + h;
        }
    }
}
