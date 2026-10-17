using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public static partial class TasksView
    {
        static List<TaskInfo> Filtered(List<TaskInfo> allTasks)
        {
            if (ReferenceEquals(_taskSnapshot, allTasks) && !_filtersDirty)
                return VisibleTasks;

            // TaskStore publishes a replacement list for every mutation and refresh.
            _taskSnapshot = allTasks;
            _filtersDirty = false;
            VisibleTasks.Clear();
            foreach (var task in allTasks)
                if (Filters.Matches(task))
                    VisibleTasks.Add(task);
            return VisibleTasks;
        }

        public static void FilterButton(Rect r)
        {
            TooltipHandler.TipRegion(
                r,
                Filtering
                    ? "Task filters are active. Click to change the filters."
                    : "Showing all tasks. Click to filter by status, direction, or agent."
            );
            if (UiLayout.IconButton(r, Icons.Filter, Filtering ? UiTheme.Lead : UiTheme.Off))
                OpenFilterMenu();
        }

        static List<FloatMenuOption> StatusOptions() =>
            SelectionOptions(
                "All statuses",
                Filters.Status,
                (DelegatedTaskStatus[])Enum.GetValues(typeof(DelegatedTaskStatus)),
                TaskInfo.StatusText
            );

        static List<FloatMenuOption> DirectionOptions() =>
            SelectionOptions(
                "All directions",
                Filters.Direction,
                (TaskDirection[])Enum.GetValues(typeof(TaskDirection)),
                direction =>
                    direction == TaskDirection.Incoming ? "Incoming to you"
                    : direction == TaskDirection.Outgoing ? "Sent by you"
                    : "Between agents"
            );

        static List<string> AgentNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var session in SessionHub.Instance.Sessions)
                if (session != null && !session.Host && !string.IsNullOrEmpty(session.Name))
                    names.Add(session.Name);
            foreach (var task in SessionHub.Instance.Tasks)
            {
                if (task.From != TaskInfo.Host)
                    names.Add(task.From);
                if (task.To != TaskInfo.Host)
                    names.Add(task.To);
            }
            return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        static List<FloatMenuOption> AgentOptions() =>
            SelectionOptions("All agents", Filters.Agent, AgentNames(), name => name);

        static List<FloatMenuOption> SelectionOptions<T>(
            string allLabel,
            FilterSelection<T> selection,
            IEnumerable<T> values,
            Func<T, string> label
        )
        {
            var choices = values.ToList();
            var options = new List<FloatMenuOption>
            {
                UiLayout.MenuToggle(
                    allLabel,
                    selection.AllSelected,
                    () =>
                    {
                        selection.SelectAll();
                        FiltersChanged();
                    }
                ),
            };
            foreach (var value in choices)
            {
                var chosen = value;
                options.Add(
                    UiLayout.MenuToggle(
                        label(chosen),
                        selection.Contains(chosen),
                        () =>
                        {
                            selection.Toggle(chosen, choices);
                            FiltersChanged();
                        }
                    )
                );
            }
            return options;
        }

        static void FiltersChanged()
        {
            _filtersDirty = true;
            OpenFilterMenu();
        }

        static void ClearFilters()
        {
            Filters.Clear();
            FiltersChanged();
        }

        public static void OpenFilterMenu()
        {
            var options = new List<FloatMenuOption>
            {
                UiLayout.MenuToggle("All tasks", !Filtering, ClearFilters),
                new UiSubmenu("Status", StatusOptions),
                new UiSubmenu("Direction", DirectionOptions),
                new UiSubmenu("Agent", AgentOptions),
            };
            TerminalWindow.OpenOverPane(new UiMenu(options));
        }
    }
}
