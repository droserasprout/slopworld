using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public static class SettingsPageLayout
    {
        public static Rect Body(Rect page) => UiRect.ToRect(SettingsLayout.Body(
            UiRect.FromRect(page), UiTheme.GapM, UiTheme.BtnH, UiTheme.GapS));

        public static Rect BodyWithoutFooter(Rect page) => UiRect.ToRect(SettingsLayout.Body(
            UiRect.FromRect(page), UiTheme.GapM, 0f, UiTheme.GapS));

        public static Rect Footer(Rect page) =>
            UiRect.ToRect(SettingsLayout.Footer(UiRect.FromRect(page), UiTheme.BtnH));

        public static Rect Inset(Rect rect, float padding) =>
            UiRect.ToRect(SettingsLayout.Inset(UiRect.FromRect(rect), padding));

        // Pages keep their scroll state and frame-stable measurements. This only lays
        // out the reserved scrollbar and padded content in scroll-local coordinates.
        public static Rect ScrollView(Rect viewport, float contentHeight,
            float paddingX, float paddingY, out Rect content)
        {
            var view = UiScrollBody.Measure(viewport, contentHeight,
                UiScrollbarReservation.Always).View;
            view.width = Mathf.Max(1f, view.width);
            content = new Rect(paddingX, paddingY,
                Mathf.Max(1f, view.width - paddingX * 2f),
                Mathf.Max(1f, view.height - paddingY * 2f));
            return view;
        }

    }

    // The page keeps this owner across resizes, preserving field identity and scroll.
    public sealed class SettingsForm
    {
        readonly ScrollableListing _listing = new ScrollableListing();

        public void Draw(Rect rect, Action<Listing_Standard> fields) =>
            _listing.Draw(rect, fields);
    }
}
