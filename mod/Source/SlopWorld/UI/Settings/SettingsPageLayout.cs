using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public static class SettingsPageLayout
    {
        public static Rect Body(Rect page, bool footer = true) => ToRect(SettingsLayout.Body(
            FromRect(page), UiTheme.GapM, footer ? UiTheme.BtnH : 0f, UiTheme.GapS));

        public static Rect Footer(Rect page) =>
            ToRect(SettingsLayout.Footer(FromRect(page), UiTheme.BtnH));

        public static Rect Inset(Rect rect, float padding) =>
            ToRect(SettingsLayout.Inset(FromRect(rect), padding));

        public static UiLayoutRect FromRect(Rect r) => new UiLayoutRect(r.x, r.y, r.width, r.height);
        public static Rect ToRect(UiLayoutRect r) => new Rect(r.X, r.Y, r.Width, r.Height);
    }

    // The page keeps this owner across resizes, preserving field identity and scroll.
    public sealed class SettingsForm
    {
        readonly ScrollableListing _listing = new ScrollableListing();

        public void Draw(Rect rect, Action<Listing_Standard> fields) =>
            _listing.Draw(rect, fields);
    }
}
