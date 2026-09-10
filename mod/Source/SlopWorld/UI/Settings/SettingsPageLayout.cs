using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public static class SettingsPageLayout
    {
        public static Rect Body(Rect page, bool footer = true) => ToRect(SettingsLayout.Body(
            FromRect(page), UiWidgets.GapM, footer ? UiWidgets.BtnH : 0f, UiWidgets.GapS));

        public static Rect Footer(Rect page) =>
            ToRect(SettingsLayout.Footer(FromRect(page), UiWidgets.BtnH));

        public static Rect Inset(Rect rect, float padding) =>
            ToRect(SettingsLayout.Inset(FromRect(rect), padding));

        public static UiLayoutRect FromRect(Rect r) => new UiLayoutRect(r.x, r.y, r.width, r.height);
        public static Rect ToRect(UiLayoutRect r) => new Rect(r.X, r.Y, r.Width, r.Height);
    }

    // The page keeps this owner across resizes, preserving field identity and scroll.
    public sealed class SettingsForm
    {
        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly SettingsContentHeight _height = new SettingsContentHeight();

        public void Draw(Rect rect, Action<Listing_Standard> fields)
        {
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Small;
                var view = UiScrollBody.View(rect, _height.BeginFrame(Time.frameCount));
                using (_scroll.Scope(rect, view))
                {
                    var listing = new Listing_Standard { maxOneColumn = true };
                    listing.Begin(new Rect(0f, 0f, view.width, UiWidgets.ListingHeight));
                    try
                    {
                        fields(listing);
                        _height.Measure(listing.CurHeight + UiWidgets.GapS);
                    }
                    finally { listing.End(); }
                }
            }
        }
    }
}
