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

        // --------------------------------------------------------------- sub-option builders

        static SubOption AgentOption(SessionInfo session) =>
            new SubOption
            {
                Label =
                    $"{session.Name}  ({session.State.ToString().ToLowerInvariant()})  -  {session.Project}",
                Value = session.Name,
            };

        static List<SubOption> AgentOptions(Func<SessionInfo, bool> include, string emptyLabel)
        {
            var list = SessionHub.Instance.Sessions.Where(include).Select(AgentOption).ToList();
            if (list.Count == 0)
                list.Add(new SubOption { Label = emptyLabel, Enabled = false });
            return list;
        }

        static List<SubOption> AgentsSub(params AgentState[] states)
        {
            var set = new HashSet<AgentState>(states);
            return AgentOptions(
                s => set.Contains(s.State) && !s.Ephemeral,
                "(no agents in that state)"
            );
        }

        static List<SubOption> AgentsSubAll() =>
            AgentOptions(s => !s.Ephemeral && !s.Host, "(no agents)");

        static List<SubOption> AgentsSubEditable() =>
            AgentOptions(s => (!s.Ephemeral || s.Worker) && !s.Host, "(no editable agents)");

        static List<SubOption> AgentsSubWithPawn()
        {
            var colony = AgentColony.Current;
            return AgentOptions(
                s => !s.Ephemeral && !s.Host && colony?.PawnOf(s.Name) != null,
                "(no agents with a colonist)"
            );
        }

        // Duplicate is meaningful for any session with a project, including a temporary errand. The
        // dialog copies that project's command and sandbox context, while a project-less session
        // has nowhere useful to start from.
        static List<SubOption> AgentsSubWithProject() =>
            AgentOptions(
                s => !s.Host && !s.Worker && !string.IsNullOrEmpty(s.Project),
                "(no agents with a project)"
            );

        // Shell clones saved agent configuration; temporary errands have no saved entry.
        static List<SubOption> AgentsSubForShell() =>
            AgentOptions(
                s => !s.Ephemeral && !s.Host && !s.Worker && !string.IsNullOrEmpty(s.Project),
                "(no saved agents with a project)"
            );

        static List<SubOption> DeletableProjectsSub()
        {
            var list = SessionHub
                .Instance.Projects.Where(p =>
                    !SessionHub.Instance.Sessions.Any(s => s.Project == p.Name)
                )
                .Select(p => new SubOption { Label = $"{p.Name}  -  {p.Dir}", Value = p.Name })
                .ToList();

            if (list.Count == 0)
                list.Add(
                    new SubOption { Label = "(no projects without sessions)", Enabled = false }
                );
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
                    Checked = !Sidebar.Filtering,
                },
            };

            foreach (
                var p in SessionHub.Instance.Projects.OrderBy(p => p.Name, StringComparer.Ordinal)
            )
                list.Add(
                    new SubOption
                    {
                        Label = $"{p.Name}  -  {p.Dir}",
                        Value = p.Name,
                        Checked = Sidebar.Ticked(p.Name),
                    }
                );

            list.Add(
                new SubOption
                {
                    Label = $"{Sidebar.NoProject}  -  whatever belongs to no project",
                    Value = Sidebar.NoProject,
                    Checked = Sidebar.Ticked(Sidebar.NoProject),
                }
            );
            return list;
        }

        static List<SubOption> LibraryItemsSub()
        {
            var list = SessionHub
                .Instance.Library.Where(s => s.Runnable)
                .Select(s => new SubOption
                {
                    Label = $"{s.Name}  ({s.Kind.ToString().ToLowerInvariant()})",
                    Value = s.Name,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no library entries)", Enabled = false });
            return list;
        }

        static List<SubOption> LibraryManageSub()
        {
            var list = SessionHub
                .Instance.Library.Where(s => !s.Builtin)
                .Select(s => new SubOption
                {
                    Label = $"{s.Name}  ({s.Kind.ToString().ToLowerInvariant()})",
                    Value = s.Name,
                    Enabled = true,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(
                    new SubOption { Label = "(no editable library entries)", Enabled = false }
                );
            return list;
        }

        static List<SubOption> HostSessionsSub()
        {
            var list = SessionHub
                .Instance.Sessions.Where(s => s.Host)
                .Select(s => new SubOption { Label = $"{s.Name}  -  {s.Dir}", Value = s.Name })
                .ToList();

            if (list.Count == 0)
                list.Add(
                    new SubOption
                    {
                        Label = "(no " + SessionHub.Instance.Capabilities.TerminalNames + ")",
                        Enabled = false,
                    }
                );
            return list;
        }

        static List<SubOption> AgentStatusSub() =>
            new List<SubOption>
            {
                new SubOption
                {
                    Label = "All",
                    Value = "all",
                    Checked = !Sidebar.StatusFiltering,
                },
                new SubOption
                {
                    Label = "Active",
                    Value = "active",
                    Checked = (Sidebar.StatusFilter & AgentStatusFilter.Active) != 0,
                },
                new SubOption
                {
                    Label = "Idle",
                    Value = "idle",
                    Checked = (Sidebar.StatusFilter & AgentStatusFilter.Idle) != 0,
                },
                new SubOption
                {
                    Label = "Down",
                    Value = "down",
                    Checked = (Sidebar.StatusFilter & AgentStatusFilter.Down) != 0,
                },
                new SubOption
                {
                    Label = "Shells",
                    Value = "shells",
                    Checked = (Sidebar.StatusFilter & AgentStatusFilter.Shells) != 0,
                },
            };

        static void SetAgentStatusFromPalette(string value)
        {
            if (string.Equals(value, "all", StringComparison.Ordinal))
            {
                Sidebar.SetStatusFilter(AgentStatusSelection.ToggleAll(Sidebar.StatusFilter));
                return;
            }

            AgentStatusFilter status;
            switch (value)
            {
                case "active":
                    status = AgentStatusFilter.Active;
                    break;
                case "idle":
                    status = AgentStatusFilter.Idle;
                    break;
                case "down":
                    status = AgentStatusFilter.Down;
                    break;
                case "shells":
                    status = AgentStatusFilter.Shells;
                    break;
                default:
                    return;
            }

            Sidebar.SetStatusFilter(AgentStatusSelection.Toggle(Sidebar.StatusFilter, status));
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
                list.Add(
                    new SubOption
                    {
                        Label =
                            Radio.Picked == s && !Radio.Muted
                                ? $"{s.Name}  -  {Radio.RateLabel(s.Rate)}  (playing)"
                                : s.Name,
                        Children = () => JukeboxPresetsSub(s),
                    }
                );
            }
            return list;
        }

        static List<SubOption> JukeboxPresetsSub(Radio.Station station)
        {
            var list = new List<SubOption>();
            foreach (int preset in station.Rates)
            {
                var rate = preset;
                list.Add(
                    new SubOption
                    {
                        Label =
                            Radio.RateLabel(rate)
                            + (
                                Radio.Picked == station && !Radio.Muted && station.Rate == rate
                                    ? "  (playing)"
                                    : ""
                            ),
                        Select = () => Radio.Pick(station, rate),
                    }
                );
            }
            return list;
        }

        // --------------------------------------------------------------- actions
    }
}
