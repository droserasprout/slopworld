using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // The daemon owns the file and hands these over parsed, so nothing in the mod has
    // to speak TOML. This is a read model for the settings pages, not a second schema for
    // the whole daemon config. Writes are partial patches, so fields not represented here
    // remain untouched on the server.
    public class SlopConfig
    {
        // Read-only status for the connection note. The actual connection comes from the
        // daemon endpoint descriptor or the mod's explicit override.
        public string Bind = "127.0.0.1:7717";
        public string TmuxSocket = "slopworld";
        public int PollMs = 80;
        // Scrollback tmux keeps per pane, and so the ceiling on how much history survives
        // a daemon restart.
        public int HistoryLimit = 5000;
        // How the daemon relaunches the game for "save and restart".
        public string GameCmd = "~/.local/bin/slopworld";
        // Off means the daemon never reads the credentials file.
        public bool Usage = true;
        public int UsagePollSecs = 60;
        // Where the daemon looks for Claude Code's OAuth token.
        public string ClaudeCredentials = "~/.claude/.credentials.json";
        // The other subscription, and off unless somebody says otherwise: there is no login
        // on the host to read a key out of. Shares the poll interval above - a balance moves
        // slower than a rate limit, never faster.
        public bool Openrouter;
        // Blank means the daemon reads OPENROUTER_API_KEY out of its own environment, which
        // is where the pi preset forwards it from.
        public string OpenrouterKeyFile = "";
        // Codex's ChatGPT login carries the token the usage endpoint needs. Unlike a key,
        // it is a live login file that the daemon reads fresh and never sends to the mod.
        public bool Openai = true;
        public string OpenaiCredentials = "~/.codex/auth.json";
        // Prompt-derived session titles are a separate OpenRouter request, not part of the
        // usage poll. Kept here because the Usage page owns both the key and the Codex rows.
        public string AgentTitles = "never";
        public string TitleModel = "google/gemini-3.1-flash-lite";

        // Both name a command preset: what an agent that names none of its own runs, and
        // what a shell errand runs. What each one *is* is a TOML file the daemon reads.
        public string Agent = "claude";
        public string Shell = "shell";

        public static SlopConfig FromJson(JVal v)
        {
            var d = v["daemon"];
            var f = v["defaults"];
            return new SlopConfig
            {
                Bind = d["bind"].AsString("127.0.0.1:7717"),
                TmuxSocket = d["tmux_socket"].AsString("slopworld"),
                PollMs = d["poll_ms"].AsInt(80),
                HistoryLimit = d["history_limit"].AsInt(5000),
                GameCmd = d["game_cmd"].AsString("~/.local/bin/slopworld"),
                Usage = d["usage"].AsBool(true),
                UsagePollSecs = d["usage_poll_secs"].AsInt(60),
                ClaudeCredentials =
                    d["claude_credentials"].AsString("~/.claude/.credentials.json"),
                Openrouter = d["openrouter"].AsBool(false),
                OpenrouterKeyFile = d["openrouter_key_file"].AsString(),
                Openai = d["openai"].AsBool(true),
                OpenaiCredentials = d["openai_credentials"].AsString("~/.codex/auth.json"),
                AgentTitles = d["agent_titles"].AsString("never"),
                TitleModel = d["title_model"].AsString("google/gemini-3.1-flash-lite"),

                Agent = f["agent"].AsString("claude"),
                Shell = f["shell"].AsString("shell"),
            };
        }

        // This deliberately omits bind, token, projects, sessions, state rules and sandbox
        // presets. The daemon deep-merges this object before validating it.
        public string ToPatchJson() =>
            "{\"daemon\":{" +
            $"\"tmux_socket\":{JVal.Q(TmuxSocket)},\"poll_ms\":{PollMs}," +
            $"\"history_limit\":{HistoryLimit},\"game_cmd\":{JVal.Q(GameCmd)}," +
            $"\"usage\":{JVal.B(Usage)},\"usage_poll_secs\":{UsagePollSecs}," +
            $"\"claude_credentials\":{JVal.Q(ClaudeCredentials)}," +
            $"\"openrouter\":{JVal.B(Openrouter)}," +
            $"\"openrouter_key_file\":{JVal.Q(OpenrouterKeyFile)}," +
            $"\"openai\":{JVal.B(Openai)}," +
            $"\"openai_credentials\":{JVal.Q(OpenaiCredentials)}," +
            $"\"agent_titles\":{JVal.Q(AgentTitles)}," +
            $"\"title_model\":{JVal.Q(TitleModel)}" +
            "}," +
            "\"defaults\":{" +
            $"\"agent\":{JVal.Q(Agent)},\"shell\":{JVal.Q(Shell)}" +
            "}}";

        // One entry per line, which is how the GUI edits these lists.
        public static string Lines(List<string> items) =>
            string.Join("\n", items.ToArray());

        public static List<string> Split(string text) =>
            (text ?? "").Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();
    }
}
