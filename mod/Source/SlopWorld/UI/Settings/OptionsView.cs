using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Render Dialog_Options as content inside the chrome. A stacked window would block sidebar
    // input. Preserve vanilla's GUI-coordinate category layout inside a temporary group.
    public class OptionsView : ContentView
    {
        // What vanilla reserves at the foot of the page for the OK button. Handed back to
        // the options list, the button being gone (Patch_OptionsOk).
        const float OkRow = 60f;

        readonly Dialog_Options _dlg;

        // True for the length of this view's own call into the dialog.
        public static bool Drawing { get; private set; }

        // Dialog_Options has no retained scroll position for its category column. The patch
        // around DoCategoryRow uses this local viewport while the content view is drawing.
        // the main-menu window keeps the game's own category layout.
        static Rect _railViewport;
        public static Rect RailViewport => _railViewport;

        // Shared options context for vanilla patches: true while this view draws or a Dialog_Options is the active window.
        public static bool Anywhere =>
            Drawing || Find.WindowStack?.currentlyDrawnWindow is Dialog_Options;

        public OptionsView(OptionCategoryDef category = null)
        {
            var c = category ?? ModOptions.CategoryFor(ModOptions.PageId.Config);
            _dlg = c != null ? new Dialog_Options(c) : new Dialog_Options();
        }

        public override string Title => "Settings";

        // Navigate in place; release field focus on category changes and clear selected-mod state.
        public OptionCategoryDef Category
        {
            get { return _dlg.selectedCategory; }
            set
            {
                if (_dlg.selectedCategory != value) TextFieldSelection.ReleaseFocus();
                _dlg.selectedCategory = value;
                _dlg.selectedMod = null;
            }
        }

        // The height is the band's plus the row vanilla takes off for the OK button. Therefore, the
        // options list fills the band and the button - suppressed, see Patch_OptionsOk - is laid
        // out past the bottom of the group.
        public static Rect Inner(Rect band) =>
            new Rect(0f, 0f, band.width, band.height + OkRow);

        public override void Draw(Rect body)
        {
            var band = UiLayout.CenteredBand(body);
            // Other views own wheel input outside Settings or already consumed.
            if (SmoothScroll.WheelOnly && (Event.current.type == EventType.Used ||
                !band.Contains(Event.current.mousePosition))) return;

            GUI.BeginGroup(band);
            Drawing = true;
            _railViewport = new Rect(0f, 0f, Mathf.Min(177f, band.width), band.height);
            try { _dlg.DoWindowContents(Inner(band)); }
            finally
            {
                Drawing = false;
                GUI.EndGroup();
            }
        }

        public override void Opened() { }

        // On close, remember the selected tab, tear down pages so the next open rereads config.toml, and persist mod settings once.
        public override void Closed()
        {
            ModOptions.Remember(Category);
            ModOptions.Teardown();
        }
    }
}
