using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Worker bootstrap text is independent of the removed generated project manifest.
    public sealed class WorkersPage : DaemonConfigPage
    {
        protected override bool ShowEditButton => true;
        protected override string SavedMessage => "worker settings saved.";

        protected override void DrawFields(Listing_Standard l)
        {
            UiLayout.SectionHeading(l, "Worker bootstrap");
            UiLayout.Note(l, "This prompt is submitted to each worker spawned with slopctl spawn. " +
                "Use $SLOPWORLD_TASK_ID to refer to its exact mailbox task.");
            l.Label("Worker prompt");
            _cfg.WorkerPrompt = UiControls.Area(l, 180f, "instructions.worker_prompt",
                _cfg.WorkerPrompt, on: true, defaultValue: _cfg.FactoryDefaults?.WorkerPrompt);
            UiLayout.Note(l, "The task body stays in the mailbox; this prompt tells the worker " +
                "how to retrieve and finish it. Reset changes the form only; press Save to apply it.");

        }
    }
}
