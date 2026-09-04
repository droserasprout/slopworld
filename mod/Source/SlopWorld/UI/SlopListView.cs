using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public abstract class SlopListView<T> : IContentView
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

            SlopWidgets.Header(rect, Title, hub);

            float top = rect.y + SlopWidgets.HeaderH + SlopWidgets.GapS;
            float foot = SlopWidgets.BtnH + SlopWidgets.GapS;
            DrawList(new Rect(rect.x, top, rect.width, rect.yMax - foot - top), hub);

            DoFooter(new Rect(rect.x, rect.yMax - SlopWidgets.BtnH, rect.width,
                SlopWidgets.BtnH), hub);
        }

        void DrawList(Rect rect, SessionHub hub)
        {
            var items = Rows.ToList();
            var view = new Rect(0f, 0f, rect.width - SlopWidgets.ScrollbarW,
                items.Count * RowH + SlopWidgets.GapXS);

            using (_scroll.Scope(rect, view))
            {
                if (items.Count == 0)
                {
                    using (WidgetState.Save())
                    {
                        GUI.color = SlopWidgets.Dim;
                        string note = hub.Online ? EmptyNote : SlopWidgets.Unreachable;
                        Widgets.Label(
                            new Rect(SlopWidgets.GapXS, SlopWidgets.GapS,
                                view.width - SlopWidgets.GapS,
                                Text.CalcHeight(note, view.width - SlopWidgets.GapS)),
                            note);
                    }
                }

                float y = 0f;
                foreach (var item in items)
                {
                    DrawRow(new Rect(0f, y, view.width, RowH - SlopWidgets.GapXS), item);
                    y += RowH;
                }
            }
        }
    }
}
