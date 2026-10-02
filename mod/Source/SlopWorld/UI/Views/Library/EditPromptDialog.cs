using Verse;

namespace SlopWorld
{
    public sealed class EditPromptDialog : EditLibraryItemDialog
    {
        public EditPromptDialog(LibraryItemInfo existing) : this(existing, false) { }

        internal EditPromptDialog(LibraryItemInfo existing, bool copy)
            :
            base(existing, copy, LibraryItemKind.Prompt)
        { }

        protected override string TitleNoun => "prompt";
        protected override string TextHeading => "Prompt";

        protected override void DrawKindFields(Listing_Standard listing)
        {
            DrawRunLocation(listing);
            DrawProject(listing);
            DrawExecution(listing);
            DrawExplanation(listing, RunExplanation);
            DrawCommand(listing, "Command override (blank = template or host default)",
                _agentDefault, templatePlaceholder: true);
        }
    }
}
