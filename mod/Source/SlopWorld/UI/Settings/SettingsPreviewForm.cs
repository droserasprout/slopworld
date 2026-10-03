using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Owns scrolling and measured listing height for settings with a preview. Pages retain
    // their fields, preview renderer, overlays, and apply policy; SettingsPreviewLayout owns geometry.
    public sealed class SettingsPreviewForm
    {
        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly ContentHeight _height;
        readonly SettingsPreviewLayout _layout = new SettingsPreviewLayout();
        readonly UiScrollbarReservation _reservation;

        public SettingsPreviewForm(float initialHeight,
            UiScrollbarReservation reservation = UiScrollbarReservation.Always)
        {
            _height = new ContentHeight(initialHeight);
            _reservation = reservation;
        }

        public void Draw(Rect inner, float previewHeight, Action<Listing_Standard> fields,
            Action<Rect, Rect> preview, int contentRevision = 0)
        {
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Small;
                float formHeight = _height.BeginFrame(Time.frameCount);
                float blockHeight = UiTheme.RowH + UiTheme.GapXS + previewHeight;
                // Pin the preview while at least one form row fits. Short windows scroll
                // the form and preview together, within the body's existing footer boundary.
                bool stacked = inner.height < UiTheme.RowH + UiTheme.GapM + blockHeight;
                if (stacked)
                {
                    var geometry = UiScrollBody.Measure(inner,
                        formHeight + UiTheme.GapM + blockHeight, _reservation);
                    _layout.Arrange(geometry.ContentWidth, inner.height, true,
                        formHeight, previewHeight, contentRevision);
                    using (_scroll.Scope(inner, geometry.View))
                    {
                        DrawFields(UiRect.ToRect(_layout.Form), fields);
                        preview(UiRect.ToRect(_layout.PreviewCaption), UiRect.ToRect(_layout.Preview));
                    }
                }
                else
                {
                    _layout.Arrange(inner.width, inner.height, false,
                        formHeight, previewHeight, contentRevision);
                    var form = Place(inner, _layout.Form);
                    var geometry = UiScrollBody.Measure(form, formHeight, _reservation);
                    using (_scroll.Scope(form, geometry.View))
                        DrawFields(geometry.View, fields);
                    preview(Place(inner, _layout.PreviewCaption), Place(inner, _layout.Preview));
                }
            }
        }

        void DrawFields(Rect rect, Action<Listing_Standard> fields)
        {
            var listing = new Listing_Standard { maxOneColumn = true };
            bool begun = false;
            try
            {
                listing.Begin(new Rect(rect.x, rect.y, rect.width, UiLayout.ListingHeight));
                begun = true;
                fields(listing);
                _height.Measure(listing.CurHeight + UiTheme.GapS);
            }
            finally
            {
                if (begun) listing.End();
            }
        }

        static Rect Place(Rect origin, UiLayoutRect local) =>
            new Rect(origin.x + local.X, origin.y + local.Y, local.Width, local.Height);
    }
}
