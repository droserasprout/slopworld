using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public sealed class SelectorOption
    {
        public readonly string Label;
        public readonly Action Choose;
        public readonly bool Enabled;

        public SelectorOption(string label, Action choose, bool enabled = true)
        {
            Label = label;
            Choose = choose;
            Enabled = enabled;
        }
    }

    // One labelled selector pattern for forms. The same box determines both measured menu
    // width and screen anchor, including custom and disabled options.
    public static class UiSelector
    {
        // Anchors are in screen coordinates so menus can be reopened outside the source GUI group.
        // Filters keep their natural menu width; single-value fields match the source box.
        public static UiMenu Menu(
            List<FloatMenuOption> options,
            Rect screenBox,
            bool matchWidth = true
        ) =>
            new UiMenu(
                options,
                new Vector2(screenBox.x, screenBox.yMax),
                selector: true,
                selectorWidth: matchWidth ? screenBox.width : 0f
            );

        public static Rect ScreenBox(Rect box) =>
            new Rect(UiControls.MenuAt(box) - new Vector2(0f, box.height), box.size);

        public static bool DrawInline(
            Rect box,
            string value,
            Func<List<FloatMenuOption>> options,
            Action<UiMenu> openMenu = null
        )
        {
            if (!UiControls.SelectInline(box, value))
                return false;
            var menu = Menu(options(), ScreenBox(box));
            if (openMenu != null)
                openMenu(menu);
            else
                Find.WindowStack.Add(menu);
            return true;
        }

        // Stable label catalogs let forms measure without constructing menu actions.
        // Width is still resolved with current text metrics and bounded by the form.
        public static bool Draw(
            Listing_Standard listing,
            string caption,
            string value,
            IEnumerable<string> labels,
            Func<List<FloatMenuOption>> options,
            out Rect box,
            string tip = null
        )
        {
            var rect = listing.GetRect(UiTheme.LineH + UiTheme.GapXS + UiTheme.CompactH);
            bool pressed = UiControls.Select(
                rect,
                caption,
                value,
                out box,
                tip,
                forcedWidth: UiMenu.WidthFor(new[] { value }.Concat(labels))
            );
            listing.Gap(UiTheme.GapS);
            if (!pressed)
                return false;
            Find.WindowStack.Add(Menu(options(), ScreenBox(box)));
            return true;
        }

        public static bool Draw(
            Rect rect,
            string caption,
            string value,
            IEnumerable<FloatMenuOption> source,
            out Rect box,
            string tip = null,
            bool enabled = true,
            bool open = false,
            Action<UiMenu> openMenu = null
        )
        {
            var options = (source ?? Enumerable.Empty<FloatMenuOption>()).ToList();
            bool pressed = UiControls.Select(
                rect,
                caption,
                value,
                out box,
                tip,
                enabled,
                open,
                Mathf.Max(UiMenu.WidthFor(new[] { value }), UiMenu.WidthFor(options))
            );
            if (!pressed)
                return false;

            var popup = Menu(options, ScreenBox(box));
            if (openMenu != null)
                openMenu(popup);
            else
                Find.WindowStack.Add(popup);
            return true;
        }

        public static bool Draw(
            Rect rect,
            string caption,
            string value,
            IEnumerable<SelectorOption> source,
            out Rect box,
            string tip = null,
            bool enabled = true,
            bool open = false,
            Action<UiMenu> openMenu = null
        )
        {
            var options = (source ?? Enumerable.Empty<SelectorOption>()).ToList();
            bool pressed = UiControls.Select(
                rect,
                caption,
                value,
                out box,
                tip,
                enabled,
                open,
                UiMenu.WidthFor(new[] { value }.Concat(options.Select(option => option.Label)))
            );
            if (!pressed)
                return false;

            var menu = options
                .Select(option =>
                {
                    var item = new FloatMenuOption(option.Label, option.Choose);
                    item.Disabled = !option.Enabled;
                    return item;
                })
                .ToList();
            var popup = Menu(menu, ScreenBox(box));
            if (openMenu != null)
                openMenu(popup);
            else
                Find.WindowStack.Add(popup);
            return true;
        }

        public static bool Draw(
            Listing_Standard listing,
            string caption,
            string value,
            IEnumerable<FloatMenuOption> source,
            out Rect box,
            string tip = null,
            bool enabled = true,
            bool open = false,
            Action<UiMenu> openMenu = null
        )
        {
            var rect = listing.GetRect(UiTheme.LineH + UiTheme.GapXS + UiTheme.CompactH);
            bool pressed = Draw(
                rect,
                caption,
                value,
                source,
                out box,
                tip,
                enabled,
                open,
                openMenu
            );
            listing.Gap(UiTheme.GapS);
            return pressed;
        }

        public static bool Draw(
            Listing_Standard listing,
            string caption,
            string value,
            IEnumerable<SelectorOption> source,
            out Rect box,
            string tip = null,
            bool enabled = true,
            bool open = false,
            Action<UiMenu> openMenu = null
        )
        {
            var rect = listing.GetRect(UiTheme.LineH + UiTheme.GapXS + UiTheme.CompactH);
            bool pressed = Draw(
                rect,
                caption,
                value,
                source,
                out box,
                tip,
                enabled,
                open,
                openMenu
            );
            listing.Gap(UiTheme.GapS);
            return pressed;
        }
    }
}
