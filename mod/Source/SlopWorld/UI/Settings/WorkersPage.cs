using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The daemon owns the worker prompt and template allowlist.
    public sealed class WorkersPage : DaemonConfigPage
    {
        readonly UiAreaResize _promptResize = new UiAreaResize(180f);
        protected override bool ShowEditButton => true;
        protected override string SavedMessage => "Worker settings saved.";

        protected override void AfterLoad()
        {
            // Reload templates when this page opens or reloads.
            // New templates start outside the worker allowlist.
            SessionHub.Instance.Catalog.RefreshTemplates();
        }

        protected override void DrawFields(Listing_Standard l)
        {
            UiLayout.SectionHeading(l, "Templates");
            UiLayout.Note(
                l,
                "Selected templates are available to agents that create task workers."
            );

            var templates = SessionHub
                .Instance.Templates.Where(template => template != null)
                .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (templates.Count == 0)
            {
                UiLayout.Note(l, "No agent templates are available. Create one in Library.");
            }
            foreach (var template in templates)
            {
                bool selected = _cfg.WorkerTemplates.Contains(template.Name);
                string tip = "";
                bool next = UiControls.Checkbox(l, template.DisplayLabel, selected, tip);
                if (next == selected)
                    continue;
                if (next)
                    _cfg.WorkerTemplates.Add(template.Name);
                else
                    _cfg.WorkerTemplates.RemoveAll(name => name == template.Name);
            }

            l.Gap(UiTheme.GapL);
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
