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

    // The single shared row contract. OverlayAware is the normal interactive policy;
    // popovers and other window-local controls must request Local deliberately.
    public static class RowChrome
    {
        public static bool Hover(Rect rect, bool selected, bool enabled,
                                 RowHoverPolicy hoverPolicy,
                                 RowSelectionStyle selectionStyle = RowSelectionStyle.Standard)
        {
            bool over = enabled && IsOver(rect, hoverPolicy);
            return PaintHover(rect, selected, over, selectionStyle);
        }

        // For scroll-local rows whose outer viewport already resolved hover before entering
        // the translated group. The policy remains explicit even though the boolean is cached.
        public static bool Hover(Rect rect, bool selected, bool enabled, bool hovered,
                                 RowHoverPolicy hoverPolicy,
                                 RowSelectionStyle selectionStyle = RowSelectionStyle.Standard)
        {
            bool over = enabled && hoverPolicy != RowHoverPolicy.None && hovered;
            return PaintHover(rect, selected, over, selectionStyle);
        }

        static bool PaintHover(Rect rect, bool selected, bool over,
                               RowSelectionStyle selectionStyle)
        {
            if (selected) Slab.Fill(rect, selectionStyle == RowSelectionStyle.Palette
                ? UiWidgets.Sel
                : selectionStyle == RowSelectionStyle.Hover
                    ? UiWidgets.Hover : UiWidgets.RowOn);
            if (over) Slab.Fill(rect, UiWidgets.Hover);
            return over;
        }

        static bool IsOver(Rect rect, RowHoverPolicy policy)
        {
            switch (policy)
            {
                case RowHoverPolicy.OverlayAware:
                    return ColonistBarStrip.SidebarHover(rect);
                case RowHoverPolicy.Local:
                    return Mouse.IsOver(rect);
                default:
                    return false;
            }
        }
    }
}
