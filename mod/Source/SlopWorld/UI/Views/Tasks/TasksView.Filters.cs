using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // TasksView filtering and filter menu construction.
    public static partial class TasksView
    {
        static bool Matches(TaskInfo task)
        {
            if (_status.HasValue && task.Status != _status.Value) return false;
            switch (_direction)
            {
                case DirectionFilter.Incoming:
                    if (!task.Incoming) return false;
                    break;
                case DirectionFilter.Outgoing:
                    if (!task.Outgoing) return false;
                    break;
                case DirectionFilter.AgentToAgent:
                    if (task.Incoming || task.Outgoing) return false;
                    break;
            }
            return string.IsNullOrEmpty(_agent) || task.From == _agent || task.To == _agent;
        }

        static List<TaskInfo> Filtered(List<TaskInfo> allTasks)
        {
            if (ReferenceEquals(_taskSnapshot, allTasks) && !_filtersDirty)
                return VisibleTasks;

            _taskSnapshot = allTasks;
            _filtersDirty = false;
            VisibleTasks.Clear();
            foreach (var task in allTasks)
                if (Matches(task)) VisibleTasks.Add(task);
            return VisibleTasks;
        }

        public static void FilterButton(Rect r)
        {
            TooltipHandler.TipRegion(r, Filtering
                ? "Task filters active. Click to change them."
                : "All tasks. Click to filter by status, direction or agent.");
            if (UiLayout.IconButton(r, Icons.Filter,
                    Filtering ? UiTheme.Lead : UiTheme.Off))
                OpenFilterMenu();
        }

        static List<FloatMenuOption> StatusOptions()
        {
            var options = new List<FloatMenuOption>
            {
                UiLayout.MenuToggle("All statuses", !_status.HasValue, () =>
                {
                    _status = null;
                    _filtersDirty = true;
                    OpenFilterMenu();
                }),
            };
            foreach (DelegatedTaskStatus status in Enum.GetValues(typeof(DelegatedTaskStatus)))
            {
                var chosen = status;
                options.Add(UiLayout.MenuToggle(TaskInfo.StatusText(chosen),
                    _status == chosen, () =>
                    {
                        _status = chosen;
                        _filtersDirty = true;
                        OpenFilterMenu();
                    }));
            }
            return options;
        }

        static List<FloatMenuOption> DirectionOptions()
        {
            return new List<FloatMenuOption>
            {
                UiLayout.MenuToggle("All directions", _direction == DirectionFilter.All,
                    () => SetDirection(DirectionFilter.All)),
                UiLayout.MenuToggle("Incoming to you", _direction == DirectionFilter.Incoming,
                    () => SetDirection(DirectionFilter.Incoming)),
                UiLayout.MenuToggle("Sent by you", _direction == DirectionFilter.Outgoing,
                    () => SetDirection(DirectionFilter.Outgoing)),
                UiLayout.MenuToggle("Agent to agent", _direction == DirectionFilter.AgentToAgent,
                    () => SetDirection(DirectionFilter.AgentToAgent)),
            };
        }

        static List<FloatMenuOption> AgentOptions()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var session in SessionHub.Instance.Sessions)
                if (session != null && !session.Host && !string.IsNullOrEmpty(session.Name))
                    names.Add(session.Name);
            foreach (var task in SessionHub.Instance.Tasks)
            {
                if (task.From != TaskInfo.Host) names.Add(task.From);
                if (task.To != TaskInfo.Host) names.Add(task.To);
            }

            var options = new List<FloatMenuOption>
            {
                UiLayout.MenuToggle("All agents", string.IsNullOrEmpty(_agent),
                    () => SetAgent("")),
            };
            foreach (var name in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                var chosen = name;
                options.Add(UiLayout.MenuToggle(chosen, _agent == chosen,
                    () => SetAgent(chosen)));
            }
            return options;
        }

        static void SetDirection(DirectionFilter direction)
        {
            _direction = direction;
            _filtersDirty = true;
            OpenFilterMenu();
        }

        static void SetAgent(string agent)
        {
            _agent = agent ?? "";
            _filtersDirty = true;
            OpenFilterMenu();
        }

        static void ClearFilters()
        {
            _status = null;
            _direction = DirectionFilter.All;
            _agent = "";
            _filtersDirty = true;
            OpenFilterMenu();
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
