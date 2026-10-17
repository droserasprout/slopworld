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
        All = Active | Idle | Down,
    }

    // Shared checkbox and persistence policy for sidebar and command-palette status controls.
    static class AgentStatusSelection
    {
        public static AgentStatusFilter Toggle(
            AgentStatusFilter selected,
            AgentStatusFilter status
        ) => selected ^ status;

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
            return values.Count == 0 ? "none" : string.Join(",", values.ToArray());
        }
    }
}
