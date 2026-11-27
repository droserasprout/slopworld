using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SlopWorld
{
    internal static class BoardDiscussion
    {
        internal static IEnumerable<(Wire.BoardComment Entry, bool Activity)> Timeline(
            Wire.BoardCard card,
            bool showActivity
        )
        {
            var entries = card.Comments.Select(c => (Entry: c, Activity: false));
            if (showActivity)
                entries = entries.Concat(card.Activity.Select(a => (a, true)));
            return entries
                .OrderByDescending(e => e.Entry.TimestampMs)
                .ThenByDescending(e => e.Entry.Id);
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
