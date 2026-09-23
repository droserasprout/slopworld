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

        public static DaemonConfigDefaults FromWire(Wire.Config root)
        {
            if (root == null) return null;
            var result = new DaemonConfigDefaults();
            var d = root.Daemon ?? new Wire.Daemon();
            var f = root.Defaults ?? new Wire.Defaults();
            var c = root.Commands ?? new Wire.CommandDefaults();
            var i = d.Instructions ?? new Wire.Instructions();
            result.UsagePollSecs = (int)d.UsagePollSecs;
            result.ClaudeCredentials = d.ClaudeCredentials;
            result.OpenaiCredentials = d.OpenaiCredentials;
            result.TitleModel = d.TitleModel;
            result.SummaryPrompt = d.SummaryPrompt;
            result.TitleMinChars = (int)d.TitleMinChars;
            result.WorkerPrompt = i.WorkerPrompt;
            result.Agent = f.Agent;
            result.AgentShell = f.AgentShell;
            result.Shell = f.Shell;
            result.Pager = c.Pager;
            result.Editor = c.Editor;
            result.Highlighter = c.Highlighter;
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

        public static UsageCatalogInfo FromWire(Wire.UsageCatalogEntry j) => new UsageCatalogInfo
        {
            Key = j.Key,
            Label = j.Label,
            Provider = j.Provider,
            Unit = j.Unit,
            Rank = (int)j.Rank,
            DefaultPoll = j.DefaultPoll,
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

        public static TerminalLimits FromWire(Wire.TerminalCapabilities j)
        {
            var result = new TerminalLimits();
            if (j == null) return result;
            int scrollback = (int)j.ScrollbackLines;
            result.ScrollbackLines = Math.Max(1, Math.Min(ClientMaxScrollbackLines, scrollback));

            int minCols = (int)j.MinCols;
            int maxCols = (int)j.MaxCols;
            if (minCols < 1 || maxCols < minCols)
            {
                minCols = result.MinCols;
                maxCols = result.MaxCols;
            }
            result.MinCols = Math.Max(ClientMinCols, Math.Min(ClientMaxCols, minCols));
            result.MaxCols = Math.Max(result.MinCols,
                Math.Min(ClientMaxCols, maxCols));

            int minRows = (int)j.MinRows;
            int maxRows = (int)j.MaxRows;
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
        // Settings > Agents > Workers stores the selected qualified template identities.
        // The daemon uses them as policy, not as part of templates or saved agent settings.
        public List<string> WorkerTemplates = new List<string>();

        // Agent and Shell name command presets. AgentShell is the shell advertised inside
        // sandboxed agent sessions. It is separate from the shell errand preset.
        public string Agent = "";
        public string AgentShell = "";
        public string Shell = "";
        public string Pager = "";
        public string Editor = "";
        public string Highlighter = "";

        public static DaemonConfig FromWire(Wire.Config v, Wire.ConfigMetadata metadata = null) =>
            Read(v.Daemon, v.Defaults, v.Commands, metadata);
        public static DaemonConfig FromSnapshot(Wire.EditableConfig v) => Read(v.Daemon, v.Defaults, v.Commands, null);
        static DaemonConfig Read(Wire.Daemon d, Wire.Defaults f, Wire.CommandDefaults c, Wire.ConfigMetadata metadata)
        {
            var defaults = DaemonConfigDefaults.FromWire(metadata?.Defaults);
            d = d ?? new Wire.Daemon(); f = f ?? new Wire.Defaults(); c = c ?? new Wire.CommandDefaults();
            var i = d.Instructions ?? new Wire.Instructions();
            return new DaemonConfig
            {
                FactoryDefaults = defaults,
                MetadataAvailable = metadata != null && defaults != null,
                UsageCatalog = metadata?.UsageCatalog.Select(UsageCatalogInfo.FromWire).ToList()
                    ?? new List<UsageCatalogInfo>(),
                TemporaryRoot = metadata?.TemporaryRoot ?? "",
                Terminal = TerminalLimits.FromWire(metadata?.Terminal),
                UsagePollSecs = (int)d.UsagePollSecs,
                UsageItems = d.UsageItems.ToDictionary(p => p.Key, p => new UsageItemConfig { Poll = p.Value.Poll, IntervalSecs = (int)p.Value.IntervalSecs }),
                ClaudeCredentials =
                    d.ClaudeCredentials,
                OpenrouterKeyFile = d.OpenrouterKeyFile,
                OpenaiCredentials = d.OpenaiCredentials,
                AgentTitles = d.AgentTitles,
                TitleModel = d.TitleModel,
                SummaryPrompt = d.SummaryPrompt,
                TitleMinChars = (int)d.TitleMinChars,
                PiTitles = d.PiTitles,
                TaskSummaries = d.TaskSummaries,
                WorkerPrompt = i.WorkerPrompt,
                WorkerTemplates = d.WorkerTemplates.ToList(),

                Agent = f.Agent,
                AgentShell = f.AgentShell,
                Shell = f.Shell,
                Pager = c.Pager,
                Editor = c.Editor,
                Highlighter = c.Highlighter,
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

        public Wire.EditableConfig Snapshot()
        {
            var daemon = new Wire.Daemon
            {
                UsagePollSecs = checked((ulong)UsagePollSecs),
                ClaudeCredentials = ClaudeCredentials,
                OpenrouterKeyFile = OpenrouterKeyFile,
                OpenaiCredentials = OpenaiCredentials,
                AgentTitles = AgentTitles,
                TitleModel = TitleModel,
                SummaryPrompt = SummaryPrompt,
                TitleMinChars = checked((ulong)TitleMinChars),
                PiTitles = PiTitles,
                TaskSummaries = TaskSummaries,
                WorkerTemplates = { WorkerTemplates.Distinct().OrderBy(v => v, StringComparer.Ordinal) },
                Instructions = new Wire.Instructions { WorkerPrompt = WorkerPrompt },
            };
            foreach (var pair in UsageItems) daemon.UsageItems[pair.Key] = new Wire.UsageItem
            {
                Poll = pair.Value.Poll,
                IntervalSecs = checked((ulong)Math.Max(0, pair.Value.IntervalSecs))
            };
            return new Wire.EditableConfig
            {
                Daemon = daemon,
                Defaults = new Wire.Defaults { Agent = Agent, AgentShell = AgentShell, Shell = Shell },
                Commands = new Wire.CommandDefaults { Pager = Pager, Editor = Editor, Highlighter = Highlighter }
            };
        }
        public Wire.ConfigPatch ToPatch(Wire.EditableConfig baseline = null)
        {
            var snapshot = Snapshot();
            return new Wire.ConfigPatch
            {
                Values = snapshot,
                Paths = { ProtoFields.Changes(snapshot, baseline).Keys.OrderBy(v => v, StringComparer.Ordinal) }
            };
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
