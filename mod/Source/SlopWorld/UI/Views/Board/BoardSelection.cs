using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // Card identity owns the editor across polling and column moves. Untouched descriptions follow the shared snapshot.
    internal sealed class BoardSelection
    {
        internal Wire.BoardCard Card { get; private set; }
        internal string Comment = "",
            Title = "",
            Description = "",
            LinkKind = "worktree",
            LinkTarget = "";
        internal bool AddingLink;

        internal IEnumerable<TaskInfo> LinkedTasks(IEnumerable<TaskInfo> visibleTasks) =>
            Card
                .Links.Where(link => link.Kind == "task")
                .Select(link => visibleTasks.FirstOrDefault(task => task.Id == link.Target));

        internal bool SelectLinkedTaskCard(Wire.ProjectBoard board, string cardId, string taskId)
        {
            var card = board
                ?.Columns.SelectMany(c => c.Cards)
                .FirstOrDefault(c =>
                    c.Id == cardId
                    && c.Links.Any(link => link.Kind == "task" && link.Target == taskId)
                );
            Select(card);
            return card != null;
        }

        internal void Select(Wire.BoardCard card)
        {
            bool changed = card?.Id != Card?.Id;
            if (changed || Title == Card?.Title)
                Title = card?.Title ?? "";
            if (changed)
            {
                Comment = LinkTarget = "";
                AddingLink = false;
                LinkKind = "worktree";
                Description = card?.Description ?? "";
            }
            else if (Description == Card?.Description)
            {
                Description = card?.Description ?? "";
            }
            Card = card;
        }

        internal Wire.ProjectBoard Refresh(Wire.BoardCatalog catalog)
        {
            if (Card == null)
                return null;
            foreach (var board in catalog.Boards)
            {
                var live = board
                    .Columns.SelectMany(c => c.Cards)
                    .FirstOrDefault(c => c.Id == Card.Id);
                if (live == null)
                    continue;
                Select(live);
                return board;
            }
            Select(null);
            return null;
        }
    }
}
