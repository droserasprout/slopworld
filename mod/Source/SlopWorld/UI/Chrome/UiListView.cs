using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public abstract class UiListView<T> : ContentView
    {
        readonly SmoothScroll _scroll = new SmoothScroll();

        public abstract override string Title { get; }

        protected abstract float RowH { get; }

        protected abstract string EmptyNote { get; }

        protected abstract IList<T> Rows { get; }

        protected abstract void DrawRow(Rect r, T item);

        protected abstract void DoFooter(Rect bar, SessionHub hub);

        public override void Opened() { }

        public override void Closed() { }

        public override void Draw(Rect rect)
        {
            var hub = SessionHub.Instance;

            UiWidgets.Header(rect, Title, hub);

            float top = rect.y + UiWidgets.HeaderH + UiWidgets.GapS;
            float foot = UiWidgets.BtnH + UiWidgets.GapS;
            DrawList(new Rect(rect.x, top, rect.width, rect.yMax - foot - top), hub);

            DoFooter(new Rect(rect.x, rect.yMax - UiWidgets.BtnH, rect.width,
                UiWidgets.BtnH), hub);
        }

        void DrawList(Rect rect, SessionHub hub)
        {
            var items = Rows;
            float contentH = items.Count * RowH + UiWidgets.GapXS;
            var geometry = UiScrollBody.Measure(rect, contentH,
                UiScrollbarReservation.WhenNeeded);

            using (_scroll.Scope(rect, geometry.View))
            {
                if (items.Count == 0)
                {
                    string note = hub.Online ? EmptyNote : UiWidgets.Unreachable;
                    UiWidgets.StatusLabel(new Rect(UiWidgets.GapXS, UiWidgets.GapS,
                            geometry.View.width - UiWidgets.GapS, geometry.View.height), note,
                        UiWidgets.Dim);
                }

                VisibleRows.Uniform(items.Count, RowH, _scroll.Position.y, rect.height,
                    out int first, out int end);
                PerfTrace.Count("ui-list-rows-drawn", end - first);
                for (int i = first; i < end; i++)
                {
                    DrawRow(new Rect(0f, i * RowH, geometry.View.width,
                        RowH - UiWidgets.GapXS), items[i]);
                }
            }
        }
    }
}
