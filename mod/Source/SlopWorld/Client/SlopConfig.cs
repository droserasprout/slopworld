using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    /// <summary>
    /// The parts of config.toml the settings GUI edits. The daemon owns the file
    /// and hands these over parsed, so nothing in the mod has to speak TOML;
    /// sessions and state rules are left out on purpose, because a section this
    /// window never shows is a section it must not write back.
    /// </summary>
    public class SlopConfig
    {
        public string Bind = "127.0.0.1:7717";
        public string Token = "";
        public string TmuxSocket = "slopworld";
        public int PollMs = 80;
        /// Scrollback tmux keeps per pane, and so the ceiling on how much history
        /// survives a daemon restart.
        public int HistoryLimit = 5000;
        /// How the daemon relaunches the game for "save and restart".
        public string GameCmd = "";
        /// Whether the daemon polls Anthropic for what is left of the
        /// subscription. Off means it never reads the credentials file.
        public bool Usage = true;
        public int UsagePollSecs = 60;
        /// Where the daemon looks for Claude Code's OAuth token.
        public string ClaudeCredentials = "~/.claude/.credentials.json";

        public string Agent = "claude";

        public bool SandboxEnabled = true;
        public List<string> RoPaths = new List<string>();
        public List<string> RwPaths = new List<string>();
        public List<string> PassEnv = new List<string>();

        public static SlopConfig FromJson(JVal v)
        {
            var d = v["daemon"];
            var f = v["defaults"];
            var s = v["sandbox"];
            return new SlopConfig
            {
                Bind = d["bind"].AsString("127.0.0.1:7717"),
                Token = d["token"].AsString(),
                TmuxSocket = d["tmux_socket"].AsString("slopworld"),
                PollMs = d["poll_ms"].AsInt(80),
                HistoryLimit = d["history_limit"].AsInt(5000),
                GameCmd = d["game_cmd"].AsString(),
                Usage = d["usage"].AsBool(true),
                UsagePollSecs = d["usage_poll_secs"].AsInt(60),
                ClaudeCredentials =
                    d["claude_credentials"].AsString("~/.claude/.credentials.json"),

                Agent = f["agent"].AsString("claude"),

                SandboxEnabled = s["enabled"].AsBool(true),
                RoPaths = Strings(s["ro_paths"]),
                RwPaths = Strings(s["rw_paths"]),
                PassEnv = Strings(s["pass_env"]),
            };
        }

        // Every field of a section this writes back has to be here, or saving from
        // the GUI silently resets the ones it left out to their serde defaults -
        // which for game_cmd would mean losing it on any unrelated save.
        public string ToJson() =>
            "{\"daemon\":{" +
            $"\"bind\":{JVal.Q(Bind)},\"token\":{JVal.Q(Token)}," +
            $"\"tmux_socket\":{JVal.Q(TmuxSocket)},\"poll_ms\":{PollMs}," +
            $"\"history_limit\":{HistoryLimit},\"game_cmd\":{JVal.Q(GameCmd)}," +
            $"\"usage\":{JVal.B(Usage)},\"usage_poll_secs\":{UsagePollSecs}," +
            $"\"claude_credentials\":{JVal.Q(ClaudeCredentials)}}}," +
            "\"defaults\":{" +
            $"\"agent\":{JVal.Q(Agent)}}}," +
            "\"sandbox\":{" +
            $"\"enabled\":{JVal.B(SandboxEnabled)}," +
            $"\"ro_paths\":{Arr(RoPaths)},\"rw_paths\":{Arr(RwPaths)}," +
            $"\"pass_env\":{Arr(PassEnv)}}}}}";

        static List<string> Strings(JVal a) => a.Items.Select(i => i.AsString()).ToList();

        static string Arr(List<string> items) =>
            "[" + string.Join(",", items.Select(JVal.Q).ToArray()) + "]";

        /// <summary>One entry per line, which is how the GUI edits these lists.</summary>
        public static string Lines(List<string> items) =>
            string.Join("\n", items.ToArray());

        public static List<string> Split(string text) =>
            (text ?? "").Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();
    }
}
