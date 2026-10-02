using UnityEngine;
using RimWorld;
using Verse;
using Verse.Sound;

namespace SlopWorld
{
    // Small interactive cells use an explicit hover policy so local dialogs cannot answer
    // map-overlay hover. In contrast, Map rows can opt into OverlayAware at their call site.
    public static class ToggleCell
    {
        public static bool Draw(Rect rect, string label, bool on, string tip = null,
                                bool locked = false, bool warn = false,
                                RowHoverPolicy hoverPolicy = RowHoverPolicy.Local)
        {
            using (WidgetState.Save())
            {
                bool over = RowChrome.Hover(rect, false, !locked, hoverPolicy);
                if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(rect, tip);
                var box = UiControls.TickBox(
                    new Rect(rect.x + 1f, rect.y, UiControls.TickW, rect.height), on, locked);
                GUI.color = locked ? UiTheme.Faint : warn ? UiTheme.Warn
                    : over ? UiTheme.Lead : UiTheme.Name;
                UiText.RowLabel(new Rect(box.xMax + UiTheme.GapS, rect.y,
                    rect.xMax - box.xMax - UiTheme.GapS, rect.height), label);
                if (locked || !UiButtons.RowButton(rect)) return on;
                SoundDefOf.Click.PlayOneShotOnCamera();
                return !on;
            }
        }

        public static bool DrawCheck(Rect rect, bool on, string tip = null,
                                     bool locked = false,
                                     RowHoverPolicy hoverPolicy = RowHoverPolicy.Local)
        {
            using (WidgetState.Save())
            {
                RowChrome.Hover(rect, false, !locked, hoverPolicy);
                if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(rect, tip);
                UiControls.TickBox(new Rect(rect.center.x - UiControls.TickW / 2f,
                    rect.y, UiControls.TickW, rect.height), on, locked);
                if (locked || !UiButtons.RowButton(rect)) return on;
                SoundDefOf.Click.PlayOneShotOnCamera();
                return !on;
            }
        }
    }

    // Icon picker cells own the standard well and hit target. The caller only supplies the
    // icon drawing because cursor previews and ThingDefs have different render primitives.
    public static class IconPickerCell
    {
        public static bool Draw(Rect area, System.Action<Rect> draw, string tip,
                                RowHoverPolicy hoverPolicy = RowHoverPolicy.Local)
        {
            using (WidgetState.Save())
            {
                float boxW = Mathf.Min(UiTheme.FieldH,
                    Mathf.Max(0f, area.width - UiTheme.GapS));
                var box = new Rect(area.center.x - boxW / 2f,
                    area.y + (area.height - boxW) / 2f, boxW, boxW);
                return DrawBoxCore(box, draw,
                    r => { if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip); },
                    hoverPolicy);
            }
        }

        public static bool Draw(Rect area, System.Action<Rect> draw, TipSignal tip,
                                RowHoverPolicy hoverPolicy = RowHoverPolicy.Local)
        {
            using (WidgetState.Save())
            {
                float boxW = Mathf.Min(UiTheme.FieldH,
                    Mathf.Max(0f, area.width - UiTheme.GapS));
                var box = new Rect(area.center.x - boxW / 2f,
                    area.y + (area.height - boxW) / 2f, boxW, boxW);
                return DrawBoxCore(box, draw, r => TooltipHandler.TipRegion(r, tip),
                    hoverPolicy);
            }
        }

        public static bool DrawBox(Rect box, System.Action<Rect> draw, string tip,
                                   RowHoverPolicy hoverPolicy = RowHoverPolicy.Local)
        {
            using (WidgetState.Save())
                return DrawBoxCore(box, draw,
                    r => { if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip); },
                    hoverPolicy);
        }

        public static bool DrawBox(Rect box, System.Action<Rect> draw, TipSignal tip,
                                   RowHoverPolicy hoverPolicy = RowHoverPolicy.Local)
        {
            using (WidgetState.Save())
                return DrawBoxCore(box, draw, r => TooltipHandler.TipRegion(r, tip), hoverPolicy);
        }

        static bool DrawBoxCore(Rect box, System.Action<Rect> draw,
                                System.Action<Rect> showTip, RowHoverPolicy hoverPolicy)
        {
            Slab.Box(box, UiTheme.Well, UiTheme.Edge);
            draw?.Invoke(box.ContractedBy(UiTheme.IconInset));
            RowChrome.Hover(box, false, true, hoverPolicy);
            showTip?.Invoke(box);
            return UiButtons.RowButton(box);
        }
    }

    public sealed class UiChoice
    {
        public string Group;
        public string Label;
        public string Tip;
        public bool On;
        public bool Locked;
        public bool Warn;
        public System.Action<bool> Changed;
    }

    // Framed choice-list contract: inset, empty state, scrolling, row pitch, and checkbox
    // input are shared. Adapters calculate dependency/implicit state and receive changes.
    public static class UiChoiceList
    {
        public static void Draw(Rect outer, System.Collections.Generic.IList<UiChoice> choices,
                                SmoothScroll scroll, string empty)
        {
            using (WidgetState.Save())
            {
                Slab.Box(outer, UiTheme.Well, UiTheme.Edge);
                var pad = outer.ContractedBy(UiTheme.ListInset);
                if (choices == null || choices.Count == 0)
                {
                    using (WidgetState.Save())
                    {
                        GUI.color = UiTheme.Dim;
                        UiText.RowLabel(new Rect(pad.x + UiTheme.GapS, pad.y,
                            pad.width - UiTheme.GapS,
                            UiTheme.LineH), empty);
                    }
                    return;
                }

                float contentH = choices.Count * UiTheme.RowH;
                string group = null;
                foreach (var choice in choices)
                {
                    if (choice.Group != group && !string.IsNullOrEmpty(choice.Group)) contentH += UiTheme.RowH;
                    group = choice.Group;
                }
                var geometry = UiScrollBody.Measure(pad, contentH,
                    UiScrollbarReservation.WhenNeeded);
                using (scroll.Scope(pad, geometry.View))
                {
                    float y = 0f;
                    group = null;
                    foreach (var choice in choices)
                    {
                        if (choice.Group != group && !string.IsNullOrEmpty(choice.Group))
                        {
                            UiLayout.SectionHeading(new Rect(UiTheme.GapS, y,
                                geometry.View.width - UiTheme.GapS, UiTheme.RowH), choice.Group);
                            y += UiTheme.RowH;
                        }
                        group = choice.Group;
                        var cell = new Rect(UiTheme.GapS, y,
                            geometry.View.width - UiTheme.GapS, UiTheme.RowH);
                        y += UiTheme.RowH;
                        bool next = ToggleCell.Draw(cell, choice.Label, choice.On, choice.Tip,
                            choice.Locked, choice.Warn, RowHoverPolicy.Local);
                        if (next != choice.On)
                        {
                            choice.On = next;
                            choice.Changed?.Invoke(next);
                        }
                    }
                }
            }
        }
    }
}
