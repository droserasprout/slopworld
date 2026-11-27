using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public sealed partial class BoardView
    {
        void DrawTasks(
            Listing_Standard l,
            Wire.ProjectBoard board,
            Wire.BoardCard card,
            bool editable
        )
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
                Save(CardEdit(board, card, "link", id, "task"));
            }
            if (!editable)
                return;
            if (UiControls.Button(l.GetRect(32), "Delegate to agent"))
                Find.WindowStack.Add(new DelegateTaskDialog(card.Assignee, card.Title, Attach));
            if (UiControls.Button(l.GetRect(32), "Spawn worker"))
            {
                void Spawn(string project) =>
                    Find.WindowStack.Add(
                        new SpawnWorkerDialog(TaskInfo.Host, project, card.Title, created: Attach)
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
    }
}
