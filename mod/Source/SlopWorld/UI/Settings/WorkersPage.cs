using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Worker bootstrap text is kept separate from the generated project instructions. The
    // prompt is an instruction for a task worker; its optional first-prompt breadcrumb is a
    // breadcrumb, so each control follows the corresponding experimental feature gate.
    public sealed class WorkersPage : DaemonConfigPage
    {
        protected override bool ShowEditButton => true;
        protected override string SavedMessage => "worker settings saved.";

        protected override void DrawFields(Listing_Standard l)
        {
            bool instructions = SessionHub.Instance.Config != null &&
                SessionHub.Instance.Config.ExperimentalInstructions;

            UiLayout.SectionHeading(l, "Worker bootstrap");
            if (!instructions)
            {
                UiLayout.Note(l, "Requires the live Instructions gate in Settings > General > Experimental.");
                if (UiLayout.Button(l, "Open General > Experimental", UiTheme.Btn.Ghost))
                    ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Config));
            }
            UiLayout.Note(l, "This prompt is submitted to each worker spawned with slopctl spawn. " +
                "Use $SLOPWORLD_TASK_ID to refer to its exact mailbox task.");
            l.Label("Worker prompt");
            _cfg.WorkerPrompt = UiControls.Area(l, 180f, "instructions.worker_prompt",
                _cfg.WorkerPrompt, on: instructions, defaultValue: DaemonConfig.DefaultWorkerPrompt);
            UiLayout.Note(l, "The task body stays in the mailbox; this prompt tells the worker " +
                "how to retrieve and finish it. Reset changes the form only; press Save to apply it.");

        }
    }
}
