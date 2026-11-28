using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SlopWorld
{
    public partial class EditSessionDialog
    {
        Wire.BoardCatalog _permissionBoards;
        bool _requestedBoards;

        void DrawWorkers(Listing_Standard l)
        {
            if (!_requestedBoards)
            {
                _requestedBoards = true;
                DaemonClient.Get<Wire.BoardCatalog>(
                    WireProtocol.Routes.Boards,
                    catalog => _permissionBoards = catalog,
                    error => UiLayout.Fail(error)
                );
            }
            WorkerPermissionFields.Draw(l, _s.Permissions, _permissionBoards);
            UiLayout.Note(
                l,
                EditingTemplate
                    ? "New agents copy these permissions."
                    : "Changes apply when saved, including to existing workers. No restart required."
            );
            UiLayout.SectionHeading(l, "Worker spawning");
            var policy = _s.Workers;
            policy.Enabled = UiControls.Checkbox(l, "Allow spawning workers", policy.Enabled);
            UiLayout.Note(l, "Applies to new workers. Edit existing workers separately.");
            var allowed = SessionHub.Instance.Templates.Select(t => t.Name).ToList();
            var choices = new List<(string Label, Func<bool> Selected, Action Toggle)>
            {
                (
                    "All templates",
                    () => policy.AllTemplates,
                    () => policy.AllTemplates = !policy.AllTemplates
                ),
            };
            foreach (var template in SessionHub.Instance.Templates)
            {
                choices.Add(
                    (
                        template.DisplayLabel,
                        () => policy.AllTemplates || policy.Templates.Contains(template.Name),
                        () =>
                        {
                            if (policy.AllTemplates)
                            {
                                policy.AllTemplates = false;
                                policy.Templates.Clear();
                                policy.Templates.Add(allowed);
                            }
                            if (!policy.Templates.Remove(template.Name))
                                policy.Templates.Add(template.Name);
                        }
                    )
                );
            }
            UiSelector.MultiSelectRow(
                l,
                "Available templates",
                choices,
                policy.Enabled,
                policy.AllTemplates ? "All templates" : null
            );
            if (allowed.Count == 0)
                UiLayout.Note(l, "No templates. Create one in Library.");
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
