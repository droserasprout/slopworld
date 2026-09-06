using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SlopWorld
{
    // Menus and confirmations for task state changes.
    public static class TaskActions
    {
        public static void OpenMenu(TaskInfo task, Action<TaskInfo> updated = null)
        {
            if (task == null) return;

            var selected = TasksView.SelectionFor(task);
            var cancelable = selected.Where(candidate => candidate != null &&
                (candidate.Status == DelegatedTaskStatus.Queued ||
                 candidate.Status == DelegatedTaskStatus.Accepted)).ToList();
            var removable = selected.Where(candidate => candidate != null && candidate.Terminal)
                .ToList();

            var options = new List<FloatMenuOption>();
            if (task.Incoming && !task.Terminal)
            {
                AddStatus(options, task, DelegatedTaskStatus.Accepted, updated);
                AddStatus(options, task, DelegatedTaskStatus.Working, updated);
                AddStatus(options, task, DelegatedTaskStatus.Done, updated);
                AddStatus(options, task, DelegatedTaskStatus.Failed, updated);
            }

            if (cancelable.Count > 0)
            {
                string label = cancelable.Count == 1
                    ? "Cancel"
                    : $"Cancel {cancelable.Count} selected tasks";
                options.Add(new FloatMenuOption(label, () => CancelTasks(cancelable)));
            }

            var counterpart = SessionHub.Instance.Get(task.Counterpart);
            if (counterpart != null)
            {
                var terminal = new FloatMenuOption("Open agent terminal", () =>
                    TerminalWindow.Open(counterpart.Name));
                terminal.Disabled = !counterpart.Alive;
                options.Add(terminal);
            }

            if (removable.Count > 0)
            {
                string label = removable.Count == 1
                    ? "Remove"
                    : $"Remove {removable.Count} selected tasks";
                options.Add(new FloatMenuOption(label, () => RemoveTasks(removable)));
            }

            if (options.Count > 0)
                TerminalWindow.OpenOverPane(new UiMenu(options));
        }

        static void AddStatus(List<FloatMenuOption> options, TaskInfo task,
                              DelegatedTaskStatus status, Action<TaskInfo> updated)
        {
            var option = new FloatMenuOption(TaskInfo.StatusText(status), () =>
                SessionHub.Instance.UpdateTask(task.Id, status, null, updated, UiWidgets.Fail));
            option.Disabled = task.Status == status;
            options.Add(option);
        }

        public static void RemoveTask(TaskInfo task)
        {
            if (task == null || !task.Terminal) return;
            TerminalWindow.OpenOverPane(ConfirmDialog.Create(
                $"Remove task '{task.Id}'? It will disappear for both participants.",
                () => SessionHub.Instance.RemoveTask(task.Id, null, UiWidgets.Fail),
                destructive: true));
        }

        public static void CancelTask(TaskInfo task, Action<TaskInfo> updated = null)
        {
            if (task == null || (task.Status != DelegatedTaskStatus.Queued &&
                                 task.Status != DelegatedTaskStatus.Accepted)) return;
            CancelTasks(new List<TaskInfo> { task }, updated);
        }

        static void CancelTasks(List<TaskInfo> tasks, Action<TaskInfo> updated = null)
        {
            if (tasks == null || tasks.Count == 0) return;

            string prompt = tasks.Count == 1
                ? $"Cancel task '{tasks[0].Id}'? It will be marked canceled for both participants."
                : $"Cancel {tasks.Count} selected tasks? They will be marked canceled.";
            TerminalWindow.OpenOverPane(ConfirmDialog.Create(prompt, () =>
            {
                Action done = null;
                if (updated != null && tasks.Count == 1)
                    done = () =>
                    {
                        var canceled = SessionHub.Instance.Tasks.FirstOrDefault(
                            task => task.Id == tasks[0].Id);
                        if (canceled != null) updated(canceled);
                    };
                SessionHub.Instance.CancelTasks(tasks.Select(task => task.Id), done,
                    UiWidgets.Fail);
            }, destructive: true));
        }

        static void RemoveTasks(List<TaskInfo> tasks)
        {
            if (tasks == null || tasks.Count == 0) return;

            string prompt = tasks.Count == 1
                ? $"Remove task '{tasks[0].Id}'? It will disappear for both participants."
                : $"Remove {tasks.Count} selected tasks? They will disappear for both participants.";
            TerminalWindow.OpenOverPane(ConfirmDialog.Create(prompt, () =>
            {
                SessionHub.Instance.RemoveTasks(tasks.Select(task => task.Id), null,
                    UiWidgets.Fail);
            }, destructive: true));
        }
    }
}
