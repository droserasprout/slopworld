using System.Globalization;

namespace SlopWorld
{
    internal static class BoardDiscussion
    {
        internal static Wire.BoardEdit Remove(
            string board,
            string card,
            Wire.BoardComment entry,
            bool activity
        ) =>
            new Wire.BoardEdit
            {
                Action = activity ? "remove-activity" : "remove-comment",
                Board = board,
                Card = card,
                Text = entry.Id.ToString(CultureInfo.InvariantCulture),
            };
    }
}
