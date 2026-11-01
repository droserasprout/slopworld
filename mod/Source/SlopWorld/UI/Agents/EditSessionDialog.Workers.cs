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
                        "This worker has no saved parent identity. Spawn a new worker to edit its permissions."
                    );
                WorkerPermissionFields.Draw(
                    l,
                    "What "
                        + (string.IsNullOrEmpty(_s.Parent) ? "the parent" : _s.Parent)
                        + " can do",
                    _s.ParentAccess
                );
                UiLayout.Note(l, "Applies to this worker only.");
            }
            var policy = _s.Workers;
            policy.Enabled = UiControls.Checkbox(l, "Allow spawning workers", policy.Enabled);
            UiLayout.Note(l, "Applies to new workers. Edit existing workers separately.");
            UiLayout.SectionHeading(l, "Templates");
            var allowed = SessionHub.Instance.Templates.Select(t => t.Name).ToList();
            policy.AllTemplates = UiControls.Checkbox(
                l,
                "All templates",
                policy.AllTemplates,
                locked: !policy.Enabled
            );
            foreach (var template in SessionHub.Instance.Templates)
            {
                bool selected = policy.AllTemplates || policy.Templates.Contains(template.Name);
                bool next = UiControls.Checkbox(
                    l,
                    template.DisplayLabel,
                    selected,
                    locked: !policy.Enabled
                );
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
                UiLayout.Note(l, "No templates. Create one in Library.");
            WorkerPermissionFields.Draw(l, "Permissions", policy.Access, locked: !policy.Enabled);
            UiLayout.SectionHeading(l, "Advanced");
            policy.Recursive = UiControls.Checkbox(
                l,
                "Allow workers to spawn workers",
                policy.Recursive,
                locked: !policy.Enabled
            );
            UiLayout.Note(l, "Workers use the stricter limits of their parent and template.");
        }
    }
}
