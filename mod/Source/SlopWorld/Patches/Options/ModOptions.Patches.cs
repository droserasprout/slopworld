using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SlopWorld
{
    public static partial class ModOptions
    {
        // The content view has no OK button; suppress only vanilla's translated OK button
        // while the options view is active.
        [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonText),
            new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool),
                    typeof(TextAnchor?) })]
        public static class Patch_OptionsOk
        {
            static bool Prefix(string label, ref bool __result)
            {
                // This prefix sees every button, so gate before translating the label.
                if (!OptionsView.Anywhere) return true;

                string ok = "OK".Translate();
                if (label != ok) return true;

                __result = false;
                return false;
            }
        }

        // ---------------------------------------------------------------- the column

        // Children omit the icon and indent their labels by ChildIndent.
        const float RowPadX = 10f;
        const float ChildIndent = 12f;
        const float IconGap = 10f;

        // What vanilla lays a row out on: `Rect(0, i * 50, 160, 48)` contracted by 4, which
        // is the only place the row's index is available to read back.
        const float VanillaPitch = 50f;
        const float VanillaInset = 4f;

        // Vanilla reserves a 50px pitch, so top-level rows use the larger computed pitch.
        static float RowH => Mathf.Round(UiTheme.LineH * 1.4f);
        static float NestedRowH => Mathf.Round(UiTheme.LineH * 1.15f);
        static float Pitch => RowH + UiTheme.GapXS;
        static float NestedPitch => NestedRowH + UiTheme.GapXS;

        // Limit the icon box to the row height.
        static float IconBox => Mathf.Min(18f, RowH - 6f);
        static float LabelX => RowPadX + IconBox + IconGap;

        // The vanilla dialog does not retain category scroll state. Content-view options
        // therefore use this compact rail position; it is deliberately separate from every
        // page's scroll lifetime and is clamped again whenever the viewport or font changes.
        static float _railScroll;
        static int _railControl;
        static float _railGrab;

        // Recover the vanilla row index from r.y and recompute the compact row rectangle.
        static Rect Slot(Rect r, Tab tab)
        {
            int i = Mathf.Max(0, Mathf.RoundToInt((r.y - VanillaInset) / VanillaPitch));
            float y = VanillaInset;
            for (int n = 0; n < i; n++)
                y += TabAtIndex(n)?.Parent != null ? NestedPitch : Pitch;

            float h = tab != null && tab.Parent != null ? NestedRowH : RowH;
            return new Rect(r.x, y, r.width, h);
        }

        static int Index(Rect r) =>
            Mathf.Max(0, Mathf.RoundToInt((r.y - VanillaInset) / VanillaPitch));

        static float RailContentHeight()
        {
            float height = VanillaInset;
            var categories = DefDatabase<OptionCategoryDef>.AllDefsListForReading;
            for (int i = 0; i < categories.Count; i++)
                if (!categories[i].isDev)
                    height += TabOf(categories[i])?.Parent != null ? NestedPitch : Pitch;
            return height + UiTheme.GapS;
        }

        static Tab TabAtIndex(int index)
        {
            if (index < 0) return null;
            int visible = 0;
            var categories = DefDatabase<OptionCategoryDef>.AllDefsListForReading;
            for (int i = 0; i < categories.Count; i++)
            {
                if (categories[i].isDev) continue;
                if (visible++ == index) return TabOf(categories[i]);
            }
            return null;
        }

        static float UpdateRailScroll(Rect viewport, float contentHeight)
        {
            float max = Mathf.Max(0f, contentHeight - viewport.height);
            var e = Event.current;
            if (max > 0f && e.type == EventType.ScrollWheel &&
                viewport.Contains(e.mousePosition))
            {
                _railScroll = Mathf.Clamp(_railScroll + e.delta.y * 20f, 0f, max);
                e.Use();
            }
            else
                _railScroll = Mathf.Clamp(_railScroll, 0f, max);
            return max;
        }

        static void DrawRailScrollbar(Rect viewport, float max)
        {
            if (max <= 0f) return;

            float trackW = UiTheme.ScrollTrackW;
            var track = new Rect(viewport.width - trackW, 0f, trackW, viewport.height);
            float thumbH = Mathf.Clamp(track.height * (track.height /
                (track.height + max)), Mathf.Min(24f, track.height), track.height);
            float span = Mathf.Max(0f, track.height - thumbH);
            var thumb = new Rect(track.x + UiTheme.ScrollThumbInset,
                track.y + (span <= 0f ? 0f : span * (_railScroll / max)),
                track.width - UiTheme.ScrollThumbInset * 2f, thumbH);
            int id = GUIUtility.GetControlID(FocusType.Passive, track);
            var e = Event.current;
            if (GUIUtility.hotControl == 0 && e.type == EventType.MouseDown && e.button == 0 &&
                track.Contains(e.mousePosition))
            {
                _railControl = id;
                _railGrab = thumb.Contains(e.mousePosition)
                    ? e.mousePosition.y - thumb.y : thumbH / 2f;
                SetRailScroll(e.mousePosition.y, track, thumbH, max);
                e.Use();
            }
            else if (GUIUtility.hotControl == id && _railControl == id)
            {
                if (e.type == EventType.MouseDrag)
                {
                    SetRailScroll(e.mousePosition.y, track, thumbH, max);
                    e.Use();
                }
                else if (e.type == EventType.MouseUp && e.button == 0)
                {
                    GUIUtility.hotControl = 0;
                    _railControl = 0;
                    e.Use();
                }
            }

            if (e.type != EventType.Repaint) return;
            Slab.Fill(track, UiTheme.ScrollTrough);
            Slab.Fill(thumb, GUIUtility.hotControl == id
                ? UiTheme.ScrollThumbHeld
                : Mouse.IsOver(track) ? UiTheme.ScrollThumbHover : UiTheme.ScrollThumb);
        }

        static void SetRailScroll(float mouseY, Rect track, float thumbH, float max)
        {
            float span = Mathf.Max(0f, track.height - thumbH);
            float t = span <= 0f ? 0f : Mathf.Clamp01(
                (mouseY - _railGrab - track.y) / span);
            _railScroll = t * max;
            GUIUtility.hotControl = _railControl;
        }

        // Draw selected and hovered rows with the mod's colors.
        static void CategoryRow(Rect r, bool selected)
        {
            if (selected) Slab.Fill(r, UiTheme.Sel);
            else if (Mouse.IsOver(r)) Slab.Fill(r, UiTheme.Hover);
        }

        static void Select(Dialog_Options dlg, OptionCategoryDef category)
        {
            dlg.selectedCategory = category;
            dlg.selectedMod = null;
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        // Draw vanilla categories here so nested game rows use the compact layout.
        [HarmonyPatch(typeof(Dialog_Options), "DoCategoryRow")]
        public static class Patch_OptionsRow
        {
            static bool Prefix(Dialog_Options __instance, Rect r, OptionCategoryDef optionCategory)
            {
                var tab = TabOf(optionCategory);
                int index = Index(r);
                var row = Slot(r, tab);
                if (OptionsView.Drawing)
                {
                    var viewport = OptionsView.RailViewport;
                    float max = index == 0
                        ? UpdateRailScroll(viewport, RailContentHeight()) :
                        Mathf.Max(0f, RailContentHeight() - viewport.height);
                    if (index == 0) DrawRailScrollbar(viewport, max);
                    row = new Rect(row.x - viewport.x, row.y - viewport.y - _railScroll,
                        row.width, row.height);
                    if (row.yMax <= 0f || row.y >= viewport.height) return false;

                    GUI.BeginGroup(viewport);
                    try { DrawRow(__instance, row, tab, optionCategory); }
                    finally { GUI.EndGroup(); }
                    return false;
                }

                Text.Font = GameFont.Small;
                DrawRow(__instance, row, tab, optionCategory);
                return false;
            }

            static void DrawRow(Dialog_Options instance, Rect row, Tab tab,
                                OptionCategoryDef optionCategory)
            {
                Text.Font = GameFont.Small;

                CategoryRow(row, instance.selectedCategory == optionCategory);
                if (Widgets.ButtonInvisible(row))
                {
                    var target = Target(tab);
                    Select(instance, target != null ? target.Def : optionCategory);
                }

                float x = row.x + LabelX;
                if (tab != null && tab.Parent != null)
                {
                    x += ChildIndent;
                }
                else
                {
                    var icon = tab != null
                        ? tab.Icon?.Invoke()
                        : ContentFinder<Texture2D>.Get(optionCategory.texPath);
                    if (icon != null)
                        GUI.DrawTexture(
                            new Rect(row.x + RowPadX, row.y + (row.height - IconBox) / 2f,
                                IconBox, IconBox), icon);
                }

                UiText.RowLabel(new Rect(x, row.y, row.xMax - x, row.height),
                    optionCategory.LabelCap);
            }
        }

        // Vanilla's dispatch is a chain of comparisons against its own eight categories, so
        // ours would fall through it and draw nothing. Taken before the chain rather than
        // after: a page is two columns and its own scroll view, not rows on the
        // Listing_Standard vanilla opens here.
        [HarmonyPatch(typeof(Dialog_Options), "DoOptions")]
        public static class Patch_OptionsPage
        {
            static bool Prefix(OptionCategoryDef category, Rect inRect)
            {
                var tab = Target(TabOf(category));
                if (tab == null || !tab.HasPage) return true;

                tab.Draw(inRect);
                return false;
            }
        }

        // Dialog_Options() opens vanilla General by default. That category is stripped, so
        // the main menu road must not begin on a selected page with no row in the column.
        // Explicit constructors for our content view already name their destination.
        [HarmonyPatch(typeof(Dialog_Options), nameof(Dialog_Options.PostOpen))]
        public static class Patch_OptionsDefaultCategory
        {
            static void Postfix(Dialog_Options __instance)
            {
                if (__instance.selectedCategory == OptionCategoryDefOf.General)
                {
                    __instance.selectedCategory = CategoryFor(PageId.Config);
                    __instance.selectedMod = null;
                }
            }
        }


        // ---------------------------------------------------------------- the main menu
        // Main-menu options remain a real window; the content view applies the same layout itself.

        // Reserve chrome in-game; use the full screen on the main menu.
        static Rect Free()
        {
            bool playing = Current.ProgramState == ProgramState.Playing
                           && Find.CurrentMap != null;
            if (!playing) return new Rect(0f, 0f, UI.screenWidth, UI.screenHeight);
            return WorkspaceLayout.Current.Content;
        }

        [HarmonyPatch(typeof(Dialog_Options), nameof(Dialog_Options.InitialSize),
            MethodType.Getter)]
        public static class Patch_OptionsSize
        {
            static void Postfix(ref Vector2 __result)
            {
                var free = Free();
                __result = new Vector2(free.width, free.height);
            }
        }

        // Override vanilla's centred placement with the chrome-aligned rect.
        [HarmonyPatch(typeof(Window), "SetInitialSizeAndPosition")]
        public static class Patch_OptionsPlace
        {
            static void Postfix(Window __instance)
            {
                if (__instance is Dialog_Options) __instance.windowRect = Free();
            }
        }

        // Apply the band only to vanilla's window path; OptionsView opens its own group.
        [HarmonyPatch(typeof(Dialog_Options), nameof(Dialog_Options.DoWindowContents))]
        public static class Patch_OptionsBand
        {
            // Close only a group opened by this prefix.
            static bool _grouped;

            static void Prefix(ref Rect inRect)
            {
                if (OptionsView.Drawing) return;
                var band = OptionsView.Band(inRect);
                GUI.BeginGroup(band);
                _grouped = true;
                inRect = OptionsView.Inner(band);
            }

            static void Finalizer()
            {
                if (!_grouped) return;
                _grouped = false;
                GUI.EndGroup();
            }
        }

        // The pages the window built are dropped the way they always were; the view has its
        // own road to the same teardown.
        [HarmonyPatch(typeof(Dialog_Options), nameof(Dialog_Options.PreClose))]
        public static class Patch_OptionsClose
        {
            static void Postfix() => Teardown();
        }


        // -------------------------------------------------------- main menu

        // The version info corner is drawn by VersionControl on every menu frame.
        // It moved to the About tab, so the corner is blank.
        [HarmonyPatch(typeof(VersionControl), nameof(VersionControl.DrawInfoInCorner))]
        public static class Patch_VersionCorner
        {
            static bool Prefix() => false;
        }

        // Hide the main-menu web-link list; the About tab owns those links.
        [HarmonyPatch(typeof(OptionListingUtility), nameof(OptionListingUtility.DrawOptionListing))]
        public static class Patch_WebLinks
        {
            static bool Prefix(List<ListableOption> optList)
            {
                // Only suppress lists that are purely web links.
                if (optList.Count == 0) return true;
                bool allLinks = true;
                foreach (var o in optList)
                    if (!(o is ListableOption_WebLink)) { allLinks = false; break; }
                if (!allLinks) return true;

                // Don't suppress if we're in the options dialog (About page draws
                // links there).
                if (OptionsView.Anywhere) return true;

                // Suppress on the main menu.
                optList.Clear();
                return true;
            }
        }

        // Expansion icons at the bottom of the main menu are left alone: the About
        // tab no longer draws its own copy of them.
    }
}
