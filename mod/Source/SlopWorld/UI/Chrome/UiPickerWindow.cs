using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Shared picker contract: placement, title/close chrome, and fixed-cell grid geometry
    // belong to the popup. Callers own choice lookup, selection, tooltips, and local hover.
    public static class UiPickerWindow
    {
        const float WindowTopOffset = 50f;
        const float TitleWidthInset = 60f;
        const float CloseButtonRightInset = 48f;
        const float CloseButtonWidth = 44f;
        const float CloseButtonTopInset = 2f;

        public struct Grid
        {
            public readonly Rect Frame;
            public readonly Rect View;
            public readonly int Columns;
            public readonly float Cell;
            public readonly float IconSize;

            internal Grid(Rect frame, Rect view, int columns)
            {
                Frame = frame;
                View = view;
                Columns = columns;
                Cell = UiTheme.PickerCell;
                IconSize = UiTheme.PickerIcon;
            }
        }

        public static void Show(int id, Rect page, float width, float height, string title,
                                Action close, int count, SmoothScroll scroll,
                                Action<Grid> drawGrid, float titleGap = -1f,
                                float bottomGap = -1f)
        {
            if (titleGap < 0f) titleGap = UiTheme.GapS;
            if (bottomGap < 0f) bottomGap = UiTheme.GapS;
            var window = Place(page, width, height);
            Find.WindowStack.ImmediateWindow(id, window, WindowLayer.Super,
                () => DrawContents(width, height, title, close, count, scroll, drawGrid,
                    titleGap, bottomGap), true, false, 1f);
        }

        public static Rect Place(Rect page, float width, float height)
        {
            var window = new Rect(page.x + (page.width - width) / 2f,
                page.y + WindowTopOffset, width, height);
            window.x = ClampStart(window.x, page.x, page.xMax, width);
            window.y = ClampStart(window.y, page.y, page.yMax, height);
            return window;
        }

        static float ClampStart(float start, float pageStart, float pageEnd, float size)
        {
            float min = pageStart + UiTheme.GapS;
            float max = pageEnd - UiTheme.GapS - size;
            // A picker can be wider or taller than a narrow settings page. In that case no
            // placement fits both edges. Anchor it to the page's leading inset rather than
            // letting the ordinary clamp produce an inverted range.
            if (max < min) return min;
            return Mathf.Clamp(start, min, max);
        }

        static void DrawContents(float width, float height, string title, Action close,
                                 int count, SmoothScroll scroll, Action<Grid> drawGrid,
                                 float titleGap, float bottomGap)
        {
            using (WidgetState.Save())
            {
                var r = new Rect(0f, 0f, width, height);
                Slab.Box(r, UiTheme.PopoverBg, UiTheme.Edge);

                Text.Font = GameFont.Small;
                GUI.color = UiTheme.Lead;
                UiText.RowLabel(new Rect(r.x + UiTheme.GapS, r.y + UiTheme.GapXS,
                    r.width - TitleWidthInset, UiTheme.LineH), title);
                GUI.color = Color.white;

                if (UiButtons.Button(
                        new Rect(r.width - CloseButtonRightInset, r.y + CloseButtonTopInset,
                            CloseButtonWidth, UiTheme.RowBtnH),
                        "X", UiTheme.Btn.Ghost))
                    close?.Invoke();

                var grid = Layout(r, count, titleGap, bottomGap);
                using (scroll.Scope(grid.Frame, grid.View))
                    drawGrid?.Invoke(grid);
            }
        }

        static Grid Layout(Rect r, int count, float titleGap, float bottomGap)
        {
            float cell = UiTheme.PickerCell;
            float gridTop = r.y + UiTheme.GapXS + UiTheme.LineH + titleGap;
            float gridH = r.height - gridTop - bottomGap;
            float availableW = r.width - UiTheme.GapM;
            int columns = Mathf.Max(1, Mathf.FloorToInt(availableW / cell));
            int rows = Mathf.CeilToInt(count / (float)columns);
            float totalH = rows * cell;
            bool scrolls = totalH > gridH;
            if (scrolls)
            {
                // The scrollbar is drawn beside the view, inside the frame. Keep its
                // reserve out of the cell grid rather than letting it cover the last cell.
                columns = Mathf.Max(1, Mathf.FloorToInt(
                    (availableW - UiTheme.ScrollbarW) / cell));
                rows = Mathf.CeilToInt(count / (float)columns);
                totalH = rows * cell;
            }

            float contentW = columns * cell;
            float frameW = contentW + (scrolls ? UiTheme.ScrollbarW : 0f);
            var frame = new Rect(r.x + (r.width - frameW) / 2f, gridTop, frameW, gridH);
            var view = new Rect(0f, 0f, contentW, Mathf.Max(totalH, gridH));
            return new Grid(frame, view, columns);
        }
    }
}
