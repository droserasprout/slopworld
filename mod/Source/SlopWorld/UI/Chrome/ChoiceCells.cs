using UnityEngine;
using RimWorld;
using Verse;
using Verse.Sound;

namespace SlopWorld
{
    // Small interactive cells use an explicit hover policy so local dialogs cannot answer
    // map-overlay hover, while map rows can opt into OverlayAware at their call site.
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
                var box = UiWidgets.TickBox(
                    new Rect(rect.x + 1f, rect.y, UiWidgets.TickW, rect.height), on, locked);
                GUI.color = locked ? UiWidgets.Faint : warn ? UiWidgets.Warn
                    : over ? UiWidgets.Lead : UiWidgets.Name;
                UiWidgets.RowLabel(new Rect(box.xMax + UiWidgets.GapS, rect.y,
                    rect.xMax - box.xMax - UiWidgets.GapS, rect.height), label);
                if (locked || !UiWidgets.RowButton(rect)) return on;
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
                UiWidgets.TickBox(new Rect(rect.center.x - UiWidgets.TickW / 2f,
                    rect.y, UiWidgets.TickW, rect.height), on, locked);
                if (locked || !UiWidgets.RowButton(rect)) return on;
                SoundDefOf.Click.PlayOneShotOnCamera();
                return !on;
            }
        }
    }

    // Icon picker cells own the standard well and hit target; the caller only supplies the
    // icon drawing because cursor previews and ThingDefs have different render primitives.
    public static class IconPickerCell
    {
        public static bool Draw(Rect area, System.Action<Rect> draw, string tip,
                                RowHoverPolicy hoverPolicy = RowHoverPolicy.Local)
        {
            using (WidgetState.Save())
            {
                float boxW = Mathf.Min(UiWidgets.FieldH,
                    Mathf.Max(0f, area.width - UiWidgets.GapS));
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
                float boxW = Mathf.Min(UiWidgets.FieldH,
                    Mathf.Max(0f, area.width - UiWidgets.GapS));
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
            Slab.Box(box, UiWidgets.Well, UiWidgets.Edge);
            draw?.Invoke(box.ContractedBy(UiWidgets.IconInset));
            RowChrome.Hover(box, false, true, hoverPolicy);
            showTip?.Invoke(box);
            return UiWidgets.RowButton(box);
        }
    }

    public sealed class UiChoice<T>
    {
        public T Value;
        public string Label;
        public string Tip;
        public bool On;
        public bool Locked;
        public bool Warn;
        public System.Action<bool> Changed;
    }

    // Framed choice-list contract: inset, empty state, scrolling, row pitch, and checkbox
    // input are shared; adapters calculate dependency/implicit state and receive changes.
    public static class UiChoiceList<T>
    {
        public static void Draw(Rect outer, System.Collections.Generic.IList<UiChoice<T>> choices,
                                SmoothScroll scroll, string empty)
        {
            using (WidgetState.Save())
            {
                Slab.Box(outer, UiWidgets.Well, UiWidgets.Edge);
                var pad = outer.ContractedBy(UiWidgets.ListInset);
                if (choices == null || choices.Count == 0)
                {
                    using (WidgetState.Save())
                    {
                        GUI.color = UiWidgets.Dim;
                        UiWidgets.RowLabel(new Rect(pad.x + UiWidgets.GapS, pad.y,
                            pad.width - UiWidgets.GapS,
                            UiWidgets.LineH), empty);
                    }
                    return;
                }

                float contentH = choices.Count * UiWidgets.RowH;
                var geometry = UiScrollBody.Measure(pad, contentH,
                    UiScrollbarReservation.WhenNeeded);
                using (scroll.Scope(pad, geometry.View))
                {
                    float y = 0f;
                    foreach (var choice in choices)
                    {
                        var cell = new Rect(UiWidgets.GapS, y,
                            geometry.View.width - UiWidgets.GapS, UiWidgets.RowH);
                        y += UiWidgets.RowH;
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
