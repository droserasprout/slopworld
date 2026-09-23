using Verse;

namespace SlopWorld
{
    public sealed class EditBreadcrumbDialog : EditLibraryItemDialog
    {
        public EditBreadcrumbDialog(LibraryItemInfo existing) : this(existing, false) { }

        internal EditBreadcrumbDialog(LibraryItemInfo existing, bool copy)
            :
            base(existing, copy, LibraryItemKind.Breadcrumb)
        { }

        protected override string TitleNoun => "breadcrumb";
        protected override string TextHeading => "Breadcrumb text";

        protected override void DrawKindFields(Listing_Standard listing)
        {
            DrawExplanation(listing,
                "Insert this text manually from a terminal context menu. You cannot run it directly.");
        }
    }
}
