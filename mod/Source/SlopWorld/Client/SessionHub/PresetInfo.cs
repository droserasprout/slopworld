using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // Fetched rather than listed here: a preset the daemon does not have is a
    // checkbox that saves and then does nothing.
    public class PresetInfo
    {
        public string Name = "";
        public string Description = "";
        // Automatically included before this preset.  The daemon resolves the authoritative
        // closure; the client keeps it to show why a checkbox is unavailable.
        public List<string> Requires = new List<string>();
        // `system` is compiled into slopd, `user` exists only in the preset directory, and
        // `override` is a user definition replacing a system entry with the same name.
        public string Source = "";
        // Kept apart because the project dialog groups what a sandbox is handed the way it is
        // edited. A device node goes with the read-only binds: it is bound rather than passed,
        // and which flag bwrap gets is not this screen's business.
        public List<string> Ro = new List<string>();
        public List<string> Rw = new List<string>();
        public List<string> Dev = new List<string>();
        public List<string> Env = new List<string>();
        public List<string> Seed = new List<string>();
        public List<string> Skip = new List<string>();
        public Dictionary<string, string> Setenv = new Dictionary<string, string>();
        // Bound to a copy of its own rather than to the host's. Its own group because "where
        // did my ~/.claude go" is the question a session's own copy raises, and a path that
        // read as an ordinary bind would answer it wrongly.
        public List<string> Private = new List<string>();
        // The host's own file, read-write, inside one of those copies: a credential that
        // rotates cannot be a copy. Grouped with Rw rather than Private because that is what
        // it is - the one path here something inside can write and the host will read.
        public List<string> Shared = new List<string>();

        // What ticking this costs, when it costs anything: a socket whose far end runs on the
        // host, a display every other window shares. Empty for an ordinary preset.
        public string Escapes = "";
        // A generated bind for the daemon's live tmux socket. It is not a path the editor can
        // change, but it must round-trip when the settings page saves this preset.
        public bool Tmux;
        // Generated read-only binds for config.toml and endpoint.toml. Kept out of the path
        // lists because those remain subject to the ordinary protected-path guard.
        public bool DaemonConfig;
        public bool IsEscape => !string.IsNullOrEmpty(Escapes);

        // Every path and env var the preset asks for, for the tooltip.
        public List<string> Gives =>
            (Tmux ? new[] { "SlopWorld tmux socket" } : Enumerable.Empty<string>())
                .Concat(DaemonConfig
                    ? new[] { "SlopWorld daemon config (read-only)" }
                    : Enumerable.Empty<string>())
                .Concat(Ro).Concat(Rw).Concat(Dev).Concat(Shared).Concat(Private).Concat(Env)
                .Concat(Setenv.Select(x => $"{x.Key}={x.Value}")).ToList();

        public PresetInfo Copy() => new PresetInfo
        {
            Name = Name,
            Description = Description,
            Requires = new List<string>(Requires),
            Source = Source,
            Ro = new List<string>(Ro),
            Rw = new List<string>(Rw),
            Dev = new List<string>(Dev),
            Env = new List<string>(Env),
            Private = new List<string>(Private),
            Shared = new List<string>(Shared),
            Seed = new List<string>(Seed),
            Skip = new List<string>(Skip),
            Escapes = Escapes,
            Tmux = Tmux,
            DaemonConfig = DaemonConfig,
            Setenv = new Dictionary<string, string>(Setenv),
        };

        public string ToJson() =>
            "{" + $"\"name\":{JVal.Q(Name)}," +
            $"\"description\":{JVal.Q(Description)},\"ro\":{Arr(Ro)}," +
            $"\"requires\":{Arr(Requires)}," +
            $"\"rw\":{Arr(Rw)},\"dev\":{Arr(Dev)},\"private\":{Arr(Private)}," +
            $"\"seed\":{Arr(Seed)},\"skip\":{Arr(Skip)},\"shared\":{Arr(Shared)}," +
            $"\"escapes\":{JVal.Q(Escapes)},\"env\":{Arr(Env)},\"tmux\":{(Tmux ? "true" : "false")}," +
            $"\"daemon_config\":{(DaemonConfig ? "true" : "false")}," +
            $"\"setenv\":{Map(Setenv)}}}";

        static string Arr(List<string> items) =>
            "[" + string.Join(",", items.Select(JVal.Q).ToArray()) + "]";

        static string Map(Dictionary<string, string> items) =>
            "{" + string.Join(",", items.Select(x =>
                JVal.Q(x.Key) + ":" + JVal.Q(x.Value)).ToArray()) + "}";

        public static PresetInfo FromJson(JVal j)
        {
            var p = new PresetInfo
            {
                Name = j["name"].AsString(),
                Description = j["description"].AsString(),
                Source = j["source"].AsString("system"),
                Escapes = j["escapes"].AsString(),
                Tmux = j["tmux"].AsBool(false),
                DaemonConfig = j["daemon_config"].AsBool(false),
            };
            p.Ro.AddRange(j["ro"].Items.Select(i => i.AsString()));
            p.Requires.AddRange(j["requires"].Items.Select(i => i.AsString()));
            p.Rw.AddRange(j["rw"].Items.Select(i => i.AsString()));
            p.Dev.AddRange(j["dev"].Items.Select(i => i.AsString()));
            p.Private.AddRange(j["private"].Items.Select(i => i.AsString()));
            p.Seed.AddRange(j["seed"].Items.Select(i => i.AsString()));
            p.Skip.AddRange(j["skip"].Items.Select(i => i.AsString()));
            p.Shared.AddRange(j["shared"].Items.Select(i => i.AsString()));
            p.Env.AddRange(j["env"].Items.Select(i => i.AsString()));
            if (j["setenv"].IsObject)
                foreach (var pair in j["setenv"].ObjectItems)
                    p.Setenv[pair.Key] = pair.Value.AsString();
            return p;
        }
    }
}
