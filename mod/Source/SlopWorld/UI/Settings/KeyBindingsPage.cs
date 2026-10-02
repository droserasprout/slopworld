using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Lists `StripKeys.Kept` as an options page. Clicking a key cell opens the listener.
    // It replaces vanilla's all-bindings Modify dialog, which would expose stripped keys.
    public class KeyBindingsPage : IOptionPage
    {
        public void Load() { }

        // A row holds a button, so it is a button's height. The category band is a tiny line.
        // Both off the font: written down, they crop their own labels on any face taller than
        // the one they were set against.
        static float RowH => UiTheme.BtnH;
        static float CatH => UiTheme.TinyRowH + UiTheme.GapXS;
        static float Gap => UiTheme.GapXS;

        // Room for the longest bind there is, measured rather than guessed. A chord with two
        // modifiers on it is what has to fit, and at a larger font 180 is not it.
        static float KeyW => Mathf.Max(UiTheme.Wide("Ctrl + Shift + Backspace") + 24f, 180f);
        const float Indent = 12f;

        // Which categories are folded. In-memory only, the way every other fold is.
        readonly HashSet<KeyBindingCategoryDef> _folded =
            new HashSet<KeyBindingCategoryDef>();

        // True while we are waiting for a key press for a given binding.
        KeyBindingDef _listening;
        KeyPrefs.BindingSlot _bindingSlot;

        // The terminal chrome checks this before dispatching its own hotkeys. A binding
        // page must be able to receive a key that is normally one of those hotkeys.
        internal bool Listening => _listening != null;

        // The scroll position for the whole page.
        readonly SmoothScroll _scroll = new SmoothScroll();

        public void Draw(Rect rect)
        {
            var inner = SettingsPageLayout.BodyWithoutFooter(rect);

            // Build the content model once per frame.
            var cats = DefDatabase<KeyBindingCategoryDef>.AllDefs
                .OrderBy(c => c.defName)
                .ToList();

            var bindingsMap = DefDatabase<KeyBindingDef>.AllDefs
                .Where(b => b.category != null && StripKeys.Kept(b))
                .GroupBy(b => b.category)
                .ToDictionary(g => g.Key, g => g.OrderBy(b => b.defaultKeyCodeA)
                    .ThenBy(b => b.defName).ToList());

            // Measure total content height.
            float contentWidth = UiScrollBody.Measure(inner, 0f,
                UiScrollbarReservation.Always).ContentWidth;
            float totalH = MeasureCategories(cats, bindingsMap, contentWidth);
            // Room for the Restore Defaults button at the foot.
            totalH += UiTheme.BtnH + UiTheme.GapS + UiTheme.GapS;

            // Scroll view for the list area.
            var geometry = UiScrollBody.Measure(inner, totalH,
                UiScrollbarReservation.Always);
            using (_scroll.Scope(inner, geometry.View))
            {

                float y = 0f;

                foreach (var cat in cats)
                {
                    List<KeyBindingDef> list;
                    if (!bindingsMap.TryGetValue(cat, out list) || list.Count == 0) continue;
                    y += DrawCategory(new Rect(0f, y, geometry.View.width,
                        geometry.View.height - y),
                        cat, list);
                }

                // "Restore defaults" at the bottom of the scroll content.
                y += UiTheme.GapS;
                DrawRestoreDefaults(new Rect(0f, y, geometry.View.width, UiTheme.BtnH));

            }

            // Handle key capture while listening — this catches keys the buttons miss.
            if (_listening != null)
                CaptureKey();
        }

        float MeasureCategories(List<KeyBindingCategoryDef> cats,
            Dictionary<KeyBindingCategoryDef, List<KeyBindingDef>> bindingsMap, float width)
        {
            float height = 0f;
            foreach (var cat in cats)
            {
                List<KeyBindingDef> list;
                if (!bindingsMap.TryGetValue(cat, out list) || list.Count == 0) continue;
                height += CatH;
                if (!_folded.Contains(cat))
                    height += list.Count * BindingHeight(BindingWidth(width)) + Gap;
            }
            return height;
        }

        float DrawCategory(Rect rect, KeyBindingCategoryDef cat, List<KeyBindingDef> list)
        {
            using (WidgetState.Save())
            {
                float y = rect.y;
                bool folded = _folded.Contains(cat);

                var headRect = new Rect(rect.x, y, rect.width, CatH);
                bool overHead = Mouse.IsOver(headRect);
                if (overHead) Slab.Fill(headRect, UiTheme.Hover);

                float arrowSize = 10f;
                var arrowRect = new Rect(headRect.x, headRect.y + (CatH - arrowSize) / 2f,
                    arrowSize, arrowSize);
                GUI.color = UiTheme.Faint;
                GUI.DrawTexture(arrowRect, folded ? TexButton.Reveal : TexButton.Collapse);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                float lx = arrowRect.xMax + Gap;
                string tail = folded ? $"  {list.Count}" : "";
                GUI.color = UiTheme.Dim;
                UiText.RowLabel(
                    new Rect(lx, headRect.y, rect.width - lx - Gap, CatH),
                    cat.label + tail);

                // Heading click: fold/unfold.
                if (overHead && Event.current.rawType == EventType.MouseDown && Event.current.button == 0)
                {
                    if (!_folded.Remove(cat)) _folded.Add(cat);
                    Event.current.Use();
                }

                y += CatH;
                if (folded) return y - rect.y;

                foreach (var binding in list)
                {
                    float rowWidth = BindingWidth(rect.width);
                    float rowHeight = BindingHeight(rowWidth);
                    y += DrawBinding(new Rect(rect.x + Mathf.Min(Indent, rect.width), y,
                        rowWidth, rowHeight), binding);
                }
                y += Gap;
                return y - rect.y;
            }
        }

        static float BindingWidth(float width) => Mathf.Max(0f, width - Indent);

        static float BindingHeight(float width) => width < KeyW + 160f + Gap
            ? UiTheme.LineH + Gap + RowH : RowH;

        float DrawBinding(Rect rect, KeyBindingDef binding)
        {
            using (WidgetState.Save())
            {
                bool over = Mouse.IsOver(rect);
                if (over && _listening != binding)
                    Slab.Fill(rect, UiTheme.RowBg);

                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = UiTheme.Name;
                bool stacked = rect.height > RowH;
                float keyW = Mathf.Min(KeyW, rect.width);
                float labelW = stacked ? rect.width : Mathf.Max(0f, rect.width - keyW - Gap);
                UiText.RowLabel(new Rect(rect.x, rect.y, labelW, stacked ? UiTheme.LineH : RowH), binding.label);

                // Key button: click to rebind.
                var keyRect = new Rect(stacked ? rect.x : rect.xMax - keyW,
                    stacked ? rect.yMax - RowH : rect.y, keyW, RowH);
                if (_listening == binding)
                {
                    // Listening state: show a primary-style button asking for input.
                    UiButtons.Button(keyRect, "Press a key", UiTheme.Btn.Primary);
                }
                else
                {
                    string keyLabel = ShortcutLabels.Binding(binding);

                    // Click side selects the main/alternate slot. Do not choose the first empty
                    // slot or a populated primary key could never be replaced.
                    var ev = Event.current;
                    if (Mouse.IsOver(keyRect) && ev.rawType == EventType.MouseDown
                                              && ev.button == 1)
                    {
                        _bindingSlot = KeyPrefs.BindingSlot.B;
                        _listening = binding;
                        ev.Use();
                    }
                    else if (UiButtons.Button(keyRect, keyLabel, UiTheme.Btn.Default))
                    {
                        _bindingSlot = KeyPrefs.BindingSlot.A;
                        _listening = binding;
                    }

                    TooltipHandler.TipRegion(keyRect,
                        "Click to set the main key, right-click for the alternate.\n\n" +
                        "Esc cancels, Delete clears the slot. Modifiers are not configurable.");
                }

                return rect.height;
            }
        }

        float DrawRestoreDefaults(Rect rect)
        {
            if (UiButtons.Button(rect, "Restore defaults", UiTheme.Btn.Ghost))
            {
                KeyPrefs.KeyPrefsData.ResetToDefaults();
                KeyPrefs.Save();
                Messages.Message("SlopWorld: key bindings restored to defaults.",
                    MessageTypeDefOf.TaskCompletion, false);
            }
            return UiTheme.BtnH;
        }

        // Called at the end of the frame when we are waiting for a key. The key press
        // arrives as a KeyDown event on a subsequent event pass.
        void CaptureKey()
        {
            var e = Event.current;
            if (e.rawType != EventType.KeyDown) return;

            if (e.keyCode == KeyCode.Escape)
            {
                // Escape cancels the rebinding.
                _listening = null;
                e.Use();
                return;
            }

            // Unity emits a separate KeyDown for each modifier before the key at the end of
            // a chord. KeyPrefs stores only the key code, so modifiers are prefixes rather
            // than bindings of their own. Keep listening until the actual key arrives.
            if (IgnoredKeys.Contains(e.keyCode)) return;

            var data = KeyPrefs.KeyPrefsData;
            // Delete empties the slot rather than binding Delete to it, which is the only
            // way back from a key added by mistake.
            var code = e.keyCode == KeyCode.Delete ? KeyCode.None : e.keyCode;

            if (code != KeyCode.None)
            {
                var clash = Conflict(code, _listening);
                if (clash != null)
                    Messages.Message(
                        $"SlopWorld: {ShortcutLabels.Key(code)} is also on \"{clash.label}\".",
                        MessageTypeDefOf.CautionInput, false);
            }

            data.SetBinding(_listening, _bindingSlot, code);
            KeyPrefs.Save();
            _listening = null;
            e.Use();
        }

        // Return the first duplicate among kept bindings. Conflicts warn rather than refuse.
        // dropped bindings are hidden and unbound, so reporting them would name an inactive row.
        static KeyBindingDef Conflict(KeyCode code, KeyBindingDef except)
        {
            var data = KeyPrefs.KeyPrefsData;
            foreach (var b in DefDatabase<KeyBindingDef>.AllDefs)
            {
                if (b == except || !StripKeys.Kept(b)) continue;
                if (data.GetBoundKeyCode(b, KeyPrefs.BindingSlot.A) == code
                    || data.GetBoundKeyCode(b, KeyPrefs.BindingSlot.B) == code)
                    return b;
            }
            return null;
        }

        static readonly HashSet<KeyCode> IgnoredKeys = new HashSet<KeyCode>
        {
            KeyCode.Escape,
            KeyCode.Mouse0, KeyCode.Mouse1, KeyCode.Mouse2,
            KeyCode.Mouse3, KeyCode.Mouse4, KeyCode.Mouse5, KeyCode.Mouse6,
            KeyCode.LeftShift, KeyCode.RightShift,
            KeyCode.LeftControl, KeyCode.RightControl,
            KeyCode.LeftAlt, KeyCode.RightAlt,
            KeyCode.LeftCommand, KeyCode.RightCommand,
            KeyCode.LeftWindows, KeyCode.RightWindows,
        };
    }
}
