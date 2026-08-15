using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // What an agent runs, and the sandbox presets that come with it. Fetched for the same
    // reason presets are: a command file added while the game was up is one the dropdown
    // has to be able to show.
    public class CommandInfo
    {
        public string Name = "";
        public string Category = "";
        public string Description = "";
        public string Source = "";
        // What it runs before this machine's `[defaults]` and the agent's own override.
        public string Cmd = "";
        public List<string> Sandbox = new List<string>();

        public string ToJson() =>
            "{" + $"\"name\":{JVal.Q(Name)},\"category\":{JVal.Q(Category)}," +
            $"\"description\":{JVal.Q(Description)},\"cmd\":{JVal.Q(Cmd)}," +
            $"\"sandbox\":[{string.Join(",", Sandbox.Select(JVal.Q).ToArray())}]}}";

        public CommandInfo Copy() => new CommandInfo
        {
            Name = Name,
            Category = Category,
            Description = Description,
            Source = Source,
            Cmd = Cmd,
            Sandbox = new List<string>(Sandbox),
        };

        public static CommandInfo FromJson(JVal j) => new CommandInfo
        {
            Name = j["name"].AsString(),
            Category = j["category"].AsString(),
            Description = j["description"].AsString(),
            Source = j["source"].AsString("system"),
            Cmd = j["cmd"].AsString(),
            Sandbox = j["sandbox"].Items.Select(i => i.AsString()).ToList(),
        };
    }
}
