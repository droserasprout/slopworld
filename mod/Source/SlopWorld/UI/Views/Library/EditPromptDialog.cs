using Verse;

namespace SlopWorld
{
    public sealed class EditPromptDialog : EditLibraryItemDialog
    {
        public EditPromptDialog(LibraryItemInfo existing)
            : this(existing, false) { }

        internal EditPromptDialog(LibraryItemInfo existing, bool copy)
            : base(existing, copy, LibraryItemKind.Prompt)
        {
            if (existing == null)
                _s.Link = LibraryItemLink.Ask;
        }

        protected override string TitleNoun => "prompt";
        protected override string TextHeading => "Prompt";

        protected override void DrawKindFields(Listing_Standard listing)
        {
            DrawExecution(listing);
            if (!_s.Runnable)
            {
                DrawExplanation(
                    listing,
                    "Reusable text for the terminal Prompts menu, agent startup and Timers. Choose Host or a template to also run it as an errand."
                );
                return;
            }
            DrawRunLocation(listing);
            DrawProject(listing);
            DrawExplanation(listing, RunExplanation);
            DrawCommand(
                listing,
                "Command override (blank = template or host default)",
                _agentDefault,
                templatePlaceholder: true
            );
        }
    }
}
