using System.Linq;

namespace SlopWorld
{
    // Card identity owns the editor across polling and moves, including moves out
    // of the displayed board. Untouched descriptions follow the shared snapshot.
    internal sealed class BoardSelection
    {
        internal Wire.BoardCard Card { get; private set; }
        internal string Comment = "",
            Description = "",
            LinkKind = "worktree",
            LinkTarget = "";

        internal void Select(Wire.BoardCard card)
        {
            if (card?.Id != Card?.Id)
            {
                Comment = LinkTarget = "";
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
