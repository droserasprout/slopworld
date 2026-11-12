using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Each editor retains its reply layout; only visible rows enter native drawing.
    public sealed class SandboxPreviewPanel
    {
        readonly SandboxPreviewLayout _layout = new SandboxPreviewLayout();

        public void Draw(Rect outer, ref SmoothScroll scroll, Wire.SettingsPreview data)
        {
            Slab.Box(outer, UiTheme.Well, UiTheme.Edge);
            var pad = outer.ContractedBy(UiTheme.GapS);
            float width = Mathf.Max(1f, pad.width - UiTheme.ScrollbarW);
            _layout.Ensure(
                data,
                width,
                UiMetrics.Revision,
                // Browser fallback/glyph completion also advances this revision.
                TerminalFont.Rev,
                UiTheme.RowH,
                UiTheme.GapXS,
                UiTheme.GapS,
                UiTheme.GapM,
                (text, available) => UiText.PlainStatusLabelHeight(text, available)
            );
            var view = new Rect(0f, 0f, width, Mathf.Max(_layout.Height, pad.height));
            using (scroll.Scope(pad, view))
            {
                // Scope consumes wheel input, but this read-only panel has no
                // controls requiring text layout on input/layout passes.
                if (Event.current.type != EventType.Repaint)
                    return;
                float top = scroll.Position.y;
                for (int i = _layout.FirstVisible(top); i < _layout.Rows.Count; i++)
                {
                    var row = _layout.Rows[i];
                    if (row.Top >= top + pad.height)
                        break;
                    var rect = new Rect(0f, row.Top, width, row.Height);
                    if (row.Style == SandboxPreviewLayout.Kind.Heading)
                        UiLayout.SectionHeading(rect, row.Text);
                    else
                        UiText.PlainStatusLabel(rect, row.Text, ColorFor(row.Style));
                }
            }
        }

        static Color ColorFor(SandboxPreviewLayout.Kind kind)
        {
            switch (kind)
            {
                case SandboxPreviewLayout.Kind.Title:
                    return UiTheme.Lead;
                case SandboxPreviewLayout.Kind.Subtitle:
                    return UiTheme.Dim;
                case SandboxPreviewLayout.Kind.Note:
                case SandboxPreviewLayout.Kind.Empty:
                    return UiTheme.Faint;
                default:
                    return UiTheme.Name;
            }
        }
    }
}
