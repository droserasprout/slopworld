using System;

namespace SlopWorld
{
    enum TaskDirection
    {
        Incoming,
        Outgoing,
        AgentToAgent,
    }

    // Task board selection and matching; TasksView owns menus and snapshot invalidation.
    sealed class TaskFilters
    {
        public readonly FilterSelection<DelegatedTaskStatus> Status =
            new FilterSelection<DelegatedTaskStatus>();
        public readonly FilterSelection<TaskDirection> Direction =
            new FilterSelection<TaskDirection>();
        public readonly FilterSelection<string> Agent = new FilterSelection<string>(
            StringComparer.Ordinal
        );

        public bool Filtering =>
            !Status.AllSelected || !Direction.AllSelected || !Agent.AllSelected;

        public bool Matches(TaskInfo task)
        {
            if (!Status.Contains(task.Status))
                return false;
            bool direction =
                (task.Incoming && Direction.Contains(TaskDirection.Incoming))
                || (task.Outgoing && Direction.Contains(TaskDirection.Outgoing))
                || (
                    !task.Incoming
                    && !task.Outgoing
                    && Direction.Contains(TaskDirection.AgentToAgent)
                );
            return direction
                && (
                    Agent.AllSelected
                    || (task.From != TaskInfo.Host && Agent.Contains(task.From))
                    || (task.To != TaskInfo.Host && Agent.Contains(task.To))
                );
        }

        public void Clear()
        {
            Status.SelectAll();
            Direction.SelectAll();
            Agent.SelectAll();
        }

        public void ToggleAll()
        {
            if (Filtering)
                Clear();
            else
            {
                Status.DeselectAll();
                Direction.DeselectAll();
                Agent.DeselectAll();
            }
        }
    }
}
