using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SlopWorld
{
    public abstract class UiLayout : UiControls
    {
        // Listing_Standard uses this oversized rect so a scroll body can measure its content
        // without constraining the listing to the current viewport.
        public const float ListingHeight = 4000f;

        // A scene has the board and everything else stands down, so the room goes back
        // rather than leaving a button row indented against nothing.
        public static bool Shown => !Cutscene.Playing;

        public static float LeftInset => Shown && !Settings.SidebarHidden ? AgentSidebar.Width : 0f;

        public static float TopInset => Shown ? TopBar.H : 0f;

        // Screenshot mode filters vanilla chrome separately; the top bar and inspect controls
        // run before that filter, so Hidden is independent of layout insets.
        public static bool Hidden => Find.ScreenshotModeHandler?.FiltersCurrentEvent ?? false;

        // One measurement rule for every action row, kept beside the layout helpers rather
        // than duplicated by individual windows.
        public static float BtnW(string label, float floor) =>
            Mathf.Max(Wide(label) + ButtonPadX * 2f, floor);

        // Form buttons use their content width; callers that own fixed geometry (row action
        // clusters, key cells, footer bars) continue to use Button(Rect, ...).
        public static bool Button(Listing_Standard l, string label, Btn kind = Btn.Default,
                                  bool on = true)
        {
            var r = l.GetRect(BtnH);
            r.width = Mathf.Min(r.width, BtnW(label, ButtonMinW));
            bool clicked = Button(r, label, kind, on);
            // Keep consecutive form buttons legible without making each caller remember
            // the minimum gap. Callers can still add a larger group gap when needed.
            l.Gap(GapS);
            return clicked;
        }

        // An icon that answers to a press, in the chrome's own rectangular hover surface.
        //
        // `tint` is the icon's color at rest - a disabled errand hands over a faded one -
        // and it goes to full white under the mouse.
        public static bool IconButton(Rect r, Texture2D icon, Color tint, bool on = true)
        {
            return IconButton(r, icon, tint, IconInset, on);
        }

        // A compact icon can keep a deliberate inset while sharing the same hover and press
        // treatment as a full-size chrome icon (the window close cross is the one case).
        public static bool IconButton(Rect r, Texture2D icon, Color tint, float inset,
                                      bool on = true)
        {
            using (WidgetState.Save()) return IconButtonCore(r, icon, tint, inset, on);
        }

        static bool IconButtonCore(Rect r, Texture2D icon, Color tint, float inset, bool on)
        {
            bool over = on && Mouse.IsOver(r);
            bool held = over && Input.GetMouseButton(0);

            if (over) Slab.Fill(r, held ? BtnDown : BtnHover);

            using (WidgetState.Save())
            {
                GUI.color = on ? (over ? Lead : tint) : Fade(tint, 0.5f);
                GUI.DrawTexture(r.ContractedBy(inset), icon);
            }

            if (!RowButton(r, on)) return false;

            SoundDefOf.Click.PlayOneShotOnCamera();
            return true;
        }

        public static bool IconButton(Rect r, Texture2D icon, bool on = true) =>
            IconButton(r, icon, Name, on);

        public static void SectionHeading(Listing_Standard l, string text)
        {
            SectionHeading(l.GetRect(RowH), text);
            l.Gap(GapS);
        }

        public static void Note(Listing_Standard l, string text)
        {
            using (WidgetState.Save())
            {
                GUI.color = Dim;
                l.Label(text);
            }
        }

        public struct Bar
        {
            Rect _r;
            float _left, _right;

            public Bar(Rect r) { _r = r; _left = 0f; _right = 0f; }

            public bool Left(string label, Btn kind = Btn.Default, bool on = true)
            {
                float w = Wide(label);
                var at = new Rect(_r.x + _left, _r.y, w, UiWidgets.BtnH);
                _left += w + UiWidgets.GapS;
                return UiWidgets.Button(at, label, kind, on);
            }

            public bool Right(string label, Btn kind = Btn.Default, bool on = true)
            {
                float w = Wide(label);
                var at = new Rect(_r.xMax - _right - w, _r.y, w, UiWidgets.BtnH);
                _right += w + UiWidgets.GapS;
                return UiWidgets.Button(at, label, kind, on);
            }

            public Rect Rest()
            {
                float x = _r.x + _left;
                return new Rect(x, _r.y,
                    Mathf.Max(_r.xMax - _right - UiWidgets.GapS - x, 0f),
                    UiWidgets.BtnH);
            }

            // Measured at Small whatever the caller left the font at. [BtnH] is a fixed
            // height cut for that face, so a width taken against another one gives a box
            // that does not match its own row.
            static float Wide(string label)
            {
                using (WidgetState.Save())
                {
                    Verse.Text.Font = GameFont.Small;
                    return UiWidgets.BtnW(label, UiWidgets.ButtonMinW);
                }
            }
        }

        public const string Unreachable =
            "Daemon unreachable. Is slopd running?  systemctl --user status slopd";

        public static void Fail(string msg) =>
            Messages.Message($"SlopWorld: {msg}", MessageTypeDefOf.RejectInput, false);

        // A checked menu row wears the same box a settings page does, before the label
        // rather than after it: `UiMenu` reads `extraPartRightJustified` and puts the
        // part on the left when it is false. One checkbox everywhere, so a tick means the
        // same thing wherever it is read.
        public static FloatMenuOption MenuToggle(string label, bool on, Action act)
        {
            var opt = new FloatMenuOption(label, act, MenuOptionPriority.Default, null, null,
                TickColW, r => DrawTick(r, on));
            opt.extraPartRightJustified = false;
            return opt;
        }

        // OS font lists often expose a whole foundry under one leading word: Noto alone can
        // account for dozens of faces. Keep those families behind one menu while leaving
        // names with no siblings as one-click choices.
        public static IEnumerable<FloatMenuOption> GroupedFontOptions(IEnumerable<string> names,
                                                                       Action<string> choose)
        {
            var groups = (names ?? Enumerable.Empty<string>())
                .Where(name => !string.IsNullOrEmpty(name))
                .GroupBy(FontFamily, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                var faces = group.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
                if (faces.Count == 1)
                {
                    yield return FontOption(faces[0], choose);
                    continue;
                }

                var family = group.Key;
                yield return new UiSubmenu(family, () => faces
                    .Select(name => FontOption(name, choose))
                    .ToList());
            }
        }

        static string FontFamily(string name)
        {
            int space = name.IndexOf(' ');
            return space > 0 ? name.Substring(0, space) : name;
        }

        static FloatMenuOption FontOption(string name, Action<string> choose) =>
            new FloatMenuOption(name, () => choose(name));

        static bool DrawTick(Rect r, bool on)
        {
            TickBox(r, on);
            // extraPartOnGUI's return means the extra part handled the click; the row does.
            return false;
        }

        public static void Header(Rect rect, string title, SessionHub hub)
        {
            using (WidgetState.Save())
            {
                Title(rect, title);
                Status(new Rect(rect.x, rect.y, rect.width, HeaderH), hub);
            }
        }

        static void Status(Rect line, SessionHub hub)
        {
            string text = $"{DaemonClient.BaseUrl} - {hub.Status}";
            float w = Wide(text);

            float h = RowH;
            var badge = new Rect(line.xMax - (w + StatusMarker + GapS * 3f),
                line.y + (line.height - h) / 2f,
                w + StatusMarker + GapS * 3f, h);
            Slab.Box(badge, Well, Edge);

            var marker = new Rect(badge.x + GapS, badge.y + (h - StatusMarker) / 2f,
                StatusMarker, StatusMarker);
            Slab.Fill(marker, hub.Online ? Yes : Bad);

            using (WidgetState.Save())
            {
                GUI.color = Dim;
                RowLabel(new Rect(marker.xMax + GapS, badge.y, w + 2f, h), text);
            }
        }

        public static void SectionHeading(Rect r, string text)
        {
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Small;
                float w = Wide(text);
                GUI.color = Faint;
                RowLabel(r, text);
                float x = r.x + w + GapS;
                if (x < r.xMax)
                    Slab.Hairline(new Rect(x, r.y + r.height / 2f, r.xMax - x, 1f), Edge);
            }
        }

        public static void PageCaption(Rect page, string text)
        {
            using (WidgetState.Save())
            {
                GUI.color = Dim;
                RowLabel(new Rect(page.x, page.y, page.width, RowH), text);
            }
        }

        // Settings pages leave room for their footer, but otherwise use the tab's whole
        // content area. The old caption and card chrome are intentionally gone.
        public static Rect PageBody(Rect page) =>
            new Rect(page.x, page.y, page.width, page.height - BtnH - GapS);

        public static Rect FooterBar(Rect rect) =>
            new Rect(rect.x, rect.yMax - BtnH, rect.width, BtnH);

        public static void Title(Rect rect, string text)
        {
            using (WidgetState.Save())
            {
                var line = new Rect(rect.x, rect.y, rect.width, HeaderH);
                Text.Font = GameFont.Medium;
                GUI.color = Lead;
                RowLabel(line, text);
                Slab.Hairline(new Rect(rect.x, line.yMax, rect.width, 1f), Edge);
            }
        }

        public static string PathList(Rect r, string name, string label, string text)
        {
            float h = RowH;
            SectionHeading(new Rect(r.x, r.y, r.width, h), label);
            var box = new Rect(r.x, r.y + h + GapXS, r.width,
                Mathf.Max(r.height - h - GapXS, 40f));
            return Area(box, name, text);
        }

        public static string FreeName(string name, IEnumerable<string> taken, string fallback)
            => NameTools.FreeName(name, taken, fallback);

        public static void DrawRail<T>(Rect r, (string label, T tab)[] tabs, ref T active)
        {
            float y = r.y;
            foreach (var tab in tabs)
                y = RailTab(r, y, tab.label, tab.tab, ref active);
        }

        static float RailTab<T>(Rect r, float y, string label, T tab, ref T active)
        {
            if (Button(new Rect(r.x, y, r.width, BtnH), label,
                    EqualityComparer<T>.Default.Equals(active, tab) ? Btn.Primary : Btn.Ghost))
            {
                if (!EqualityComparer<T>.Default.Equals(active, tab))
                    TextFieldSelection.ReleaseFocus();
                active = tab;
            }
            return y + BtnH + GapS;
        }
    }
}
