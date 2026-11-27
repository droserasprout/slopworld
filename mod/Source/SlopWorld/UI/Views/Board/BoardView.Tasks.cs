using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public sealed partial class BoardView
    {
        void DrawTasks(Rect panel, Wire.ProjectBoard board, Wire.BoardCard card, bool editable)
        {
            _details.Draw(
                panel,
                l =>
                {
                    void Attach(string id)
                    {
                        if (!Active || _saving)
                        {
                            UiLayout.Fail(
                                $"Attach task {id} to {card.Key} with slopctl board link {board.Id} {card.Key} task {id}"
                            );
                            return;
                        }
                        _editor.TaskTarget = id;
                        Save(
                            CardEdit(board, card, "link", id, "task"),
                            () =>
                            {
                                if (_editor.Card?.Id == card.Id && _editor.TaskTarget == id)
                                    _editor.TaskTarget = "";
                            }
                        );
                    }
                    foreach (var task in _editor.LinkedTasks(SessionHub.Instance.Tasks))
                    {
                        if (task == null)
                            l.Label("Unavailable task");
                        else if (
                            UiControls.Button(
                                l.GetRect(32),
                                task.Summary
                                    + " · "
                                    + task.To
                                    + " · "
                                    + TaskInfo.StatusText(task.Status)
                            )
                        )
                            TaskDetailView.Open(task, this);
                    }
                    if (!editable)
                        return;
                    _editor.TaskTarget = UiText.FieldWithPlaceholder(
                        TextBox(l.GetRect(30)),
                        "board.task." + card.Id,
                        _editor.TaskTarget,
                        "Task ID"
                    );
                    if (
                        UiControls.Button(
                            l.GetRect(32),
                            "Attach task",
                            on: _editor.TaskTarget.Trim().Length > 0
                        )
                    )
                        Attach(_editor.TaskTarget.Trim());
                    if (UiControls.Button(l.GetRect(32), "Delegate to agent"))
                        Find.WindowStack.Add(
                            new DelegateTaskDialog(card.Assignee, card.Title, Attach)
                        );
                    if (UiControls.Button(l.GetRect(32), "Spawn worker"))
                    {
                        void Spawn(string project) =>
                            Find.WindowStack.Add(
                                new SpawnWorkerDialog(
                                    TaskInfo.Host,
                                    project,
                                    card.Title,
                                    created: Attach
                                )
                            );
                        if (board.Id == "root")
                            UiMenu.Open(
                                SessionHub
                                    .Instance.Projects.Select(p => new FloatMenuOption(
                                        p.Name,
                                        () => Spawn(p.Name)
                                    ))
                                    .ToList()
                            );
                        else
                            Spawn(board.Name);
                    }
                }
            );
        }
    }
}
