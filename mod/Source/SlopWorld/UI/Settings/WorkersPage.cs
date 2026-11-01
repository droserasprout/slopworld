using Verse;

namespace SlopWorld
{
    // The daemon owns the shared prompt delivered to new workers.
    public sealed class WorkersPage : DaemonConfigPage
    {
        readonly UiAreaResize _promptResize = new UiAreaResize(180f);
        protected override bool ShowEditButton => true;
        protected override string SavedMessage => "Worker settings saved.";

        protected override void DrawFields(Listing_Standard l)
        {
            UiLayout.Note(
                l,
                "Configure delegation permissions in each agent or template’s Workers tab."
            );
            UiLayout.SectionHeading(l, "Instructions");
            UiLayout.Note(
                l,
                "Prompt sent to worker. Task commands use the worker's SLOPWORLD_TASK_ID."
            );
            _cfg.WorkerPrompt = UiControls.Area(
                l,
                180f,
                "instructions.worker_prompt",
                _cfg.WorkerPrompt,
                on: true,
                defaultValue: _cfg.FactoryDefaults?.WorkerPrompt,
                resize: _promptResize
            );
            UiLayout.Note(l, "Task bodies are stored in the workers' mailboxes.");
        }
    }
}
