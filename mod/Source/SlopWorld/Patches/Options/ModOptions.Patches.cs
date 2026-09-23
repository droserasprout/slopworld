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
        // Hide the translated OK button while the options content view is active.
        [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonText),
            new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool),
                    typeof(TextAnchor?) })]
        public static class Patch_OptionsOk
        {
            static bool Prefix(string label, ref bool __result)
            {
                // Check the active view before translating labels because this prefix receives every button.
                if (!OptionsView.Anywhere) return true;

                string ok = "OK".Translate();
                if (label != ok) return true;

                __result = false;
                return false;
            }
        }

        // Category column.

        // Omit icons for child rows. Indent their labels by ChildIndent.
        const float RowPadX = 10f;
        const float ChildIndent = 12f;
        const float IconGap = 10f;

        // Derive the base game row index from its rectangle position.
        // Rows use a 50-unit pitch and a 4-unit inset.
        const float VanillaPitch = 50f;
        const float VanillaInset = 4f;

        // Use one Small-font line per label to keep the settings column compact.
        // Use the same height for icons and labels.
        static float RowH => UiTheme.LineH;
        static float NestedRowH => UiTheme.LineH;
        static float Pitch => RowH + UiTheme.GapXS;
        static float NestedPitch => NestedRowH + UiTheme.GapXS;

        // Limit the icon box to the row height.
        static float IconBox => Mathf.Min(18f, RowH - 6f);
        static float LabelX => RowPadX + IconBox + IconGap;

        // Retain category column scroll state separately from page scroll state.
        // Clamp it to the current viewport and font dimensions.
        static float _railScroll;
        static int _railControl;
        static float _railGrab;

        // Derive the original row index from r.y, then calculate the compact row rectangle.
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

            var track = UiScrollbar.Track(viewport);
            var hit = UiScrollbar.Hit(viewport, track);
            float thumbH = UiScrollbar.ThumbHeight(track, viewport.height, max);
            var thumb = UiScrollbar.Thumb(track, thumbH, max <= 0f ? 0f : _railScroll / max);
            int id = GUIUtility.GetControlID(FocusType.Passive, hit);
            var e = Event.current;
            if (GUIUtility.hotControl == 0 && e.type == EventType.MouseDown && e.button == 0 &&
                hit.Contains(e.mousePosition))
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

            UiScrollbar.Draw(hit, track, thumb, GUIUtility.hotControl == id);
        }

        static void SetRailScroll(float mouseY, Rect track, float thumbH, float max)
        {
            float span = Mathf.Max(0f, track.height - thumbH);
            float t = span <= 0f ? 0f : Mathf.Clamp01(
                (mouseY - _railGrab - track.y) / span);
            _railScroll = t * max;
            GUIUtility.hotControl = _railControl;
        }

        // Use mod colors for selected rows and rows under the pointer.
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

        // Draw base game categories with the compact layout.
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

        // Draw mod pages before the base game category checks.
        // Each page controls its columns and scroll view instead of using the base game Listing_Standard.
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

        // Replace the default General selection with Config because General is hidden.
        // Content view constructors already specify their destination.
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


        // Options opened from the main menu use a window. The content view applies the same layout itself.

        // Reserve interface space during play. Use the full screen in the main menu.
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

        // Place the window within the available content rectangle.
        [HarmonyPatch(typeof(Window), "SetInitialSizeAndPosition")]
        public static class Patch_OptionsPlace
        {
            static void Postfix(Window __instance)
            {
                if (__instance is Dialog_Options) __instance.windowRect = Free();
            }
        }

        // Apply the content band only to the base game window. OptionsView opens its own GUI group.
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

        // Release page instances when the window closes. The content view calls the same cleanup separately.
        [HarmonyPatch(typeof(Dialog_Options), nameof(Dialog_Options.PreClose))]
        public static class Patch_OptionsClose
        {
            static void Postfix() => Teardown();
        }


        // Main menu.

        // Hide version information in the menu corner because the About tab provides it.
        [HarmonyPatch(typeof(VersionControl), nameof(VersionControl.DrawInfoInCorner))]
        public static class Patch_VersionCorner
        {
            static bool Prefix() => false;
        }

        // Hide main menu web links because the About tab provides them.
        [HarmonyPatch(typeof(OptionListingUtility), nameof(OptionListingUtility.DrawOptionListing))]
        public static class Patch_WebLinks
        {
            static bool Prefix(List<ListableOption> optList)
            {
                // Only suppress lists that contain web links exclusively.
                if (optList.Count == 0) return true;
                bool allLinks = true;
                foreach (var o in optList)
                    if (!(o is ListableOption_WebLink)) { allLinks = false; break; }
                if (!allLinks) return true;

                // Keep links in the options dialog for the About page.
                if (OptionsView.Anywhere) return true;

                // Clear the main menu link list.
                optList.Clear();
                return true;
            }
        }

        // Keep expansion icons in the main menu. The About tab does not duplicate them.
    }
}
