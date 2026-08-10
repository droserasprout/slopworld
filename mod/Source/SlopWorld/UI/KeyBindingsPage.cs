using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The key bindings that used to sit behind the "Modify" button on the Controls tab,
    // drawn as a page of the options menu instead. Every binding this game still answers
    // to, grouped by category, each one a row that says what it does and what key it is on
    // now - and clicking the key area opens the listener for a new press.
    //
    // Every binding, not every def: what is listed here is StripKeys.Kept, the camera and
    // the colonist bar and Escape and ours. The Controls tab's own "Modify" button is one
    // of StripOptions' dropped rows now, so vanilla's Dialog_KeyBindings - which would list
    // all of them, kept or not - has no door left to open by, and this is the one place a
    // key is set.
    //
    // A page rather than a Window because SlopOptions hangs it off an OptionCategoryDef.
    public class KeyBindingsPage
    {
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
        Vector2 _scroll;

        public void Draw(Rect rect)
        {
            SlopWidgets.PageCaption(rect, "Keyboard shortcuts  \u2013  click a key to rebind");

            var body = SlopWidgets.PageBody(rect);
            Widgets.DrawMenuSection(body);
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
            float totalH = 0f;
            foreach (var cat in cats)
            {
                List<KeyBindingDef> list;
                if (!bindingsMap.TryGetValue(cat, out list) || list.Count == 0) continue;
                totalH += CatH;
                if (_folded.Contains(cat)) continue;
                totalH += list.Count * RowH + Gap;
            }
            // Room for the Restore Defaults button at the foot.
            totalH += SlopWidgets.BtnH + SlopWidgets.GapS + SlopWidgets.GapS;

            // Scroll view for the list area.
            var innerRect = new Rect(0f, 0f, inner.width - 18f,
                Mathf.Max(totalH, inner.height));
            Widgets.BeginScrollView(inner, ref _scroll, innerRect);

            float y = 0f;

            foreach (var cat in cats)
            {
                List<KeyBindingDef> list;
                if (!bindingsMap.TryGetValue(cat, out list) || list.Count == 0) continue;

                bool folded = _folded.Contains(cat);

                var headRect = new Rect(0f, y, innerRect.width, CatH);
                bool overHead = Mouse.IsOver(headRect);
                if (overHead) Widgets.DrawHighlight(headRect);

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
                    new Rect(lx, headRect.y, innerRect.width - lx - Gap, CatH),
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
                if (folded) continue;

                foreach (var binding in list)
                {
                    float rh = RowH;
                    var r = new Rect(Indent, y, innerRect.width - Indent, rh);

                    bool over = Mouse.IsOver(r);
                    if (over && _listening != binding)
                        Widgets.DrawBoxSolid(r, SlopWidgets.RowBg);

                    // Label
                    Text.Anchor = TextAnchor.MiddleLeft;
                    GUI.color = SlopWidgets.Name;
                    float labelW = r.width - KeyW - Gap;
                    SlopWidgets.RowLabel(new Rect(r.x, r.y, labelW, rh), binding.label);
                    GUI.color = Color.white;

                    // Key button: click to rebind.
                    var keyRect = new Rect(r.xMax - KeyW, r.y, KeyW, rh);
                    if (_listening == binding)
                    {
                        // Listening state: show a primary-style button asking for input.
                        SlopWidgets.Button(keyRect, "Press a key...", SlopWidgets.Btn.Primary);
                    }
                    else
                    {
                        string keyLabel = BindingLabel(binding);

                        // Left sets the main key, right the alternate. Taken as an event
                        // rather than off the button, which answers the left button only.
                        // Choosing the slot by which one happened to be free - so B for
                        // every binding that already has an A - would mean the key the row
                        // shows first could never be changed, only added to.
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

                    y += rh;
                }

                y += Gap;
            }

            // "Restore defaults" at the bottom of the scroll content.
            y += SlopWidgets.GapS;
            if (SlopWidgets.Button(
                    new Rect(0f, y, innerRect.width, SlopWidgets.BtnH),
                    "Restore defaults", SlopWidgets.Btn.Ghost))
            {
                KeyPrefs.KeyPrefsData.ResetToDefaults();
                KeyPrefs.Save();
                Messages.Message("SlopWorld: key bindings restored to defaults.",
                    MessageTypeDefOf.TaskCompletion, false);
            }

            Widgets.EndScrollView();

            // Handle key capture while listening — this catches keys the buttons miss.
            if (_listening != null)
                CaptureKey();
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

        // The first other binding already holding this key, or null. A warning rather than
        // a refusal: vanilla lets two things share a key and so does this, but silently
        // shadowing a key the player set an hour ago is not something to do without a word.
        //
        // Over the kept ones only. A dropped binding still holds whatever key it was
        // shipped with, and naming one - "P is also on Misc 12" - would report a clash with
        // something the player cannot see, cannot change, and that does not fire.
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
