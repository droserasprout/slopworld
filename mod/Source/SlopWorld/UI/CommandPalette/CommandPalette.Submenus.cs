using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public partial class CommandPalette
    {
        static bool Playing() => Current.ProgramState == ProgramState.Playing;

        static bool HasTaskRecipients() => SessionHub.Instance.Sessions.Any(s =>
            s != null && !s.Ephemeral && !s.Host && !string.IsNullOrEmpty(s.Name));

        // --------------------------------------------------------------- sub-option builders

        static List<SubOption> AgentsSub(params AgentState[] states)
        {
            var set = new HashSet<AgentState>(states);
            var list = SessionHub.Instance.Sessions
                .Where(s => set.Contains(s.State) && !s.Ephemeral)
                .Select(s => new SubOption
                {
                    Label = $"{s.Name}  ({s.State.ToString().ToLower()})  -  {s.Project}",
                    Value = s.Name,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no agents in that state)", Enabled = false });
            return list;
        }

        static List<SubOption> AgentsSubAll()
        {
            var list = SessionHub.Instance.Sessions
                .Where(s => !s.Ephemeral && !s.Host)
                .Select(s => new SubOption
                {
                    Label = $"{s.Name}  ({s.State.ToString().ToLower()})  -  {s.Project}",
                    Value = s.Name,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no agents)", Enabled = false });
            return list;
        }

        static List<SubOption> AgentsSubEditable()
        {
            var list = SessionHub.Instance.Sessions
                .Where(s => !s.Ephemeral && !s.Host && !s.Worker)
                .Select(s => new SubOption
                {
                    Label = $"{s.Name}  ({s.State.ToString().ToLower()})  -  {s.Project}",
                    Value = s.Name,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no editable agents)", Enabled = false });
            return list;
        }

        static List<SubOption> AgentsSubWithPawn()
        {
            var colony = AgentColony.Current;
            var list = SessionHub.Instance.Sessions
                .Where(s => !s.Ephemeral && !s.Host && colony?.PawnOf(s.Name) != null)
                .Select(s => new SubOption
                {
                    Label = $"{s.Name}  ({s.State.ToString().ToLower()})  -  {s.Project}",
                    Value = s.Name,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no agents with a colonist)", Enabled = false });
            return list;
        }

        // Duplicate is meaningful for any session with a project, including a temporary
        // errand: the dialog copies that project's command and sandbox context, while a
        // project-less session has nowhere useful to start from.
        static List<SubOption> AgentsSubWithProject()
        {
            var list = SessionHub.Instance.Sessions
                .Where(s => !s.Host && !s.Worker && !string.IsNullOrEmpty(s.Project))
                .Select(s => new SubOption
                {
                    Label = $"{s.Name}  ({s.State.ToString().ToLower()})  -  {s.Project}",
                    Value = s.Name,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no agents with a project)", Enabled = false });
            return list;
        }

        static List<SubOption> ProjectsSub()
        {
            var list = SessionHub.Instance.Projects
                .Select(p => new SubOption
                {
                    Label = $"{p.Name}  -  {p.Dir}",
                    Value = p.Name,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no projects)", Enabled = false });
            return list;
        }

        static List<SubOption> DeletableProjectsSub()
        {
            var list = SessionHub.Instance.Projects
                .Where(p => !SessionHub.Instance.Sessions.Any(s => s.Project == p.Name))
                .Select(p => new SubOption
                {
                    Label = $"{p.Name}  -  {p.Dir}",
                    Value = p.Name,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no projects without sessions)", Enabled = false });
            return list;
        }

        // The sidebar's filter, one row a checkbox - the same ticks the strip's own menu
        // draws. The blank value is that menu's "all projects", which clears the filter
        // rather than ticking anything.
        static List<SubOption> FilterSub()
        {
            var list = new List<SubOption>
            {
                new SubOption
                {
                    Label = "All projects",
                    Value = "",
                    Checked = !AgentSidebar.Filtering,
                },
            };

            foreach (var p in SessionHub.Instance.Projects.OrderBy(p => p.Name,
                StringComparer.Ordinal))
                list.Add(new SubOption
                {
                    Label = $"{p.Name}  -  {p.Dir}",
                    Value = p.Name,
                    Checked = AgentSidebar.Ticked(p.Name),
                });

            list.Add(new SubOption
            {
                Label = $"{AgentSidebar.NoProject}  -  whatever belongs to no project",
                Value = AgentSidebar.NoProject,
                Checked = AgentSidebar.Ticked(AgentSidebar.NoProject),
            });
            return list;
        }

        static List<SubOption> LibraryItemsSub()
        {
            var list = SessionHub.Instance.Library
                .Where(s => s.Kind != LibraryItemKind.Breadcrumb && s.Kind != LibraryItemKind.FileAction)
                .Select(s => new SubOption
                {
                    Label = $"{s.Name}  ({s.Kind.ToString().ToLower()})",
                    Value = s.Name,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no library entries)", Enabled = false });
            return list;
        }

        static List<SubOption> LibraryManageSub()
        {
            var list = SessionHub.Instance.Library
                .Where(s => !s.Builtin)
                .Select(s => new SubOption
                {
                    Label = $"{s.Name}  ({s.Kind.ToString().ToLower()})",
                    Value = s.Name,
                    Enabled = s.Kind != LibraryItemKind.Breadcrumb ||
                              SessionHub.Instance.Config.ExperimentalBreadcrumbs,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no editable library entries)", Enabled = false });
            return list;
        }

        static List<SubOption> NewLibraryItemSub() => new List<SubOption>
        {
            new SubOption { Label = "Prompt", Select = () => NewLibraryItem(LibraryItemKind.Prompt) },
            new SubOption
            {
                Label = "Breadcrumb",
                Enabled = SessionHub.Instance.Config.ExperimentalBreadcrumbs,
                Select = () => NewLibraryItem(LibraryItemKind.Breadcrumb),
            },
            new SubOption { Label = "Shell", Select = () => NewLibraryItem(LibraryItemKind.Shell) },
            new SubOption { Label = "File Action", Select = () => NewLibraryItem(LibraryItemKind.FileAction) },
        };

        static void NewLibraryItem(LibraryItemKind kind) =>
            TerminalWindow.OpenOverPane(new EditLibraryItemDialog(kind));

        static List<SubOption> HostShellSub()
        {
            var list = new List<SubOption>
            {
                new SubOption
                {
                    Label = "~",
                    Value = "",
                    Select = () => SessionHub.Instance.SessionStore.RunHostShell("",
                        session => TerminalWindow.Open(session), UiLayout.Fail),
                },
            };

            foreach (var p in SessionHub.Instance.Projects)
            {
                string name = p.Name;
                list.Add(new SubOption
                {
                    Label = $"{name}  -  {p.Dir}",
                    Value = name,
                    Select = () => SessionHub.Instance.SessionStore.RunHostShell(name,
                        session => TerminalWindow.Open(session), UiLayout.Fail),
                });
            }
            return list;
        }

        static List<SubOption> HostSessionsSub()
        {
            var list = SessionHub.Instance.Sessions
                .Where(s => s.Host)
                .Select(s => new SubOption
                {
                    Label = $"{s.Name}  -  {s.Dir}",
                    Value = s.Name,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption
                {
                    Label = "(no " + SessionHub.Instance.Capabilities.TerminalNames + ")",
                    Enabled = false,
                });
            return list;
        }

        static string TaskLabel(TaskInfo task) =>
            $"{task.Summary}  ({TaskInfo.StatusText(task.Status)}, {task.Direction})  -  {task.Id}";

        static List<SubOption> TasksSub()
        {
            var list = SessionHub.Instance.Tasks
                .Where(task => task != null && !string.IsNullOrEmpty(task.Id))
                .Select(task => new SubOption
                {
                    Label = TaskLabel(task),
                    Value = task.Id,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no tasks)", Enabled = false });
            return list;
        }

        static List<SubOption> CancelableTasksSub()
        {
            var list = SessionHub.Instance.Tasks
                .Where(task => task != null && !string.IsNullOrEmpty(task.Id) &&
                    (task.Status == DelegatedTaskStatus.Queued ||
                     task.Status == DelegatedTaskStatus.Accepted))
                .Select(task => new SubOption
                {
                    Label = TaskLabel(task),
                    Value = task.Id,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no cancelable tasks)", Enabled = false });
            return list;
        }

        static List<SubOption> TerminalTasksSub()
        {
            var list = SessionHub.Instance.Tasks
                .Where(task => task != null && !string.IsNullOrEmpty(task.Id) && task.Terminal)
                .Select(task => new SubOption
                {
                    Label = TaskLabel(task),
                    Value = task.Id,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no completed tasks)", Enabled = false });
            return list;
        }

        static List<SubOption> TaskStatusSub()
        {
            var list = SessionHub.Instance.Tasks
                .Where(task => task != null && !string.IsNullOrEmpty(task.Id) &&
                    task.Incoming && !task.Terminal)
                .Select(task =>
                {
                    var selected = task;
                    return new SubOption
                    {
                        Label = TaskLabel(selected),
                        Children = () => TaskStatusChoices(selected),
                    };
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no incoming tasks to update)", Enabled = false });
            return list;
        }

        static List<SubOption> TaskStatusChoices(TaskInfo task)
        {
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
                list.Add(new SubOption
                {
                    Label = TaskInfo.StatusText(status),
                    Enabled = task.Status != status,
                    Select = () => SessionHub.Instance.TaskStore.UpdateStatus(
                        task.Id, status, null, null, UiLayout.Fail),
                });
            }
            return list;
        }

        static List<SubOption> AgentStatusSub() => new List<SubOption>
        {
            new SubOption
            {
                Label = "All",
                Value = "all",
                Checked = !AgentSidebar.StatusFiltering,
            },
            new SubOption
            {
                Label = "Active",
                Value = "active",
                Checked = (AgentSidebar.StatusFilter & AgentStatusFilter.Active) != 0,
            },
            new SubOption
            {
                Label = "Idle",
                Value = "idle",
                Checked = (AgentSidebar.StatusFilter & AgentStatusFilter.Idle) != 0,
            },
            new SubOption
            {
                Label = "Down",
                Value = "down",
                Checked = (AgentSidebar.StatusFilter & AgentStatusFilter.Down) != 0,
            },
        };

        static void SetAgentStatusFromPalette(string value)
        {
            if (string.Equals(value, "all", StringComparison.Ordinal))
            {
                AgentSidebar.SetStatusFilter(AgentStatusFilter.All);
                return;
            }

            AgentStatusFilter status;
            switch (value)
            {
                case "active": status = AgentStatusFilter.Active; break;
                case "idle": status = AgentStatusFilter.Idle; break;
                case "down": status = AgentStatusFilter.Down; break;
                default: return;
            }

            var selected = AgentSidebar.StatusFilter;
            var next = selected == AgentStatusFilter.All
                ? status
                : (selected & status) != 0 ? selected & ~status : selected | status;
            AgentSidebar.SetStatusFilter(next);
        }

        static List<SubOption> JukeboxSub()
        {
            var list = new List<SubOption>
            {
                new SubOption
                {
                    Label = Radio.Picked == null && !Radio.Muted ? "OST  (playing)" : "OST",
                    Select = Radio.PickOst,
                },
            };

            foreach (var station in Radio.Stations)
            {
                var s = station;
                list.Add(new SubOption
                {
                    Label = Radio.Picked == s && !Radio.Muted
                        ? $"{s.Name}  -  {Radio.RateLabel(s.Rate)}  (playing)"
                        : s.Name,
                    Children = () => JukeboxPresetsSub(s),
                });
            }
            return list;
        }

        static List<SubOption> JukeboxPresetsSub(Radio.Station station)
        {
            var list = new List<SubOption>();
            foreach (int preset in station.Rates)
            {
                var rate = preset;
                list.Add(new SubOption
                {
                    Label = Radio.RateLabel(rate)
                        + (Radio.Picked == station && !Radio.Muted && station.Rate == rate
                            ? "  (playing)" : ""),
                    Select = () => Radio.Pick(station, rate),
                });
            }
            return list;
        }

        // --------------------------------------------------------------- actions

    }
}
