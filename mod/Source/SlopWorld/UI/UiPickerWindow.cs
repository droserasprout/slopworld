using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Shared picker contract: placement, title/close chrome, and fixed-cell grid geometry
    // belong to the popup; callers own choice lookup, selection, tooltips, and local hover.
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
                Cell = UiWidgets.PickerCell;
                IconSize = UiWidgets.PickerIcon;
            }
        }

        public static void Show(int id, Rect page, float width, float height, string title,
                                Action close, int count, SmoothScroll scroll,
                                Action<Grid> drawGrid, float titleGap = UiWidgets.GapS,
                                float bottomGap = UiWidgets.GapS)
        {
            var window = Place(page, width, height);
            Find.WindowStack.ImmediateWindow(id, window, WindowLayer.Super,
                () => DrawContents(width, height, title, close, count, scroll, drawGrid,
                    titleGap, bottomGap), true, false, 1f);
        }

        public static Rect Place(Rect page, float width, float height)
        {
            var window = new Rect(page.x + (page.width - width) / 2f,
                page.y + WindowTopOffset, width, height);
            if (window.yMax > page.yMax - UiWidgets.GapS)
                window.y = page.yMax - UiWidgets.GapS - height;
            if (window.y < page.y + UiWidgets.GapS)
                window.y = page.y + UiWidgets.GapS;
            return window;
        }

        static void DrawContents(float width, float height, string title, Action close,
                                 int count, SmoothScroll scroll, Action<Grid> drawGrid,
                                 float titleGap, float bottomGap)
        {
            using (WidgetState.Save())
            {
                var r = new Rect(0f, 0f, width, height);
                Text.Font = GameFont.Small;
                UiWidgets.RowLabel(new Rect(r.x + UiWidgets.GapS, r.y + UiWidgets.GapXS,
                    r.width - TitleWidthInset, UiWidgets.LineH), title);

                if (UiWidgets.Button(
                        new Rect(r.width - CloseButtonRightInset, r.y + CloseButtonTopInset,
                            CloseButtonWidth, UiWidgets.RowBtnH),
                        "X", UiWidgets.Btn.Ghost))
                    close?.Invoke();

                var grid = Layout(r, count, titleGap, bottomGap);
                using (scroll.Scope(grid.Frame, grid.View))
                    drawGrid?.Invoke(grid);
            }
        }

        static Grid Layout(Rect r, int count, float titleGap, float bottomGap)
        {
            float cell = UiWidgets.PickerCell;
            float gridTop = r.y + UiWidgets.GapXS + UiWidgets.LineH + titleGap;
            float gridH = r.height - gridTop - bottomGap;
            float availableW = r.width - UiWidgets.GapM;
            int columns = Mathf.Max(1, Mathf.FloorToInt(availableW / cell));
            int rows = Mathf.CeilToInt(count / (float)columns);
            float totalH = rows * cell;
            bool scrolls = totalH > gridH;
            if (scrolls)
            {
                // The scrollbar is drawn beside the view, inside the frame. Keep its
                // reserve out of the cell grid rather than letting it cover the last cell.
                columns = Mathf.Max(1, Mathf.FloorToInt(
                    (availableW - UiWidgets.ScrollbarW) / cell));
                rows = Mathf.CeilToInt(count / (float)columns);
                totalH = rows * cell;
            }

            float contentW = columns * cell;
            float frameW = contentW + (scrolls ? UiWidgets.ScrollbarW : 0f);
            var frame = new Rect(r.x + (r.width - frameW) / 2f, gridTop, frameW, gridH);
            var view = new Rect(0f, 0f, contentW, Mathf.Max(totalH, gridH));
            return new Grid(frame, view, columns);
        }
    }
}
