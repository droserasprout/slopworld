using Verse;

namespace SlopWorld
{
    public sealed class EditShellDialog : EditLibraryItemDialog
    {
        public EditShellDialog(LibraryItemInfo existing) : this(existing, false) { }

        internal EditShellDialog(LibraryItemInfo existing, bool copy)
            :
            base(existing, copy, LibraryItemKind.Shell)
        { }

        protected override string TitleNoun => "shell entry";
        protected override string TextHeading => "Command line";

        protected override void DrawKindFields(Listing_Standard listing)
        {
            DrawRunLocation(listing);
            DrawProject(listing);
            DrawExecution(listing);
            DrawExplanation(listing, RunExplanation);
            DrawCommand(listing, "Shell (blank = the default)", _shellDefault);
        }
    }
}
