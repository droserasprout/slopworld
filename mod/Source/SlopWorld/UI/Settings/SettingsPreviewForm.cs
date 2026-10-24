using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Owns scrolling and measured listing height for settings with a preview. Pages retain
    // their fields, preview renderer, overlays, and apply policy; SettingsPreviewGeometry owns placement.
    public sealed class SettingsPreviewForm
    {
        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly ContentHeight _height;
        readonly UiScrollbarReservation _reservation;

        public SettingsPreviewForm(
            float initialHeight,
            UiScrollbarReservation reservation = UiScrollbarReservation.Always
        )
        {
            _height = new ContentHeight(initialHeight);
            _reservation = reservation;
        }

        public void Draw(
            Rect inner,
            float previewHeight,
            Action<Listing_Standard> fields,
            Action<Rect, Rect> preview
        )
        {
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Small;
                float formHeight = _height.BeginFrame(Time.frameCount);
                // Pin the preview while at least one form row fits. Short windows scroll
                // the form and preview together, within the body's existing footer boundary.
                bool stacked = SettingsPreviewGeometry.ShouldStack(
                    inner.height,
                    UiTheme.RowH,
                    previewHeight,
                    UiTheme.RowH,
                    UiTheme.GapXS,
                    UiTheme.GapM
                );
                if (stacked)
                {
                    float blockHeight = UiTheme.RowH + UiTheme.GapXS + previewHeight;
                    var geometry = UiScrollBody.Measure(
                        inner,
                        formHeight + UiTheme.GapM + blockHeight,
                        _reservation
                    );
                    var layout = SettingsPreviewGeometry.Arrange(
                        geometry.ContentWidth,
                        inner.height,
                        true,
                        formHeight,
                        previewHeight,
                        UiTheme.RowH,
                        UiTheme.GapXS,
                        UiTheme.GapM
                    );
                    using (_scroll.Scope(inner, geometry.View))
                    {
                        DrawFields(UiRect.ToRect(layout.Form), fields);
                        preview(
                            UiRect.ToRect(layout.PreviewCaption),
                            UiRect.ToRect(layout.Preview)
                        );
                    }
                }
                else
                {
                    var layout = SettingsPreviewGeometry.Arrange(
                        inner.width,
                        inner.height,
                        false,
                        formHeight,
                        previewHeight,
                        UiTheme.RowH,
                        UiTheme.GapXS,
                        UiTheme.GapM
                    );
                    var form = Place(inner, layout.Form);
                    var geometry = UiScrollBody.Measure(form, formHeight, _reservation);
                    using (_scroll.Scope(form, geometry.View))
                        DrawFields(geometry.View, fields);
                    preview(Place(inner, layout.PreviewCaption), Place(inner, layout.Preview));
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
                if (begun)
                    listing.End();
            }
        }

        static Rect Place(Rect origin, UiLayoutRect local) =>
            new Rect(origin.x + local.X, origin.y + local.Y, local.Width, local.Height);
    }
}
