using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Render Dialog_Options as content inside the chrome; a stacked window would block sidebar
    // input. Preserve vanilla's GUI-coordinate category layout inside a temporary group.
    public class OptionsView : IContentView
    {
        // As wide as the config page needs and no wider: 177 for the category column, the
        // rest for two columns of fields. A form stretched across a 4K screen is a form
        // nobody can read a row of.
        const float MaxW = 1020f;
        // Room above the first category row. There is none below: the band runs to the
        // bottom and the hidden OK button's row is what reads as padding.
        const float PadY = 24f;
        // What vanilla reserves at the foot of the page for the OK button. Handed back to
        // the options list, the button being gone (Patch_OptionsOk).
        const float OkRow = 60f;

        readonly Dialog_Options _dlg;

        // True for the length of this view's own call into the dialog.
        public static bool Drawing { get; private set; }

        // What everything that used to ask `currentlyDrawnWindow is Dialog_Options` asks now
        // - the OK suppression, the three vanilla rows StripOptions drops, the web links.
        // That question stopped answering the moment the pages were drawn by the chrome's
        // window instead of by the dialog's own, and the main menu still opens the real
        // window, so both roads are asked.
        public static bool Anywhere =>
            Drawing || Find.WindowStack?.currentlyDrawnWindow is Dialog_Options;

        public OptionsView(OptionCategoryDef category = null)
        {
            var c = category ?? SlopOptions.Category;
            _dlg = c != null ? new Dialog_Options(c) : new Dialog_Options();
        }

        public string Title => "Options";

        // The tab the column is on, so the doors that used to swap a category on an open
        // dialog still have something to swap it on.
        public OptionCategoryDef Category
        {
            get { return _dlg.selectedCategory; }
            set { _dlg.selectedCategory = value; _dlg.selectedMod = null; }
        }

        // The centred band inside whatever room the pages are given, and the rect handed to
        // the dialog once a group is open on it. Both are shared with the patch that shapes
        // the window the *main menu* still opens (Patch_OptionsBand): one band, two roads.
        public static Rect Band(Rect r)
        {
            float w = Mathf.Min(r.width, MaxW);
            return new Rect(r.x + (r.width - w) / 2f, r.y + PadY, w, r.height - PadY);
        }

        // The height is the band's plus the row vanilla takes off for the OK button, so the
        // options list fills the band and the button - suppressed, see Patch_OptionsOk - is
        // laid out past the bottom of the group.
        public static Rect Inner(Rect band) =>
            new Rect(0f, 0f, band.width, band.height + OkRow);

        public void Draw(Rect body)
        {
            var band = Band(body);

            GUI.BeginGroup(band);
            Drawing = true;
            try { _dlg.DoWindowContents(Inner(band)); }
            finally
            {
                Drawing = false;
                GUI.EndGroup();
            }
        }

        public void Opened() { }

        // What the dialog's PreClose did when it was a window: drop the pages so the next
        // open re-reads config.toml, and write the settings file once, the way the terminal
        // settings window it replaced did on close. The tab is handed over first - the view
        // goes with the dialog that holds it, so this is the last moment anything knows
        // which page was being read.
        public void Closed()
        {
            SlopOptions.Remember(Category);
            SlopOptions.Teardown();
        }
    }
}
