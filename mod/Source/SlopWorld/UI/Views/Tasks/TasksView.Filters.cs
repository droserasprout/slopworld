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
            if (Rows.Update(allTasks, Filters, _filtersDirty))
                PruneSelection();
            _filtersDirty = false;
            return VisibleTasks;
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
                        selection.ToggleAll();
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

        public static void ResetFilters()
        {
            Filters.Clear();
            _filtersDirty = true;
        }

        static void FiltersChanged()
        {
            _filtersDirty = true;
            OpenFilterMenuAt(_filterAnchor);
        }

        static void ToggleAllTasks()
        {
            Filters.ToggleAll();
            FiltersChanged();
        }

        static Rect? _filterAnchor;

        // Keyboard commands open at the pointer; button and repeated choices retain their anchor.
        public static void OpenFilterMenu() => OpenFilterMenuAt(null);

        public static void OpenFilterMenu(Rect screenAnchor) => OpenFilterMenuAt(screenAnchor);

        static void OpenFilterMenuAt(Rect? screenAnchor)
        {
            _filterAnchor = screenAnchor;
            var options = new List<FloatMenuOption>
            {
                UiLayout.MenuToggle("All tasks", !Filtering, ToggleAllTasks),
                new UiSubmenu("Status", StatusOptions),
                new UiSubmenu("Direction", DirectionOptions),
                new UiSubmenu("Agent", AgentOptions),
            };
            WorkspaceWindow.OpenOverPane(
                screenAnchor.HasValue
                    ? UiSelector.Menu(options, screenAnchor.Value, matchWidth: false)
                    : new UiMenu(options)
            );
        }
    }
}
