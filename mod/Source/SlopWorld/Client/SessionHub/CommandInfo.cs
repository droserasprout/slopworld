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

        public string ToJson() =>
            "{" + $"\"name\":{JVal.Q(Name)}," +
            $"\"kind\":{JVal.Q(Kind)}," +
            $"\"description\":{JVal.Q(Description)},\"cmd\":{JVal.Q(Cmd)}," +
            $"\"sandbox\":[{string.Join(",", Sandbox.Select(JVal.Q).ToArray())}]}}";

        public CommandInfo Copy() => new CommandInfo
        {
            Name = Name,
            Kind = Kind,
            Description = Description,
            Source = Source,
            Cmd = Cmd,
            Sandbox = new List<string>(Sandbox),
        };

        public static CommandInfo FromJson(JVal j) => new CommandInfo
        {
            Name = j["name"].AsString(),
            Kind = j["kind"].AsString(AgentKind),
            Description = j["description"].AsString(),
            Source = j["source"].AsString("system"),
            Cmd = j["cmd"].AsString(),
            Sandbox = j["sandbox"].Items.Select(i => i.AsString()).ToList(),
        };
    }
}
