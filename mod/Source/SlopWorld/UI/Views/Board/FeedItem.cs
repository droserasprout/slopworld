using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    internal sealed class FeedItem
    {
        internal (string Body, string Sender, long Time, string Status) Message;
        internal Wire.BoardComment Entry;
        internal bool Activity;
        internal string TaskEntryKey;

        internal static IEnumerable<FeedItem> Build(
            Wire.BoardCard card,
            IEnumerable<TaskInfo> tasks,
            string filter
        )
        {
            return Items(card, tasks, filter).OrderByDescending(i => i.Message.Time);
        }

        static IEnumerable<FeedItem> Items(
            Wire.BoardCard card,
            IEnumerable<TaskInfo> tasks,
            string filter
        )
        {
            if (card != null && filter != "Status changes")
                foreach (var comment in card.Comments.Reverse())
                    yield return new FeedItem
                    {
                        Entry = comment,
                        Message = (comment.Text, comment.Author, comment.TimestampMs, ""),
                    };
            if (filter == "Comments")
                yield break;
            if (card != null)
                foreach (var move in Moves(card).Reverse())
                    yield return move;
            foreach (var task in tasks.Where(t => t != null))
            {
                string prefix = "Task " + task.Id;
                var previous = DelegatedTaskStatus.Queued;
                string lastNote = null;
                var updates = new List<FeedItem>();
                int index = 0;
                foreach (var update in task.History)
                {
                    int updateIndex = index++;
                    var note = update.Note?.Trim();
                    if (
                        update.Status == previous
                        && (string.IsNullOrEmpty(note) || note == lastNote)
                    )
                        continue;
                    updates.Add(
                        new FeedItem
                        {
                            TaskEntryKey = $"{task.Id}/update/{update.TimestampMs}/{updateIndex}",
                            Message = (
                                update.Note ?? "",
                                update.Sender,
                                update.TimestampMs,
                                prefix + " · " + TaskInfo.StatusText(update.Status)
                            ),
                        }
                    );
                    previous = update.Status;
                    lastNote = note;
                }
                for (int i = updates.Count - 1; i >= 0; i--)
                    yield return updates[i];
                if (
                    task.History.Count == 0
                    && (
                        task.Status != DelegatedTaskStatus.Queued
                        || !string.IsNullOrWhiteSpace(task.Note)
                    )
                )
                    yield return new FeedItem
                    {
                        TaskEntryKey = $"{task.Id}/status/{task.UpdatedMs}",
                        Message = (
                            task.Note ?? "",
                            task.To,
                            task.UpdatedMs,
                            prefix + " · " + TaskInfo.StatusText(task.Status)
                        ),
                    };
                if (filter == "All")
                    yield return new FeedItem
                    {
                        TaskEntryKey = task.Id + "/body",
                        Message = (task.Body ?? "", task.From, task.CreatedMs, prefix),
                    };
            }
        }

        static IEnumerable<FeedItem> Moves(Wire.BoardCard card)
        {
            // Older daemons stored a destination for both moves and same-column reorders.
            string previous = null;
            foreach (var entry in card.Activity)
            {
                string transition;
                if (entry.Text.StartsWith("Moved: ", StringComparison.Ordinal))
                    transition = entry.Text.Substring("Moved: ".Length);
                else if (entry.Text.StartsWith("Added to ", StringComparison.Ordinal))
                {
                    previous = entry.Text.Substring("Added to ".Length);
                    continue;
                }
                else if (entry.Text.StartsWith("Positioned in ", StringComparison.Ordinal))
                {
                    var destination = entry.Text.Substring("Positioned in ".Length);
                    var source = previous;
                    previous = destination;
                    if (source == null || source == destination)
                        continue;
                    transition = source + " → " + destination;
                }
                else
                    continue;
                yield return new FeedItem
                {
                    Entry = entry,
                    Activity = true,
                    Message = (transition, entry.Author, entry.TimestampMs, "Card moved"),
                };
            }
        }
    }
}
