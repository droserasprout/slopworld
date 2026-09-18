using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // What a session runs, and the sandbox presets that come with it. Fetched for the same
    // reason presets are: a command file added while the game was up is one the dropdown
    // has to be able to show.
    public class CommandInfo
    {
        public const string AgentKind = "agent";
        public const string ShellKind = "shell";

        public string Name = "";
        // Defaults use this role to keep agent CLIs and interactive shells in the right lists.
        public string Kind = AgentKind;
        public string Description = "";
        public string Source = "";
        // What it runs before this machine's `[defaults]` and the agent's own override.
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
