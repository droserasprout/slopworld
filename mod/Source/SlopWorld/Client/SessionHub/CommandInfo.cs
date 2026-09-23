using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // A session command and its required sandbox presets.
    // Fetch current definitions so the dropdown can show command files added while the game runs.
    public class CommandInfo
    {
        public const string AgentKind = "agent";
        public const string ShellKind = "shell";

        public string Name = "";
        // Defaults use this role to keep agent CLIs and interactive shells in the right lists.
        public string Kind = AgentKind;
        public string Description = "";
        public string Source = "";
        // Base command before applying machine defaults and the agent's override.
        public string Cmd = "";
        public List<string> Sandbox = new List<string>();

        public Wire.CommandPreset ToWire() => new Wire.CommandPreset
        {
            Name = Name,
            Kind = Kind,
            Description = Description,
            Cmd = Cmd,
            Sandbox = { Sandbox },
        };

        public CommandInfo Copy() => new CommandInfo
        {
            Name = Name,
            Kind = Kind,
            Description = Description,
            Source = Source,
            Cmd = Cmd,
            Sandbox = new List<string>(Sandbox),
        };

        public static CommandInfo FromWire(Wire.CommandPreset j) => new CommandInfo
        {
            Name = j.Name,
            Kind = j.Kind,
            Description = j.Description,
            Source = j.Source,
            Cmd = j.Cmd,
            Sandbox = j.Sandbox.ToList(),
        };
    }
}
