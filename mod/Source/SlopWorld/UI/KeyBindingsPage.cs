using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Lists `StripKeys.Kept` as an options page; clicking a key cell opens the listener.
    // It replaces vanilla's all-bindings Modify dialog, which would expose stripped keys.
    public class KeyBindingsPage : IOptionPage
    {
        public void Load() { }

        // A row holds a button, so it is a button's height; the category band is a tiny line.
        // Both off the font: written down, they crop their own labels on any face taller than
        // the one they were set against.
        static float RowH => SlopWidgets.BtnH;
        static float CatH => SlopWidgets.TinyRowH + 4f;
        const float Gap = 4f;

        // Room for the longest bind there is, measured rather than guessed: a chord with two
        // modifiers on it is what has to fit, and at a larger font 180 is not it.
        static float KeyW => Mathf.Max(SlopWidgets.Wide("Ctrl + Shift + Backspace") + 24f, 180f);
        const float Indent = 12f;

        // Which categories are folded. In-memory only, the way every other fold is.
        readonly HashSet<KeyBindingCategoryDef> _folded =
            new HashSet<KeyBindingCategoryDef>();

        // True while we are waiting for a key press for a given binding.
        KeyBindingDef _listening;
        KeyPrefs.BindingSlot _bindingSlot;

        // The scroll position for the whole page.
        readonly SmoothScroll _scroll = new SmoothScroll();

        public void Draw(Rect rect)
        {
            SlopWidgets.PageCaption(rect, "Keyboard shortcuts  \u2013  click a key to rebind");

            var body = SlopWidgets.PageBody(rect);
            SlopWidgets.Card(body);
            var inner = body.ContractedBy(SlopWidgets.GapM);

            // Build the content model once per frame.
            var cats = DefDatabase<KeyBindingCategoryDef>.AllDefs
                .OrderBy(c => c.defName)
                .ToList();

            var bindingsMap = DefDatabase<KeyBindingDef>.AllDefs
                .Where(b => b.category != null && StripKeys.Kept(b))
                .GroupBy(b => b.category)
                .ToDictionary(g => g.Key, g => g.OrderBy(b => b.defName).ToList());

            // Measure total content height.
            float totalH = MeasureCategories(cats, bindingsMap);
            // Room for the Restore Defaults button at the foot.
            totalH += SlopWidgets.BtnH + SlopWidgets.GapS + SlopWidgets.GapS;

            // Scroll view for the list area.
            var innerRect = new Rect(0f, 0f, inner.width - SlopWidgets.ScrollbarW,
                Mathf.Max(totalH, inner.height));
            _scroll.Begin(inner, innerRect);

            float y = 0f;

            foreach (var cat in cats)
            {
                List<KeyBindingDef> list;
                if (!bindingsMap.TryGetValue(cat, out list) || list.Count == 0) continue;
                y += DrawCategory(new Rect(0f, y, innerRect.width, innerRect.height - y),
                    cat, list);
            }

            // "Restore defaults" at the bottom of the scroll content.
            y += SlopWidgets.GapS;
            DrawRestoreDefaults(new Rect(0f, y, innerRect.width, SlopWidgets.BtnH));

            _scroll.End();

            // Handle key capture while listening — this catches keys the buttons miss.
            if (_listening != null)
                CaptureKey();
        }

        float MeasureCategories(List<KeyBindingCategoryDef> cats,
            Dictionary<KeyBindingCategoryDef, List<KeyBindingDef>> bindingsMap)
        {
            float height = 0f;
            foreach (var cat in cats)
            {
                List<KeyBindingDef> list;
                if (!bindingsMap.TryGetValue(cat, out list) || list.Count == 0) continue;
                height += CatH;
                if (!_folded.Contains(cat))
                    height += list.Count * RowH + Gap;
            }
            return height;
        }

        float DrawCategory(Rect rect, KeyBindingCategoryDef cat, List<KeyBindingDef> list)
        {
            float y = rect.y;
            bool folded = _folded.Contains(cat);

            var headRect = new Rect(rect.x, y, rect.width, CatH);
            bool overHead = Mouse.IsOver(headRect);
            if (overHead) Slab.Fill(headRect, SlopWidgets.Hover);

            float arrowSize = 10f;
            var arrowRect = new Rect(headRect.x, headRect.y + (CatH - arrowSize) / 2f,
                arrowSize, arrowSize);
            GUI.color = SlopWidgets.Faint;
            GUI.DrawTexture(arrowRect, folded ? TexButton.Reveal : TexButton.Collapse);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            float lx = arrowRect.xMax + 4f;
            string tail = folded ? $"  {list.Count}" : "";
            GUI.color = SlopWidgets.Dim;
            SlopWidgets.RowLabel(
                new Rect(lx, headRect.y, rect.width - lx - Gap, CatH),
                cat.label + tail);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            // Heading click: fold/unfold.
            if (overHead && Event.current.rawType == EventType.MouseDown && Event.current.button == 0)
            {
                if (!_folded.Remove(cat)) _folded.Add(cat);
                Event.current.Use();
            }

            y += CatH;
            if (folded) return y - rect.y;

            foreach (var binding in list)
                y += DrawBinding(new Rect(Indent, y, rect.width - Indent, RowH), binding);
            y += Gap;
            return y - rect.y;
        }

        float DrawBinding(Rect rect, KeyBindingDef binding)
        {
            bool over = Mouse.IsOver(rect);
            if (over && _listening != binding)
                Slab.Fill(rect, SlopWidgets.RowBg);

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = SlopWidgets.Name;
            float labelW = rect.width - KeyW - Gap;
            SlopWidgets.RowLabel(new Rect(rect.x, rect.y, labelW, RowH), binding.label);
            GUI.color = Color.white;

            // Key button: click to rebind.
            var keyRect = new Rect(rect.xMax - KeyW, rect.y, KeyW, RowH);
            if (_listening == binding)
            {
                // Listening state: show a primary-style button asking for input.
                SlopWidgets.Button(keyRect, "Press a key...", SlopWidgets.Btn.Primary);
            }
            else
            {
                string keyLabel = BindingLabel(binding);

                // Click side selects the main/alternate slot; do not choose the first empty
                // slot or a populated primary key could never be replaced.
                var ev = Event.current;
                if (Mouse.IsOver(keyRect) && ev.rawType == EventType.MouseDown
                                          && ev.button == 1)
                {
                    _bindingSlot = KeyPrefs.BindingSlot.B;
                    _listening = binding;
                    ev.Use();
                }
                else if (SlopWidgets.Button(keyRect, keyLabel, SlopWidgets.Btn.Default))
                {
                    _bindingSlot = KeyPrefs.BindingSlot.A;
                    _listening = binding;
                }

                TooltipHandler.TipRegion(keyRect,
                    "Click to set the main key, right-click for the alternate.\n\n" +
                    "Esc cancels, Delete clears the slot.");
            }

            Text.Anchor = TextAnchor.UpperLeft;
            return RowH;
        }

        float DrawRestoreDefaults(Rect rect)
        {
            if (SlopWidgets.Button(rect, "Restore defaults", SlopWidgets.Btn.Ghost))
            {
                KeyPrefs.KeyPrefsData.ResetToDefaults();
                KeyPrefs.Save();
                Messages.Message("SlopWorld: key bindings restored to defaults.",
                    MessageTypeDefOf.TaskCompletion, false);
            }
            return SlopWidgets.BtnH;
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
                        $"SlopWorld: {LabelOf(code)} is also on \"{clash.label}\".",
                        MessageTypeDefOf.CautionInput, false);
            }

            data.SetBinding(_listening, _bindingSlot, code);
            KeyPrefs.Save();
            _listening = null;
            e.Use();
        }

        // Return the first duplicate among kept bindings. Conflicts warn rather than refuse;
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

        // Builds the display label from both key slots, so "F1 / Shift+F1" shows both.
        static string BindingLabel(KeyBindingDef binding)
        {
            var data = KeyPrefs.KeyPrefsData;
            KeyCode keyA = data.GetBoundKeyCode(binding, KeyPrefs.BindingSlot.A);
            KeyCode keyB = data.GetBoundKeyCode(binding, KeyPrefs.BindingSlot.B);

            bool hasA = keyA != KeyCode.None;
            bool hasB = keyB != KeyCode.None;

            if (!hasA && !hasB) return "(none)";
            if (!hasB) return LabelOf(keyA);
            if (!hasA) return LabelOf(keyB);
            return $"{LabelOf(keyA)} / {LabelOf(keyB)}";
        }

        static string LabelOf(KeyCode key) => key switch
        {
            KeyCode.None => "",
            KeyCode.Return => "Enter",
            KeyCode.Escape => "Esc",
            KeyCode.LeftShift => "Shift",
            KeyCode.RightShift => "Shift",
            KeyCode.LeftAlt => "Alt",
            KeyCode.RightAlt => "Alt",
            KeyCode.LeftControl => "Ctrl",
            KeyCode.RightControl => "Ctrl",
            KeyCode.LeftCommand => "Cmd",
            KeyCode.RightCommand => "Cmd",
            KeyCode.LeftWindows => "Win",
            KeyCode.RightWindows => "Win",
            _ => key.ToString()
        };

        static readonly HashSet<KeyCode> IgnoredKeys = new HashSet<KeyCode>
        {
            KeyCode.Escape,
            KeyCode.Mouse0, KeyCode.Mouse1, KeyCode.Mouse2,
            KeyCode.Mouse3, KeyCode.Mouse4, KeyCode.Mouse5, KeyCode.Mouse6,
        };
    }
}
