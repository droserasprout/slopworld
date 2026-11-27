using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    internal sealed class FeedItem
    {
        internal (string Body, string Sender, long Time, string Status) Message;
        internal Wire.BoardComment Entry;
        internal bool Activity;

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
            if (card != null && filter != "Tasks")
                foreach (
                    var (entry, activity) in BoardDiscussion.Timeline(card, filter != "Comments")
                )
                    if (filter != "Activity" || activity)
                        yield return new FeedItem
                        {
                            Entry = entry,
                            Activity = activity,
                            Message = (
                                entry.Text,
                                entry.Author,
                                entry.TimestampMs,
                                activity ? "Activity" : "Comment"
                            ),
                        };
            if (filter != "All" && filter != "Tasks")
                yield break;
            foreach (var task in tasks.Where(t => t != null))
            {
                string prefix = "Task " + task.Id;
                foreach (var update in task.History.AsEnumerable().Reverse())
                    yield return new FeedItem
                    {
                        Message = (
                            update.Note ?? "",
                            update.Sender,
                            update.TimestampMs,
                            prefix + " · " + TaskInfo.StatusText(update.Status)
                        ),
                    };
                if (task.History.Count == 0 && !string.IsNullOrEmpty(task.Note))
                    yield return new FeedItem
                    {
                        Message = (
                            task.Note,
                            task.To,
                            task.UpdatedMs,
                            prefix + " · " + TaskInfo.StatusText(task.Status)
                        ),
                    };
                yield return new FeedItem
                {
                    Message = (task.Body ?? "", task.From, task.CreatedMs, prefix),
                };
            }
        }
    }
}
