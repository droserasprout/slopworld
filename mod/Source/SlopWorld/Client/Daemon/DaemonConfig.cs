using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    public sealed class DaemonConfigDefaults
    {
        // Compatibility values used only when an older daemon omits metadata. Current
        // connections replace these with the daemon's factory read model.
        public int UsagePollSecs = 60;
        public string ClaudeCredentials = "~/.claude/.credentials.json";
        public string OpenaiCredentials = "~/.codex/auth.json";
        public string TitleModel = "google/gemini-3.1-flash-lite";
        public string SummaryPrompt = "Summarise this coding request in at most 6 words for a session title. Reply with only the title, without quotes, punctuation, or commentary.";
        public int TitleMinChars = 20;
        public string InstructionsTemplate = "# SlopWorld agent context\n\nRead the project's `README.md` and any applicable `AGENTS.md` files for project instructions. This generated `{{ file }}` is mounted at `{{ mount_path }}` and is runtime context, not a replacement for them.\n\n{{ runtime_context }}\n";
        public string InstructionsMountPath = "SLOPWORLD.md";
        public string InstructionsBreadcrumb = "Read `{{ mount_path }}` for SlopWorld runtime context. It is a generated snapshot, not project instructions. When delegating, send work once and use `slopctl wait ID` for the result; do not poll `task`, `inbox`, or `status`.";
        public bool InstructionsBreadcrumbEnabled = true;
        public string WorkerPrompt = "You are a SlopWorld worker. Your assigned task ID is $SLOPWORLD_TASK_ID. Run `slopctl task \"$SLOPWORLD_TASK_ID\"` once, then `slopctl accept \"$SLOPWORLD_TASK_ID\"`. Use `slopctl progress \"$SLOPWORLD_TASK_ID\" \"note\"` while working and conclude with `slopctl finish \"$SLOPWORLD_TASK_ID\" \"result\"` or `slopctl fail \"$SLOPWORLD_TASK_ID\" \"reason\"`. Do not search the inbox or poll task status.\n\nWorker task: use `$SLOPWORLD_TASK_ID` with `slopctl task`, then `accept`, `progress`, and finally `finish` or `fail`. Do not search the inbox or poll task status.";
        public string Agent = "claude";
        public string AgentShell = "bash";
        public string Shell = "bash";
        public string Pager = "less";
        public string Editor = "micro";
        public string Highlighter = "highlight --out-format=xterm256";

        public static DaemonConfigDefaults FromJson(JVal root)
        {
            var result = new DaemonConfigDefaults();
            if (root == null || root.IsNull || !root.IsObject) return result;
            var d = root["daemon"];
            var f = root["defaults"];
            var c = root["commands"];
            var i = d["instructions"];
            result.UsagePollSecs = d["usage_poll_secs"].AsInt(result.UsagePollSecs);
            result.ClaudeCredentials = d["claude_credentials"].AsString(result.ClaudeCredentials);
            result.OpenaiCredentials = d["openai_credentials"].AsString(result.OpenaiCredentials);
            result.TitleModel = d["title_model"].AsString(result.TitleModel);
            result.SummaryPrompt = d["summary_prompt"].AsString(result.SummaryPrompt);
            result.TitleMinChars = d["title_min_chars"].AsInt(result.TitleMinChars);
            result.InstructionsTemplate = i["template"].AsString(result.InstructionsTemplate);
            result.InstructionsMountPath = i["mount_path"].AsString(result.InstructionsMountPath);
            result.InstructionsBreadcrumb = i["breadcrumb"].AsString(result.InstructionsBreadcrumb);
            result.InstructionsBreadcrumbEnabled = i["breadcrumb_enabled"].AsBool(result.InstructionsBreadcrumbEnabled);
            result.WorkerPrompt = i["worker_prompt"].AsString(result.WorkerPrompt);
            result.Agent = f["agent"].AsString(result.Agent);
            result.AgentShell = f["agent_shell"].AsString(result.AgentShell);
            result.Shell = f["shell"].AsString(result.Shell);
            result.Pager = c["pager"].AsString(result.Pager);
            result.Editor = c["editor"].AsString(result.Editor);
            result.Highlighter = c["highlighter"].AsString(result.Highlighter);
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
        public int ScrollbackLines = 10000;
        public int MinCols = 20;
        public int MaxCols = 500;
        public int MinRows = 5;
        public int MaxRows = 200;

        public static TerminalLimits FromJson(JVal j)
        {
            var result = new TerminalLimits();
            if (j == null || j.IsNull || !j.IsObject) return result;
            result.ScrollbackLines = j["scrollback_lines"].AsInt(result.ScrollbackLines);
            result.MinCols = j["min_cols"].AsInt(result.MinCols);
            result.MaxCols = j["max_cols"].AsInt(result.MaxCols);
            result.MinRows = j["min_rows"].AsInt(result.MinRows);
            result.MaxRows = j["max_rows"].AsInt(result.MaxRows);
            return result;
        }
    }

    // The daemon owns the file and hands these over parsed, so nothing in the mod has
    // to speak TOML. This is a read model for the settings pages, not a second schema for
    // the whole daemon config. Writes are partial patches, so fields not represented here
    // remain untouched on the server.
    public class DaemonConfig
    {
        public DaemonConfigDefaults FactoryDefaults = new DaemonConfigDefaults();
        public bool MetadataAvailable;
        public List<UsageCatalogInfo> UsageCatalog = new List<UsageCatalogInfo>();
        public string TemporaryRoot = "/tmp/slopworld";
        public TerminalLimits Terminal = new TerminalLimits();

        public int UsagePollSecs = 60;
        // One entry per usage window. A zero interval means the global interval applies.
        public Dictionary<string, UsageItemConfig> UsageItems =
            new Dictionary<string, UsageItemConfig>();
        // Where the daemon looks for Claude Code's OAuth token.
        public string ClaudeCredentials = "~/.claude/.credentials.json";
        // Blank means the daemon reads OPENROUTER_API_KEY out of its own environment.
        public string OpenrouterKeyFile = "";
        // Codex's ChatGPT login carries the token the usage endpoint needs. Unlike a key,
        // it is a live login file that the daemon reads fresh and never sends to the mod.
        public string OpenaiCredentials = "~/.codex/auth.json";
        // Agent prompt-derived titles use a separate OpenRouter request.
        public string AgentTitles = "never";
        public string TitleModel = "google/gemini-3.1-flash-lite";
        public string SummaryPrompt = "Summarise this coding request in at most 6 words for a session title. Reply with only the title, without quotes, punctuation, or commentary.";
        public int TitleMinChars = 20;
        // Pi titles are generated by slopd before input reaches the sandbox.
        public string PiTitles = "always";
        // Task summaries are generated once for each durable delegated task.
        public string TaskSummaries = "never";
        public bool ExperimentalBreadcrumbs;
        public bool ExperimentalInstructions;
        // The generated project-root runtime manifest. The body is a Markdown template, with
        // {{ runtime_context }} expanding to the daemon's live snapshot.
        public string InstructionsTemplate = "# SlopWorld agent context\n\nRead the project's `README.md` and any applicable `AGENTS.md` files for project instructions. This generated `{{ file }}` is mounted at `{{ mount_path }}` and is runtime context, not a replacement for them.\n\n{{ runtime_context }}\n";
        public string InstructionsMountPath = "SLOPWORLD.md";
        public string InstructionsBreadcrumb = "Read `{{ mount_path }}` for SlopWorld runtime context. It is a generated snapshot, not project instructions. When delegating, send work once and use `slopctl wait ID` for the result; do not poll `task`, `inbox`, or `status`.";
        public bool InstructionsBreadcrumbEnabled = true;
        public string WorkerPrompt = "You are a SlopWorld worker. Your assigned task ID is $SLOPWORLD_TASK_ID. Run `slopctl task \"$SLOPWORLD_TASK_ID\"` once, then `slopctl accept \"$SLOPWORLD_TASK_ID\"`. Use `slopctl progress \"$SLOPWORLD_TASK_ID\" \"note\"` while working and conclude with `slopctl finish \"$SLOPWORLD_TASK_ID\" \"result\"` or `slopctl fail \"$SLOPWORLD_TASK_ID\" \"reason\"`. Do not search the inbox or poll task status.\n\nWorker task: use `$SLOPWORLD_TASK_ID` with `slopctl task`, then `accept`, `progress`, and finally `finish` or `fail`. Do not search the inbox or poll task status.";

        // Agent and Shell name command presets. AgentShell is the shell advertised inside
        // sandboxed agent sessions; it is separate from the shell errand preset.
        public string Agent = "claude";
        public string AgentShell = "bash";
        public string Shell = "bash";
        public string Pager = "less";
        public string Editor = "micro";
        public string Highlighter = "highlight --out-format=xterm256";

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
                MetadataAvailable = metadata != null && !metadata.IsNull && metadata.IsObject,
                UsageCatalog = metadata?["usage_catalog"].Items.Select(UsageCatalogInfo.FromJson).ToList()
                    ?? new List<UsageCatalogInfo>(),
                TemporaryRoot = metadata?["temporary_root"].AsString("/tmp/slopworld") ?? "/tmp/slopworld",
                Terminal = TerminalLimits.FromJson(metadata?["terminal"]),
                ExperimentalBreadcrumbs = d["experimental_breadcrumbs"].AsBool(
                    d["experimental"].AsBool(false)),
                ExperimentalInstructions = d["experimental_instructions"].AsBool(
                    d["experimental"].AsBool(false)),
                UsagePollSecs = d["usage_poll_secs"].AsInt(defaults.UsagePollSecs),
                UsageItems = UsageItemsFromJson(d["usage_items"]),
                ClaudeCredentials =
                    d["claude_credentials"].AsString(defaults.ClaudeCredentials),
                OpenrouterKeyFile = d["openrouter_key_file"].AsString(),
                OpenaiCredentials = d["openai_credentials"].AsString(defaults.OpenaiCredentials),
                AgentTitles = d["agent_titles"].AsString("never"),
                TitleModel = d["title_model"].AsString(defaults.TitleModel),
                SummaryPrompt = d["summary_prompt"].AsString(defaults.SummaryPrompt),
                TitleMinChars = d["title_min_chars"].AsInt(defaults.TitleMinChars),
                PiTitles = d["pi_titles"].AsString("always"),
                TaskSummaries = d["task_summaries"].AsString("never"),
                InstructionsTemplate = i["template"].AsString(defaults.InstructionsTemplate),
                InstructionsMountPath = i["mount_path"].AsString(defaults.InstructionsMountPath),
                InstructionsBreadcrumb =
                    i["breadcrumb"].AsString(defaults.InstructionsBreadcrumb),
                InstructionsBreadcrumbEnabled = i["breadcrumb_enabled"].AsBool(defaults.InstructionsBreadcrumbEnabled),
                WorkerPrompt = i["worker_prompt"].AsString(defaults.WorkerPrompt),

                Agent = f["agent"].AsString(defaults.Agent),
                AgentShell = f["agent_shell"].AsString(defaults.AgentShell),
                Shell = f["shell"].AsString(defaults.Shell),
                Pager = c["pager"].AsString(defaults.Pager),
                Editor = c["editor"].AsString(defaults.Editor),
                Highlighter = c["highlighter"].AsString(defaults.Highlighter),
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
                    "experimental_breadcrumbs", JVal.B(ExperimentalBreadcrumbs),
                    "experimental_instructions", JVal.B(ExperimentalInstructions),
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
                        "template", JVal.Q(InstructionsTemplate),
                        "mount_path", JVal.Q(InstructionsMountPath),
                        "breadcrumb", JVal.Q(InstructionsBreadcrumb),
                        "breadcrumb_enabled", JVal.B(InstructionsBreadcrumbEnabled),
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
