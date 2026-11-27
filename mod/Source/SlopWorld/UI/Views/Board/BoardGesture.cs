using System.Linq;

namespace SlopWorld
{
    // Input is captured at press. The destination is a pair of stable IDs and
    // survives repaints; a removed insertion anchor cancels the entire gesture.
    internal sealed class BoardGesture
    {
        internal string Card,
            Column,
            Destination,
            Before;
        internal bool Held,
            Dragging,
            Panning;
        bool _touch;
        float _x,
            _y;

        internal void Press(string column, string card, bool touch, bool handle, float x, float y)
        {
            Cancel();
            Column = column;
            Card = card;
            Held = true;
            _touch = touch;
            Dragging = handle && touch;
            _x = x;
            _y = y;
        }

        internal void Motion(float x, float y)
        {
            if (!Held || Dragging || Panning)
                return;
            float dx = x - _x,
                dy = y - _y;
            if (dx * dx + dy * dy < 36f)
                return;
            if (_touch || Column == null)
                Panning = true;
            else
                Dragging = true;
        }

        internal void Target(string column, string before)
        {
            Destination = column;
            Before = before;
        }

        internal bool Valid(Wire.ProjectBoard board)
        {
            if (!Held)
                return true;
            if (board == null)
                return false;
            if (
                Column != null
                && (
                    Card == null
                        ? !board.Columns.Any(c => c.Id == Column)
                        : !board.Columns.Any(c => c.Cards.Any(k => k.Id == Card))
                )
            )
                return false;
            if (Destination == null)
                return true;
            var destination = board.Columns.FirstOrDefault(c => c.Id == Destination);
            return destination != null
                && (
                    Before == null
                    || (
                        Card == null
                            ? board.Columns.Any(c => c.Id == Before)
                            : destination.Cards.Any(c => c.Id == Before)
                    )
                );
        }

        internal Wire.BoardEdit Release(Wire.ProjectBoard board, bool inside)
        {
            Wire.BoardEdit edit = null;
            if (Dragging && inside && Destination != null && Valid(board) && !board.Archived)
                edit = new Wire.BoardEdit
                {
                    Action = Card == null ? "move-column" : "move-card",
                    Board = board.Id,
                    Column = Card == null ? Column : Destination,
                    Card = Card ?? "",
                    Before = Before ?? "",
                };
            Cancel();
            return edit;
        }

        internal void Cancel()
        {
            Held = Dragging = Panning = false;
            Column = Card = Destination = Before = null;
        }
    }
}
