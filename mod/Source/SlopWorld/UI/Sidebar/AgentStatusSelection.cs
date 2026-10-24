using System;
using System.Collections.Generic;

namespace SlopWorld
{
    [Flags]
    public enum AgentStatusFilter
    {
        None = 0,
        Active = 1,
        Idle = 2,
        Down = 4,
        Shells = 8,
        All = Active | Idle | Down | Shells,
    }

    // Shared checkbox and persistence policy for sidebar and command-palette status controls.
    static class AgentStatusSelection
    {
        public static AgentStatusFilter ToggleAll(AgentStatusFilter selected) =>
            selected == AgentStatusFilter.All ? AgentStatusFilter.None : AgentStatusFilter.All;

        public static AgentStatusFilter Toggle(
            AgentStatusFilter selected,
            AgentStatusFilter status
        ) => selected ^ status;

        // Host and temporary terminals have their own category regardless of process state.
        // Workers remain agents even when their sessions are temporary.
        public static bool Matches(AgentStatusFilter selected, SessionInfo session)
        {
            if (selected == AgentStatusFilter.All)
                return true;
            if (!session.Worker && (session.Host || session.Ephemeral))
                return (selected & AgentStatusFilter.Shells) != 0;
            switch (session.State)
            {
                case AgentState.Working:
                    return (selected & AgentStatusFilter.Active) != 0;
                case AgentState.Idle:
                    return (selected & AgentStatusFilter.Idle) != 0;
                case AgentState.Down:
                    return (selected & AgentStatusFilter.Down) != 0;
                default:
                    return false;
            }
        }

        public static AgentStatusFilter Parse(string value)
        {
            var selected = AgentStatusFilter.None;
            foreach (
                var raw in (value ?? "").Split(
                    new[] { ',', '|' },
                    StringSplitOptions.RemoveEmptyEntries
                )
            )
            {
                switch (raw.Trim().ToLowerInvariant())
                {
                    case "active":
                        selected |= AgentStatusFilter.Active;
                        break;
                    case "idle":
                        selected |= AgentStatusFilter.Idle;
                        break;
                    case "down":
                        selected |= AgentStatusFilter.Down;
                        break;
                    case "shells":
                        selected |= AgentStatusFilter.Shells;
                        break;
                    case "all":
                        return AgentStatusFilter.All;
                    case "none":
                        return AgentStatusFilter.None;
                }
            }
            // Empty and unrecognized saved values retain the default of showing all agents.
            return selected == AgentStatusFilter.None ? AgentStatusFilter.All : selected;
        }

        public static string Format(AgentStatusFilter selected)
        {
            if (selected == AgentStatusFilter.All)
                return "all";
            var values = new List<string>();
            if ((selected & AgentStatusFilter.Active) != 0)
                values.Add("active");
            if ((selected & AgentStatusFilter.Idle) != 0)
                values.Add("idle");
            if ((selected & AgentStatusFilter.Down) != 0)
                values.Add("down");
            if ((selected & AgentStatusFilter.Shells) != 0)
                values.Add("shells");
            return values.Count == 0 ? "none" : string.Join(",", values.ToArray());
        }
    }
}
