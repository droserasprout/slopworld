using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public static partial class TasksView
    {
        public static void Clicks()
        {
            if (!ColonistBarStrip.Interactive) return;

            var e = Event.current;
            if (!ColonistBarStrip.MouseOver(_body)) return;
            if (e.rawType != EventType.MouseDown || (e.button != 0 && e.button != 1)) return;

            foreach (var line in Lines)
            {
                if (!ColonistBarStrip.MouseOver(line.Rect)) continue;
                e.Use();
                if (e.button == 1)
                {
                    if (!IsSelected(line.Task)) SelectOnly(line.Task);
                    SetAnchor(line.Task);
                    TaskActions.OpenMenu(line.Task);
                }
                else if (e.shift)
                {
                    SelectRange(line.Task, e.control);
                }
                else if (e.control)
                {
                    Toggle(line.Task);
                    SetAnchor(line.Task);
                }
                else
                {
                    SelectOnly(line.Task);
                    SetAnchor(line.Task);
                    TaskDetailView.Open(line.Task);
                }
                return;
            }
        }

        static bool IsSelected(TaskInfo task) => task != null &&
            !string.IsNullOrEmpty(task.Id) && SelectedIds.Contains(task.Id);

        internal static List<TaskInfo> SelectionFor(TaskInfo fallback)
        {
            var selected = SessionHub.Instance.Tasks.Where(IsSelected).ToList();
            if (fallback != null && selected.Any(task => task.Id == fallback.Id))
                return selected;
            return fallback == null ? new List<TaskInfo>() : new List<TaskInfo> { fallback };
        }

        static void SelectOnly(TaskInfo task)
        {
            SelectedIds.Clear();
            if (task != null && !string.IsNullOrEmpty(task.Id)) SelectedIds.Add(task.Id);
        }

        static void SetAnchor(TaskInfo task)
        {
            _selectionAnchorId = task == null || string.IsNullOrEmpty(task.Id)
                ? null : task.Id;
        }

        static void Toggle(TaskInfo task)
        {
            if (task == null || string.IsNullOrEmpty(task.Id)) return;
            if (!SelectedIds.Add(task.Id)) SelectedIds.Remove(task.Id);
        }

        static void SelectRange(TaskInfo task, bool extend)
        {
            int anchor = VisibleTasks.FindIndex(candidate =>
                candidate != null && candidate.Id == _selectionAnchorId);
            int current = VisibleTasks.FindIndex(candidate =>
                candidate != null && task != null && candidate.Id == task.Id);
            if (anchor < 0 || current < 0)
            {
                SelectOnly(task);
                SetAnchor(task);
                return;
            }

            if (!extend) SelectedIds.Clear();
            int first = Mathf.Min(anchor, current);
            int last = Mathf.Max(anchor, current);
            for (int i = first; i <= last; i++)
            {
                var candidate = VisibleTasks[i];
                if (candidate != null && !string.IsNullOrEmpty(candidate.Id))
                    SelectedIds.Add(candidate.Id);
            }
        }

        static void PruneSelection(List<TaskInfo> tasks)
        {
            var live = new HashSet<string>(tasks.Where(task => task != null)
                .Select(task => task.Id));
            SelectedIds.RemoveWhere(id => !live.Contains(id));
            if (_selectionAnchorId != null && !live.Contains(_selectionAnchorId))
                _selectionAnchorId = null;
        }
    }
}
