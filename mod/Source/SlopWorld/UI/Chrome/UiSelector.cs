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
        public static bool Draw(Rect rect, string caption, string value,
                                IEnumerable<FloatMenuOption> source, out Rect box,
                                string tip = null, bool enabled = true, bool open = false,
                                Action<UiMenu> openMenu = null)
        {
            var options = (source ?? Enumerable.Empty<FloatMenuOption>()).ToList();
            bool pressed = UiControls.Select(rect, caption, value, out box, tip, enabled,
                open, UiMenu.WidthFor(new[] { value }.Concat(
                    options.Select(option => option.Label))));
            if (!pressed) return false;

            var popup = new UiMenu(options, UiControls.MenuAt(box), selector: true);
            if (openMenu != null) openMenu(popup);
            else Find.WindowStack.Add(popup);
            return true;
        }

        public static bool Draw(Rect rect, string caption, string value,
                                IEnumerable<SelectorOption> source, out Rect box,
                                string tip = null, bool enabled = true, bool open = false,
                                Action<UiMenu> openMenu = null)
        {
            var options = (source ?? Enumerable.Empty<SelectorOption>()).ToList();
            bool pressed = UiControls.Select(rect, caption, value, out box, tip, enabled,
                open, UiMenu.WidthFor(new[] { value }.Concat(
                    options.Select(option => option.Label))));
            if (!pressed) return false;

            var menu = options.Select(option =>
            {
                var item = new FloatMenuOption(option.Label, option.Choose);
                item.Disabled = !option.Enabled;
                return item;
            }).ToList();
            var popup = new UiMenu(menu, UiControls.MenuAt(box), selector: true);
            if (openMenu != null) openMenu(popup);
            else Find.WindowStack.Add(popup);
            return true;
        }

        public static bool Draw(Listing_Standard listing, string caption, string value,
                                IEnumerable<FloatMenuOption> source, out Rect box,
                                string tip = null, bool enabled = true, bool open = false,
                                Action<UiMenu> openMenu = null)
        {
            var rect = listing.GetRect(UiTheme.LineH + UiTheme.GapXS +
                UiTheme.CompactH);
            bool pressed = Draw(rect, caption, value, source, out box, tip, enabled, open,
                openMenu);
            listing.Gap(UiTheme.GapS);
            return pressed;
        }

        public static bool Draw(Listing_Standard listing, string caption, string value,
                                IEnumerable<SelectorOption> source, out Rect box,
                                string tip = null, bool enabled = true, bool open = false,
                                Action<UiMenu> openMenu = null)
        {
            var rect = listing.GetRect(UiTheme.LineH + UiTheme.GapXS +
                UiTheme.CompactH);
            bool pressed = Draw(rect, caption, value, source, out box, tip, enabled, open,
                openMenu);
            listing.Gap(UiTheme.GapS);
            return pressed;
        }
    }
}
