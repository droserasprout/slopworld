using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SlopWorld
{
    internal static class BoardDiscussion
    {
        // Groups are newest first. The range indexes the original entries so removal
        // always targets one immutable sequence number, even after regrouping.
        internal static IEnumerable<(int Newest, int Oldest)> ActivityGroups(Wire.BoardCard card)
        {
            for (int end = card.Activity.Count - 1; end >= 0; )
            {
                int start = end;
                var entry = card.Activity[end];
                while (
                    start > 0
                    && card.Activity[start - 1].Author == entry.Author
                    && card.Activity[start - 1].Text == entry.Text
                )
                    start--;
                yield return (end, start);
                end = start - 1;
            }
        }

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
