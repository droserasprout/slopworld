using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    public partial class CommandPalette
    {
        // Registers Git and task commands beside their candidate and execution policies.
        // The main command table composes these groups; native views own final actions.
        static List<CommandDef> TaskCommands() =>
            new List<CommandDef>
            {
                CommandDef.ForTask(
                    "task.open",
                    "Task: Open",
                    TasksSub,
                    t => TaskDetailView.Open(t)
                ),
                CommandDef.ForTask(
                    "task.cancel",
                    "Task: Cancel",
                    CancelableTasksSub,
                    t => TaskActions.CancelTask(t)
                ),
                CommandDef.ForTask(
                    "task.remove",
                    "Task: Remove",
                    TerminalTasksSub,
                    t => TaskActions.RemoveTask(t)
                ),
                new CommandDef(
                    "task.status",
                    "Task: Set Status",
                    "Task",
                    _ => { },
                    subAction: TaskStatusSub
                ),
                new CommandDef(
                    "task.filter",
                    "Task: Filter",
                    "Task",
                    _ => TasksView.OpenFilterMenu()
                ),
            };

        static List<CommandDef> GitCommands() =>
            new List<CommandDef>
            {
                GitCommand("git.diff-all", "Git: Diff All", GitView.DiffAll),
                GitCommand("git.stage-all", "Git: Stage All", GitView.StageAll),
                GitCommand("git.unstage-all", "Git: Unstage All", GitView.UnstageAll),
                GitCommand("git.commit-staged", "Git: Commit Staged", GitView.CommitStaged),
            };

        static CommandDef GitCommand(string id, string label, Action<string> action) =>
            new CommandDef(
                id,
                label,
                "Git",
                key =>
                {
                    // Never resolve a missing checkout through its project name: that can
                    // redirect a retained menu action to the main checkout.
                    var scope = BrowseScope.IsKey(key) ? SidebarScopes.Find(key) : null;
                    if (scope == null || !scope.Ready || !SidebarScopes.Enabled(key))
                    {
                        UiLayout.Fail("checkout is no longer available");
                        return;
                    }
                    action(scope.Key);
                },
                subAction: GitCheckoutsSub
            );

        static List<SubOption> GitCheckoutsSub()
        {
            var options = SidebarScopes
                .EnabledScopes()
                .Where(scope => scope.Ready)
                .Select(scope => new SubOption
                {
                    Label = scope.Project + " / " + scope.Label + "  -  " + scope.Path,
                    Value = scope.Key,
                })
                .ToList();
            if (options.Count == 0)
                options.Add(new SubOption { Label = "(no available checkouts)", Enabled = false });
            return options;
        }

        static List<SubOption> ProjectsSub()
        {
            var list = SessionHub
                .Instance.Projects.Select(p => new SubOption
                {
                    Label = $"{p.Name}  -  {p.Dir}",
                    Value = p.Name,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no projects)", Enabled = false });
            return list;
        }

        static string TaskLabel(TaskInfo task) =>
            $"{task.Summary}  ({TaskInfo.StatusText(task.Status)}, {task.Direction})  -  {task.Id}";

        static List<SubOption> TaskOptions(
            Func<TaskInfo, bool> include,
            string emptyLabel,
            Func<TaskInfo, SubOption> option = null
        )
        {
            var list = SessionHub
                .Instance.Tasks.Where(task =>
                    task != null && !string.IsNullOrEmpty(task.Id) && include(task)
                )
                .Select(
                    option ?? (task => new SubOption { Label = TaskLabel(task), Value = task.Id })
                )
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = emptyLabel, Enabled = false });
            return list;
        }

        static List<SubOption> TasksSub() => TaskOptions(task => true, "(no tasks)");

        static List<SubOption> CancelableTasksSub() =>
            TaskOptions(task => task.CanCancel, "(no cancelable tasks)");

        static List<SubOption> TerminalTasksSub() =>
            TaskOptions(task => task.Terminal, "(no completed tasks)");

        static List<SubOption> TaskStatusSub() =>
            TaskOptions(
                task => task.Incoming && !task.Terminal,
                "(no incoming tasks to update)",
                task => new SubOption
                {
                    Label = TaskLabel(task),
                    Children = () => TaskStatusChoices(task.Id),
                }
            );

        static TaskInfo StatusTarget(string id) =>
            SessionHub.Instance.Tasks.FirstOrDefault(task =>
                task != null && task.Id == id && task.Incoming && !task.Terminal
            );

        static List<SubOption> TaskStatusChoices(string id)
        {
            var task = StatusTarget(id);
            if (task == null)
                return new List<SubOption>
                {
                    new SubOption
                    {
                        Label = "(task is no longer available for status updates)",
                        Enabled = false,
                    },
                };
            var list = new List<SubOption>();
            var statuses = new[]
            {
                DelegatedTaskStatus.Accepted,
                DelegatedTaskStatus.Working,
                DelegatedTaskStatus.Done,
                DelegatedTaskStatus.Failed,
            };
            foreach (var value in statuses)
            {
                var status = value;
                list.Add(
                    new SubOption
                    {
                        Label = TaskInfo.StatusText(status),
                        Enabled = task.Status != status,
                        Select = () => SetTaskStatus(id, status),
                    }
                );
            }
            return list;
        }

        static void SetTaskStatus(string id, DelegatedTaskStatus status)
        {
            // TaskStore publishes replacement snapshots. A child menu can outlive
            // completion, removal, or reassignment; resolve again at submission.
            var current = StatusTarget(id);
            if (current == null)
            {
                UiLayout.Fail("task is no longer available for status updates");
                return;
            }
            if (current.Status == status)
                return;
            SessionHub.Instance.TaskStore.UpdateStatus(id, status, null, null, UiLayout.Fail);
        }
    }
}
