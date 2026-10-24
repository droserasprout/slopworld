using System.Collections.Generic;

namespace SlopWorld
{
    // Retain filtered membership and counts between TaskStore snapshot replacements.
    // Selection belongs to TasksView and is pruned against all tasks, never the filter.
    sealed class TaskBoardRows
    {
        List<TaskInfo> _snapshot;
        internal readonly List<TaskInfo> Visible = new List<TaskInfo>();
        internal readonly HashSet<string> LiveIds = new HashSet<string>();
        internal int Incoming { get; private set; }
        internal int Outgoing { get; private set; }
        internal int Peer { get; private set; }

        // TaskStore publishes a replacement list for every mutation and refresh.
        // Return whether selection needs reconciling, including an empty replacement.
        internal bool Update(List<TaskInfo> tasks, TaskFilters filters, bool filtersDirty)
        {
            bool replaced = !ReferenceEquals(_snapshot, tasks);
            if (!replaced && !filtersDirty)
                return false;
            if (replaced)
            {
                LiveIds.Clear();
                foreach (var task in tasks)
                    if (task != null)
                        LiveIds.Add(task.Id);
            }
            Visible.Clear();
            Incoming = Outgoing = Peer = 0;
            foreach (var task in tasks)
            {
                if (task == null || !filters.Matches(task))
                    continue;
                Visible.Add(task);
                if (task.Terminal)
                    continue;
                if (task.Incoming)
                    Incoming++;
                if (task.Outgoing)
                    Outgoing++;
                if (!task.Incoming && !task.Outgoing)
                    Peer++;
            }
            _snapshot = tasks;
            return replaced;
        }
    }
}
