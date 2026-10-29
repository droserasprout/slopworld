using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public partial class EditSessionDialog
    {
        void DrawWorkers(Listing_Standard l)
        {
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && (!_s.Worker || _s.ParentBound);
            try
            {
                DrawWorkerFields(l);
            }
            finally
            {
                GUI.enabled = enabled;
            }
        }

        void DrawWorkerFields(Listing_Standard l)
        {
            if (_s.Worker)
            {
                if (!_s.ParentBound)
                    UiLayout.Note(
                        l,
                        "This legacy worker has no captured parent identity. Its settings are shown for inspection; spawn a new worker to establish editable relationship access."
                    );
                WorkerPermissionFields.Draw(
                    l,
                    "What "
                        + (string.IsNullOrEmpty(_s.Parent) ? "the parent" : _s.Parent)
                        + " can do",
                    _s.ParentAccess
                );
                UiLayout.Note(
                    l,
                    "Save changes access for this existing relationship only. It does not change the parent's policy for new workers."
                );
            }
            var policy = _s.Workers;
            policy.Enabled = UiControls.Checkbox(
                l,
                "Allow this agent to spawn workers",
                policy.Enabled
            );
            UiLayout.Note(
                l,
                "These settings apply to newly created workers only. Edit an existing worker's Workers tab to change its relationship access."
            );
            var allowed = SessionHub.Instance.Config.WorkerTemplates;
            policy.AllTemplates = UiControls.Checkbox(
                l,
                "Use all templates allowed by daemon policy",
                policy.AllTemplates
            );
            foreach (
                var template in SessionHub.Instance.Templates.Where(t => allowed.Contains(t.Name))
            )
            {
                bool selected = policy.AllTemplates || policy.Templates.Contains(template.Name);
                bool next = UiControls.Checkbox(l, template.DisplayLabel, selected);
                if (next == selected)
                    continue;
                if (policy.AllTemplates)
                {
                    policy.AllTemplates = false;
                    policy.Templates.Clear();
                    policy.Templates.Add(allowed);
                }
                if (next)
                    policy.Templates.Add(template.Name);
                else
                    policy.Templates.Remove(template.Name);
            }
            if (allowed.Count == 0)
                UiLayout.Note(l, "Daemon policy currently allows no worker templates.");
            WorkerPermissionFields.Draw(
                l,
                "Access to new workers",
                policy.Access,
                SessionHub.Instance.Config.WorkerAccess
            );
            UiLayout.SectionHeading(l, "Advanced");
            if (!SessionHub.Instance.Config.WorkerRecursive)
                UiLayout.Note(l, "Daemon policy currently disables further worker delegation.");
            policy.Recursive = UiControls.Checkbox(
                l,
                "Allow workers to delegate further",
                policy.Recursive
            );
            UiLayout.Note(
                l,
                "Worker templates may narrow delegation further. Daemon policy always limits available templates."
            );
        }
    }
}
