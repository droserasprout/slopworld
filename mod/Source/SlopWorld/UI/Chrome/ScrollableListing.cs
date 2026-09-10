using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // A retained Listing_Standard host owns the scroll and measured content extent. Callers
    // keep their data, footer, overlay and save/load policy outside this lifecycle.
    public sealed class ScrollableListing
    {
        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly SettingsContentHeight _height;

        public ScrollableListing(float initialHeight = 900f)
        {
            _height = new SettingsContentHeight(initialHeight);
        }

        public void Draw(Rect frame, Action<Listing_Standard> drawListing,
                         Func<Rect, float, float> drawTrailing = null)
        {
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Small;
                var geometry = UiScrollBody.Measure(frame,
                    _height.BeginFrame(Time.frameCount), UiScrollbarReservation.Always);
                using (_scroll.Scope(frame, geometry.View))
                {
                    var listing = new Listing_Standard { maxOneColumn = true };
                    bool begun = false;
                    float y = 0f;
                    try
                    {
                        listing.Begin(new Rect(0f, 0f, geometry.View.width,
                            UiLayout.ListingHeight));
                        begun = true;
                        drawListing(listing);
                        y = listing.CurHeight;
                    }
                    finally
                    {
                        if (begun) listing.End();
                    }

                    if (drawTrailing != null) y = drawTrailing(geometry.View, y);
                    _height.Measure(y + UiTheme.GapS);
                }
            }
        }
    }
}
