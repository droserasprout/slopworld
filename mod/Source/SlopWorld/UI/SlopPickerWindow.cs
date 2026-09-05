using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Shared picker contract: placement, title/close chrome, and fixed-cell grid geometry
    // belong to the popup; callers own choice lookup, selection, tooltips, and local hover.
    public static class SlopPickerWindow
    {
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
                Cell = SlopWidgets.PickerCell;
                IconSize = SlopWidgets.PickerIcon;
            }
        }

        public static void Show(int id, Rect page, float width, float height, string title,
                                Action close, int count, SmoothScroll scroll,
                                Action<Grid> drawGrid, float titleGap = SlopWidgets.GapS,
                                float bottomGap = SlopWidgets.GapS)
        {
            var window = Place(page, width, height);
            Find.WindowStack.ImmediateWindow(id, window, WindowLayer.Super,
                () => DrawContents(width, height, title, close, count, scroll, drawGrid,
                    titleGap, bottomGap), true, false, 1f);
        }

        public static Rect Place(Rect page, float width, float height)
        {
            var window = new Rect(page.x + (page.width - width) / 2f,
                page.y + 50f, width, height);
            if (window.yMax > page.yMax - SlopWidgets.GapS)
                window.y = page.yMax - SlopWidgets.GapS - height;
            if (window.y < page.y + SlopWidgets.GapS)
                window.y = page.y + SlopWidgets.GapS;
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
                SlopWidgets.RowLabel(new Rect(r.x + SlopWidgets.GapS, r.y + SlopWidgets.GapXS,
                    r.width - 60f, SlopWidgets.LineH), title);

                if (SlopWidgets.Button(
                        new Rect(r.width - 48f, r.y + 2f, 44f, SlopWidgets.RowBtnH),
                        "X", SlopWidgets.Btn.Ghost))
                    close?.Invoke();

                var grid = Layout(r, count, titleGap, bottomGap);
                using (scroll.Scope(grid.Frame, grid.View))
                    drawGrid?.Invoke(grid);
            }
        }

        static Grid Layout(Rect r, int count, float titleGap, float bottomGap)
        {
            float cell = SlopWidgets.PickerCell;
            float gridTop = r.y + SlopWidgets.GapXS + SlopWidgets.LineH + titleGap;
            float gridH = r.height - gridTop - bottomGap;
            int columns = Mathf.Max(1, Mathf.FloorToInt(
                (r.width - SlopWidgets.GapM) / cell));
            float gridW = columns * cell;
            int rows = Mathf.CeilToInt(count / (float)columns);
            float totalH = rows * cell;
            if (totalH > gridH)
                gridW -= SlopWidgets.ScrollbarW;

            columns = Mathf.Max(1, Mathf.FloorToInt(gridW / cell));
            gridW = columns * cell;
            rows = Mathf.CeilToInt(count / (float)columns);
            totalH = rows * cell;

            var frame = new Rect(r.x + (r.width - gridW) / 2f, gridTop, gridW, gridH);
            var view = new Rect(0f, 0f, gridW, Mathf.Max(totalH, gridH));
            return new Grid(frame, view, columns);
        }
    }
}
