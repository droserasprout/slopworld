using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    public sealed class DaemonConfigDefaults
    {
        // A null field is unavailable metadata, not a second copy of daemon policy. The
        // settings UI uses these only for reset affordances when the daemon advertises them.
        public int? UsagePollSecs;
        public string ClaudeCredentials;
        public string OpenaiCredentials;
        public string TitleModel;
        public string SummaryPrompt;
        public int? TitleMinChars;
        public string WorkerPrompt;
        public string Agent;
        public string AgentShell;
        public string Shell;
        public string Pager;
        public string Editor;
        public string Highlighter;

        public static DaemonConfigDefaults FromJson(JVal root)
        {
            if (root == null || root.IsNull || !root.IsObject) return null;
            var result = new DaemonConfigDefaults();
            var d = root["daemon"];
            var f = root["defaults"];
            var c = root["commands"];
            var i = d["instructions"];
            result.UsagePollSecs = d["usage_poll_secs"].IsNull ? (int?)null : d["usage_poll_secs"].AsInt();
            result.ClaudeCredentials = d["claude_credentials"].AsString(null);
            result.OpenaiCredentials = d["openai_credentials"].AsString(null);
            result.TitleModel = d["title_model"].AsString(null);
            result.SummaryPrompt = d["summary_prompt"].AsString(null);
            result.TitleMinChars = d["title_min_chars"].IsNull ? (int?)null : d["title_min_chars"].AsInt();
            result.WorkerPrompt = i["worker_prompt"].AsString(null);
            result.Agent = f["agent"].AsString(null);
            result.AgentShell = f["agent_shell"].AsString(null);
            result.Shell = f["shell"].AsString(null);
            result.Pager = c["pager"].AsString(null);
            result.Editor = c["editor"].AsString(null);
            result.Highlighter = c["highlighter"].AsString(null);
            return result;
        }
    }

    public sealed class UsageCatalogInfo
    {
        public string Key = "";
        public string Label = "";
        public string Provider = "";
        public string Unit = WireProtocol.UsageUnit.Pct;
        public int Rank;
        public bool DefaultPoll;

        public static UsageCatalogInfo FromJson(JVal j) => new UsageCatalogInfo
        {
            Key = j["key"].AsString(),
            Label = j["label"].AsString(),
            Provider = j["provider"].AsString(),
            Unit = j["unit"].AsString(WireProtocol.UsageUnit.Pct),
            Rank = j["rank"].AsInt(),
            DefaultPoll = j["default_poll"].AsBool(false),
        };
    }

    public sealed class TerminalLimits
    {
        // These are client allocation guards, independent of the daemon's runtime capacity.
        public const int ClientMaxScrollbackLines = 10000;
        public const int ClientMinCols = 1;
        public const int ClientMaxCols = 500;
        public const int ClientMinRows = 1;
        public const int ClientMaxRows = 200;

        public int ScrollbackLines = 10000;
        public int MinCols = 20;
        public int MaxCols = 500;
        public int MinRows = 5;
        public int MaxRows = 200;

        public static TerminalLimits FromJson(JVal j)
        {
            var result = new TerminalLimits();
            if (j == null || j.IsNull || !j.IsObject) return result;
            int scrollback = j["scrollback_lines"].AsInt(result.ScrollbackLines);
            result.ScrollbackLines = Math.Max(1, Math.Min(ClientMaxScrollbackLines, scrollback));

            int minCols = j["min_cols"].AsInt(result.MinCols);
            int maxCols = j["max_cols"].AsInt(result.MaxCols);
            if (minCols < 1 || maxCols < minCols)
            {
                minCols = result.MinCols;
                maxCols = result.MaxCols;
            }
            result.MinCols = Math.Max(ClientMinCols, Math.Min(ClientMaxCols, minCols));
            result.MaxCols = Math.Max(result.MinCols,
                Math.Min(ClientMaxCols, maxCols));

            int minRows = j["min_rows"].AsInt(result.MinRows);
            int maxRows = j["max_rows"].AsInt(result.MaxRows);
            if (minRows < 1 || maxRows < minRows)
            {
                minRows = result.MinRows;
                maxRows = result.MaxRows;
            }
            result.MinRows = Math.Max(ClientMinRows, Math.Min(ClientMaxRows, minRows));
            result.MaxRows = Math.Max(result.MinRows,
                Math.Min(ClientMaxRows, maxRows));
            return result;
        }
    }

    // The daemon owns the file and hands these over parsed, so nothing in the mod has
    // to speak TOML. This is a read model for the settings pages, not a second schema for
    // the whole daemon config. Writes are partial patches, so fields not represented here
    // remain untouched on the server.
    public class DaemonConfig
    {
        public DaemonConfigDefaults FactoryDefaults;
        public bool MetadataAvailable;
        public List<UsageCatalogInfo> UsageCatalog = new List<UsageCatalogInfo>();
        public string TemporaryRoot = "";
        public TerminalLimits Terminal = new TerminalLimits();

        public int UsagePollSecs;
        // One entry per usage window. A zero interval means the global interval applies.
        public Dictionary<string, UsageItemConfig> UsageItems =
            new Dictionary<string, UsageItemConfig>();
        // Where the daemon looks for Claude Code's OAuth token.
        public string ClaudeCredentials = "";
        // Blank means the daemon reads OPENROUTER_API_KEY out of its own environment.
        public string OpenrouterKeyFile = "";
        // Codex's ChatGPT login carries the token the usage endpoint needs. Unlike a key,
        // it is a live login file that the daemon reads fresh and never sends to the mod.
        public string OpenaiCredentials = "";
        // Agent prompt-derived titles use a separate OpenRouter request.
        public string AgentTitles = "";
        public string TitleModel = "";
        public string SummaryPrompt = "";
        public int TitleMinChars;
        // Pi titles are generated by slopd before input reaches the sandbox.
        public string PiTitles = "";
        // Task summaries are generated once for each durable delegated task.
        public string TaskSummaries = "";
        public string WorkerPrompt = "";

        // Agent and Shell name command presets. AgentShell is the shell advertised inside
        // sandboxed agent sessions; it is separate from the shell errand preset.
        public string Agent = "";
        public string AgentShell = "";
        public string Shell = "";
        public string Pager = "";
        public string Editor = "";
        public string Highlighter = "";

        public static DaemonConfig FromJson(JVal v, JVal metadata = null)
        {
            var defaults = DaemonConfigDefaults.FromJson(metadata?["defaults"]);
            var d = v["daemon"];
            var f = v["defaults"];
            var c = v["commands"];
            var i = d["instructions"];
            return new DaemonConfig
            {
                FactoryDefaults = defaults,
                MetadataAvailable = metadata != null && !metadata.IsNull && metadata.IsObject && defaults != null,
                UsageCatalog = metadata?["usage_catalog"].Items.Select(UsageCatalogInfo.FromJson).ToList()
                    ?? new List<UsageCatalogInfo>(),
                TemporaryRoot = metadata?["temporary_root"].AsString() ?? "",
                Terminal = TerminalLimits.FromJson(metadata?["terminal"]),
                UsagePollSecs = d["usage_poll_secs"].AsInt(defaults?.UsagePollSecs ?? 0),
                UsageItems = UsageItemsFromJson(d["usage_items"]),
                ClaudeCredentials =
                    d["claude_credentials"].AsString(defaults?.ClaudeCredentials ?? ""),
                OpenrouterKeyFile = d["openrouter_key_file"].AsString(),
                OpenaiCredentials = d["openai_credentials"].AsString(defaults?.OpenaiCredentials ?? ""),
                AgentTitles = d["agent_titles"].AsString(),
                TitleModel = d["title_model"].AsString(defaults?.TitleModel ?? ""),
                SummaryPrompt = d["summary_prompt"].AsString(defaults?.SummaryPrompt ?? ""),
                TitleMinChars = d["title_min_chars"].AsInt(defaults?.TitleMinChars ?? 0),
                PiTitles = d["pi_titles"].AsString(),
                TaskSummaries = d["task_summaries"].AsString(),
                WorkerPrompt = i["worker_prompt"].AsString(defaults?.WorkerPrompt ?? ""),

                Agent = f["agent"].AsString(defaults?.Agent ?? ""),
                AgentShell = f["agent_shell"].AsString(defaults?.AgentShell ?? ""),
                Shell = f["shell"].AsString(defaults?.Shell ?? ""),
                Pager = c["pager"].AsString(defaults?.Pager ?? ""),
                Editor = c["editor"].AsString(defaults?.Editor ?? ""),
                Highlighter = c["highlighter"].AsString(defaults?.Highlighter ?? ""),
            };
        }

        public void CopyMetadataFrom(DaemonConfig source)
        {
            if (source == null) return;
            FactoryDefaults = source.FactoryDefaults;
            MetadataAvailable = source.MetadataAvailable;
            UsageCatalog = source.UsageCatalog;
            TemporaryRoot = source.TemporaryRoot;
            Terminal = source.Terminal;
        }

        public static List<UsageCatalogInfo> MergeUsageCatalogs(
            IEnumerable<UsageCatalogInfo> configured,
            IEnumerable<UsageCatalogInfo> live)
        {
            var merged = new Dictionary<string, UsageCatalogInfo>();
            foreach (var entry in configured ?? Enumerable.Empty<UsageCatalogInfo>())
                if (entry != null && !string.IsNullOrEmpty(entry.Key)) merged[entry.Key] = entry;
            foreach (var entry in live ?? Enumerable.Empty<UsageCatalogInfo>())
                if (entry != null && !string.IsNullOrEmpty(entry.Key) && !merged.ContainsKey(entry.Key))
                    merged[entry.Key] = entry;
            return merged.Values.OrderBy(entry => entry.Rank).ThenBy(entry => entry.Key,
                StringComparer.Ordinal).ToList();
        }

        public class UsageItemConfig
        {
            public bool Poll = true;
            // Zero is the client-side representation of an omitted TOML interval.
            public int IntervalSecs;
        }

        static Dictionary<string, UsageItemConfig> UsageItemsFromJson(JVal value)
        {
            var items = new Dictionary<string, UsageItemConfig>();
            if (!value.IsObject) return items;

            foreach (var pair in value.ObjectItems)
                items[pair.Key] = new UsageItemConfig
                {
                    Poll = pair.Value["poll"].AsBool(true),
                    IntervalSecs = pair.Value["interval_secs"].AsInt(0),
                };
            return items;
        }

        // This deliberately omits bind, token, projects, sessions, state rules and sandbox
        // presets. The daemon deep-merges this object before validating it.
        public string ToPatchJson(string baseline = null)
        {
            var before = baseline == null ? null : JVal.Parse(baseline);
            var daemon = before?["daemon"];
            return PatchObject(before,
                "daemon", PatchObject(daemon,
                    "usage_poll_secs", UsagePollSecs.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "usage_items", UsageItemsJson(daemon?["usage_items"]),
                    "claude_credentials", JVal.Q(ClaudeCredentials),
                    "openrouter_key_file", JVal.Q(OpenrouterKeyFile),
                    "openai_credentials", JVal.Q(OpenaiCredentials),
                    "agent_titles", JVal.Q(AgentTitles),
                    "title_model", JVal.Q(TitleModel),
                    "summary_prompt", JVal.Q(SummaryPrompt),
                    "title_min_chars", TitleMinChars.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "pi_titles", JVal.Q(PiTitles),
                    "task_summaries", JVal.Q(TaskSummaries),
                    "instructions", PatchObject(daemon?["instructions"],
                        "worker_prompt", JVal.Q(WorkerPrompt))),
                "defaults", PatchObject(before?["defaults"],
                    "agent", JVal.Q(Agent), "agent_shell", JVal.Q(AgentShell),
                    "shell", JVal.Q(Shell)),
                "commands", PatchObject(before?["commands"],
                    "pager", JVal.Q(Pager), "editor", JVal.Q(Editor),
                    "highlighter", JVal.Q(Highlighter)));
        }

        // Nested objects have already been reduced to changed leaves. Retain the original
        // scalar JSON so booleans, numbers and escaped strings keep their wire types.
        static string PatchObject(JVal baseline, params string[] fields)
        {
            var parts = new List<string>();
            for (int i = 0; i < fields.Length; i += 2)
            {
                string value = fields[i + 1];
                var current = JVal.Parse(value);
                if (baseline != null)
                {
                    var previous = baseline[fields[i]];
                    if (current.IsObject
                        ? !current.ObjectItems.Any()
                        : JVal.Equivalent(current, previous))
                        continue;
                }
                parts.Add(JVal.Q(fields[i]) + ":" + value);
            }
            return "{" + string.Join(",", parts.ToArray()) + "}";
        }

        string UsageItemsJson(JVal baseline)
        {
            var parts = new List<string>();
            foreach (var pair in UsageItems)
            {
                var item = pair.Value ?? new UsageItemConfig();
                // Zero is explicit here so a previously saved per-item override can be
                // cleared through the daemon's deep-merge patch.
                string interval = item.IntervalSecs > 0
                    ? item.IntervalSecs.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "0";
                string changes = PatchObject(baseline?[pair.Key],
                    "poll", JVal.B(item.Poll), "interval_secs", interval);
                if (baseline == null || changes != "{}")
                    parts.Add(JVal.Q(pair.Key) + ":" + changes);
            }
            return "{" + string.Join(",", parts.ToArray()) + "}";
        }

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
