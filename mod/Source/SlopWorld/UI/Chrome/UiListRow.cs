using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Agents and projects share the same two-line row pitch and the same small amount of
    // frame geometry. Their row contents stay separate because their actions differ.
    public static class UiListRow
    {
        public static float TwoLineH =>
                UiTheme.GapXS + UiTheme.LineH + UiTheme.RowBtnH + UiTheme.GapXS +
                UiTheme.GapXS;

        public static void Prepare(Rect rect)
        {
            RowChrome.Hover(rect, false, true, RowHoverPolicy.OverlayAware);
            Text.Font = GameFont.Small;
        }

        public static float LineY(Rect rect, int line) =>
            rect.y + UiTheme.GapXS + line * UiTheme.LineH;

        public static float Right(Rect rect) => rect.xMax - UiTheme.GapS;
    }
}
