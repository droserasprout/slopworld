using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public sealed partial class PlannerView
    {
        void DrawLinks(Listing_Standard listing)
        {
            UiLayout.SectionHeading(listing, "LINKS");
            var item = Linked(_selected);
            bool editable = Ready && !Dirty && item != null && item.Id.Length > 0;
            if (item != null)
                foreach (var link in item.Links.ToList())
                {
                    var worker =
                        link.Kind == "worker"
                            ? LinkedWorker(SessionHub.Instance.Sessions, link.Id)
                            : null;
                    string label =
                        link.Kind == "task"
                            ? "Task " + link.Id + " · " + link.Status
                            : "Worker "
                                + (worker?.Name ?? link.Label)
                                + (worker == null ? " · unavailable" : "");
                    var row = listing.GetRect(UiTheme.FieldH);
                    var remove = new Rect(row.xMax - 65f, row.y, 65f, row.height);
                    if (
                        UiControls.Button(
                            new Rect(row.x, row.y, row.width - 65f - UiTheme.GapS, row.height),
                            label,
                            UiControls.Btn.Ghost,
                            on: Ready && (link.Kind == "task" || worker != null)
                        )
                    )
                    {
                        if (link.Kind == "task")
                            OpenTask(link.Id);
                        else
                            Sidebar.FocusAgent(worker.Name);
                    }
                    if (UiControls.Button(remove, "Remove", on: editable))
                        RemoveLink(link);
                }
            if (item == null || item.Links.Count == 0)
                UiLayout.Note(listing, "No linked Tasks or Workers.");
            UiLayout.Note(
                listing,
                "Removing a link keeps the Task and Worker. Their status does not change this task's completion."
            );
            if (UiControls.Button(listing.GetRect(UiTheme.FieldH), "Link Task…", on: editable))
                WorkspaceWindow.OpenOverPane(
                    new UiMenu(
                        SessionHub
                            .Instance.Tasks.Select(task => new FloatMenuOption(
                                task.Id + " · " + task.Summary,
                                EditSelected(() => AddLink("task", task.Id, task.Id))
                            ))
                            .ToList()
                    )
                );
            if (UiControls.Button(listing.GetRect(UiTheme.FieldH), "Link Worker…", on: editable))
                WorkspaceWindow.OpenOverPane(
                    new UiMenu(
                        SessionHub
                            .Instance.Sessions.Where(s => s.Worker && s.StateId.Length > 0)
                            .Select(worker => new FloatMenuOption(
                                worker.Name,
                                EditSelected(() => AddLink("worker", worker.StateId, worker.Name))
                            ))
                            .ToList()
                    )
                );
            if (
                UiControls.Button(
                    listing.GetRect(UiTheme.FieldH),
                    "Spawn worker…",
                    UiControls.Btn.Primary,
                    on: editable
                        && !(_selected.Completed)
                        && SelectedDescription?.Revision != null
                        && !(SelectedDescription?.Dirty ?? false)
                        && !(SelectedDescription?.Busy ?? false)
                )
            )
            {
                SessionHub.Instance.Catalog.RefreshTemplates();
                WorkspaceWindow.OpenOverPane(
                    new UiMenu(
                        SessionHub
                            .Instance.Projects.Select(project => new FloatMenuOption(
                                project.Name,
                                EditSelected(() =>
                                    WorkspaceWindow.OpenOverPane(
                                        new SpawnWorkerDialog(
                                            TaskInfo.Host,
                                            project.Name,
                                            _selected.Title
                                                + "\n\n"
                                                + (SelectedDescription?.Text ?? ""),
                                            _selected.Id,
                                            WorkerSpawnSettled
                                        )
                                    )
                                )
                            ))
                            .ToList()
                    )
                );
            }
            if (Dirty || string.IsNullOrEmpty(_selected.Id))
                UiLayout.Note(
                    listing,
                    "Save task changes before editing links or spawning a worker."
                );
            if (SelectedDescription?.Dirty == true)
                UiLayout.Note(listing, "Save the description before spawning a worker.");
        }
    }
}
