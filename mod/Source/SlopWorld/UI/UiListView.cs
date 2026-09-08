using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public abstract class UiListView<T> : IContentView
    {
        readonly SmoothScroll _scroll = new SmoothScroll();

        public abstract string Title { get; }

        protected abstract float RowH { get; }

        protected abstract string EmptyNote { get; }

        protected abstract IEnumerable<T> Rows { get; }

        protected abstract void DrawRow(Rect r, T item);

        protected abstract void DoFooter(Rect bar, SessionHub hub);

        public virtual void Opened() { }

        public virtual void Closed() { }

        public void Draw(Rect rect)
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
            var items = Rows.ToList();
            PerfTrace.Count("ui-list-rows-copied", items.Count);
            float contentH = items.Count * RowH + UiWidgets.GapXS;
            var view = UiScrollBody.ConditionalView(rect, contentH);

            using (_scroll.Scope(rect, view))
            {
                if (items.Count == 0)
                {
                    string note = hub.Online ? EmptyNote : UiWidgets.Unreachable;
                    UiWidgets.StatusLabel(new Rect(UiWidgets.GapXS, UiWidgets.GapS,
                            view.width - UiWidgets.GapS, view.height), note, UiWidgets.Dim);
                }

                float y = 0f;
                PerfTrace.Count("ui-list-rows-drawn", items.Count);
                foreach (var item in items)
                {
                    DrawRow(new Rect(0f, y, view.width, RowH - UiWidgets.GapXS), item);
                    y += RowH;
                }
            }
        }
    }
}
