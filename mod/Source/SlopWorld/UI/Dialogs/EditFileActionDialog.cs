using Verse;

namespace SlopWorld
{
    public sealed class EditFileActionDialog : EditLibraryItemDialog
    {
        public EditFileActionDialog(LibraryItemInfo existing) : this(existing, false) { }

        internal EditFileActionDialog(LibraryItemInfo existing, bool copy)
            :
            base(existing, copy, LibraryItemKind.FileAction)
        { }

        protected override string TitleNoun => "file action";
        protected override string TextHeading => "Command line";

        protected override void DrawKindFields(Listing_Standard listing)
        {
            DrawFileActionMode(listing);
            DrawExplanation(listing,
                "This command is offered by the Files sidebar; use {{ absolute_path }} or {{ relative_path }}.");
            DrawCommand(listing, "Command (path is appended unless substituted)", _agentDefault);
        }
    }
}
