using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Worker bootstrap text is independent of the removed generated project manifest.
    public sealed class WorkersPage : DaemonConfigPage
    {
        protected override bool ShowEditButton => true;
        protected override string SavedMessage => "worker settings saved.";

        public WorkersPage()
        {
            SessionHub.Instance.Catalog.RefreshTemplates();
        }

        protected override void AfterLoad()
        {
            // Refresh the user-level catalog when the page is opened/reloaded. New definitions
            // intentionally start unchecked in daemon policy.
            SessionHub.Instance.Catalog.RefreshTemplates();
        }

        protected override void DrawFields(Listing_Standard l)
        {
            UiLayout.SectionHeading(l, "Available worker templates");
            UiLayout.Note(l, "Checked templates may be used by agents and slopctl to create task " +
                "workers.");

            var templates = SessionHub.Instance.Templates
                .Where(template => template != null)
                .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (templates.Count == 0)
            {
                UiLayout.Note(l, "No agent templates are available. Create one in Library.");
            }
            foreach (var template in templates)
            {
                bool selected = _cfg.WorkerTemplates.Contains(template.Name);
                string tip = "Personal template. Existing workers keep their captured settings.";
                bool next = UiControls.Checkbox(l, template.DisplayLabel, selected, tip);
                if (next == selected) continue;
                if (next)
                    _cfg.WorkerTemplates.Add(template.Name);
                else
                    _cfg.WorkerTemplates.RemoveAll(name => name == template.Name);
            }

            l.Gap(UiTheme.GapL);
            UiLayout.SectionHeading(l, "Worker bootstrap");
            UiLayout.Note(l, "This prompt is submitted to each worker spawned from a selected " +
                "template. " +
                "Use $SLOPWORLD_TASK_ID to refer to its exact mailbox task.");
            l.Label("Worker prompt");
            _cfg.WorkerPrompt = UiControls.Area(l, 180f, "instructions.worker_prompt",
                _cfg.WorkerPrompt, on: true, defaultValue: _cfg.FactoryDefaults?.WorkerPrompt);
            UiLayout.Note(l, "The task body stays in the mailbox; this prompt tells the worker " +
                "how to retrieve and finish it. Reset changes the form only; press Save to apply it.");

        }
    }
}
