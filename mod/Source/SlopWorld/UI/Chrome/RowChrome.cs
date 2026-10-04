using UnityEngine;
using Verse;

namespace SlopWorld
{
    public enum RowHoverPolicy
    {
        OverlayAware,
        Local,
        None,
    }

    public enum RowSelectionStyle { Standard, Palette, Hover }

    // The single shared row contract. OverlayAware is the normal interactive policy.
    // popovers and other window-local controls must request Local deliberately.
    public static class RowChrome
    {
        // The host supplies overlay input policy. Window-local controls keep their native
        // hit test; shared rows do not need to know which map or workspace owns them.
        public static System.Func<Rect, bool> OverlayHitTest { get; set; } = Mouse.IsOver;

        public static bool Hover(Rect rect, bool selected, bool enabled,
                                 RowHoverPolicy hoverPolicy,
                                 RowSelectionStyle selectionStyle = RowSelectionStyle.Standard)
        {
            bool over = enabled && IsOver(rect, hoverPolicy);
            return PaintHover(rect, selected, over, selectionStyle);
        }

        // For scroll-local rows whose outer viewport already resolved hover before entering
        // the translated group. highlighted is the final caller-resolved highlight; None suppresses
        // hover even when it is true. Selection highlighting remains independent.
        public static bool HighlightResolved(Rect rect, bool selected, bool enabled, bool highlighted,
                                 RowHoverPolicy hoverPolicy,
                                 RowSelectionStyle selectionStyle = RowSelectionStyle.Standard)
        {
            bool over = enabled && hoverPolicy != RowHoverPolicy.None && highlighted;
            return PaintHover(rect, selected, over, selectionStyle);
        }

        static bool PaintHover(Rect rect, bool selected, bool over,
                               RowSelectionStyle selectionStyle)
        {
            if (selected) Slab.Fill(rect, selectionStyle == RowSelectionStyle.Palette
                ? UiTheme.Sel
                : selectionStyle == RowSelectionStyle.Hover
                    ? UiTheme.Hover : UiTheme.RowOn);
            if (over) Slab.Fill(rect, UiTheme.Hover);
            return over;
        }

        static bool IsOver(Rect rect, RowHoverPolicy policy)
        {
            switch (policy)
            {
                case RowHoverPolicy.OverlayAware:
                    return OverlayHitTest(rect);
                case RowHoverPolicy.Local:
                    return Mouse.IsOver(rect);
                default:
                    return false;
            }
        }
    }
}
