using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld.Tests
{
    static class DaemonConfigTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("reads daemon defaults", ReadsDaemonDefaults);
            yield return ("migrates legacy experimental flag", MigratesLegacyExperimentalFlag);
            yield return ("round trips every patch field", RoundTripsEveryPatchField);
            yield return ("independent page saves preserve drafts and saved values", IndependentPageSaves);
            yield return ("patches retain nested resets and pending edits", NestedPatchChanges);
            yield return ("splits and joins line lists", SplitsAndJoinsLineLists);
            yield return ("merges refreshes around drafts and reports conflicts", DraftRefreshMerge);
            yield return ("usage inheritance stays blank across reload and discard", UsageInheritanceText);
            yield return ("reload preserves edits made after the request starts", PendingReloadEdits);
            yield return ("validates settings numeric boundaries", NumericValidation);
            yield return ("operation generations reject stale completions", OperationGenerations);
            yield return ("uses daemon factory metadata when available", UsesFactoryMetadata);
        }

        static void IndependentPageSaves()
        {
            var credentials = DaemonConfig.FromJson(JVal.Parse("{}"));
            var summaries = DaemonConfig.FromJson(JVal.Parse("{}"));
            string baseline = credentials.ToPatchJson();
            credentials.ClaudeCredentials = "/draft/credentials";
            summaries.TitleModel = "new-model";

            var summaryPatch = JVal.Parse(summaries.ToPatchJson(baseline));
            AssertEx.Equal("new-model", summaryPatch["daemon"]["title_model"].AsString(), "summary saved");
            AssertEx.True(summaryPatch["daemon"]["claude_credentials"].IsNull, "credentials untouched");
            AssertEx.Equal("/draft/credentials", credentials.ClaudeCredentials, "other draft survives");
            var credentialPatch = JVal.Parse(credentials.ToPatchJson(baseline));
            AssertEx.Equal("/draft/credentials", credentialPatch["daemon"]["claude_credentials"].AsString(), "draft saved later");
            AssertEx.True(credentialPatch["daemon"]["title_model"].IsNull, "later save cannot restore stale model");
            AssertEx.Equal("{}", summaries.ToPatchJson(summaries.ToPatchJson()), "saved page has no changes");
        }

        static void NestedPatchChanges()
        {
            var config = DaemonConfig.FromJson(JVal.Parse("{}"));
            config.ExperimentalBreadcrumbs = true;
            config.ExperimentalInstructions = true;
            config.UsageItems["test"] = new DaemonConfig.UsageItemConfig { Poll = true, IntervalSecs = 90 };
            string baseline = config.ToPatchJson();
            config.ExperimentalBreadcrumbs = false;
            config.ExperimentalInstructions = false;
            config.UsageItems["test"].IntervalSecs = 0;
            config.InstructionsBreadcrumbEnabled = false;
            config.InstructionsTemplate = "a quoted \"draft\"\nnext line";
            var patch = JVal.Parse(config.ToPatchJson(baseline));
            AssertEx.True(!patch["daemon"]["experimental_breadcrumbs"].IsNull,
                           "breadcrumb feature false reset included");
            AssertEx.Equal(false, patch["daemon"]["experimental_breadcrumbs"].AsBool(true),
                           "breadcrumb feature false reset preserved");
            AssertEx.True(!patch["daemon"]["experimental_instructions"].IsNull,
                           "instruction feature false reset included");
            AssertEx.Equal(false, patch["daemon"]["experimental_instructions"].AsBool(true),
                           "instruction feature false reset preserved");
            AssertEx.Equal(0, patch["daemon"]["usage_items"]["test"]["interval_secs"].AsInt(-1), "zero reset preserved");
            AssertEx.True(patch["daemon"]["usage_items"]["test"]["poll"].IsNull, "unchanged nested sibling omitted");
            AssertEx.Equal(config.InstructionsTemplate, patch["daemon"]["instructions"]["template"].AsString(), "escaped text preserved");
            AssertEx.True(patch["daemon"]["instructions"]["mount_path"].IsNull, "unchanged instruction omitted");
            AssertEx.True(patch["commands"].IsNull, "unchanged section omitted");

            string submitted = config.ToPatchJson();
            config.Editor = "new-editor";
            patch = JVal.Parse(config.ToPatchJson(submitted));
            AssertEx.Equal("new-editor", patch["commands"]["editor"].AsString(), "edit during save remains pending");
            AssertEx.True(patch["daemon"].IsNull, "submitted changes are clean");
        }

        static void ReadsDaemonDefaults()
        {
            var config = DaemonConfig.FromJson(JVal.Parse("{}"));
            AssertEx.False(config.MetadataAvailable, "missing daemon metadata is explicit");
            AssertEx.True(config.FactoryDefaults == null, "missing factory metadata has no reset copy");
            AssertEx.Equal(false, config.ExperimentalBreadcrumbs, "breadcrumb feature defaults off");
            AssertEx.Equal(false, config.ExperimentalInstructions, "instruction feature defaults off");

            AssertEx.Equal(0, config.UsagePollSecs, "usage poll is unavailable");
            AssertEx.Equal(0, config.UsageItems.Count, "usage item defaults");
            AssertEx.Equal("", config.ClaudeCredentials, "Claude credentials unavailable");
            AssertEx.Equal("", config.OpenrouterKeyFile, "OpenRouter key default");
            AssertEx.Equal("", config.OpenaiCredentials, "OpenAI credentials unavailable");
            AssertEx.Equal("", config.AgentTitles, "agent title unavailable");
            AssertEx.Equal("", config.TitleModel, "title model unavailable");
            AssertEx.Equal("", config.SummaryPrompt, "summary prompt unavailable");
            AssertEx.Equal(0, config.TitleMinChars, "title minimum unavailable");
            AssertEx.Equal("", config.PiTitles, "Pi title unavailable");
            AssertEx.Equal("", config.InstructionsTemplate, "instructions template unavailable");
            AssertEx.Equal("", config.InstructionsMountPath, "instructions mount unavailable");
            AssertEx.Equal("", config.InstructionsBreadcrumb, "breadcrumb unavailable");
            AssertEx.False(config.InstructionsBreadcrumbEnabled, "breadcrumb unavailable");
            AssertEx.Equal("", config.WorkerPrompt, "worker prompt unavailable");
            AssertEx.Equal("", config.Agent, "agent command unavailable");
            AssertEx.Equal("", config.AgentShell, "agent shell unavailable");
            AssertEx.Equal("", config.Shell, "shell command unavailable");
            AssertEx.Equal("", config.Pager, "pager unavailable");
            AssertEx.Equal("", config.Editor, "editor unavailable");
            AssertEx.Equal("", config.Highlighter, "highlighter unavailable");
        }

        static void UsesFactoryMetadata()
        {
            var config = DaemonConfig.FromJson(JVal.Parse("{}"), JVal.Parse(
                "{\"defaults\":{\"daemon\":{\"usage_poll_secs\":17,\"title_model\":\"daemon/model\"}," +
                "\"defaults\":{\"agent\":\"daemon-agent\"},\"commands\":{}}," +
                "\"usage_catalog\":[{\"key\":\"custom\",\"label\":\"Custom\",\"provider\":\"x\",\"unit\":\"usd\",\"rank\":9,\"default_poll\":false}]," +
                "\"temporary_root\":\"/daemon/tmp\",\"terminal\":{\"scrollback_lines\":12,\"min_cols\":21,\"max_cols\":301,\"min_rows\":6,\"max_rows\":101}}"));
            AssertEx.True(config.MetadataAvailable, "metadata available");
            AssertEx.Equal(17, config.FactoryDefaults.UsagePollSecs, "daemon poll factory");
            AssertEx.Equal("daemon/model", config.FactoryDefaults.TitleModel, "daemon title factory");
            AssertEx.Equal("daemon-agent", config.FactoryDefaults.Agent, "command factory");
            AssertEx.Equal("/daemon/tmp", config.TemporaryRoot, "temp root metadata");
            AssertEx.Equal(12, config.Terminal.ScrollbackLines, "terminal metadata");
            AssertEx.Equal(1, config.UsageCatalog.Count, "usage catalog metadata");
            AssertEx.False(config.UsageCatalog[0].DefaultPoll, "catalog poll policy");
        }

        static void MigratesLegacyExperimentalFlag()
        {
            var config = DaemonConfig.FromJson(JVal.Parse(
                "{\"daemon\":{\"experimental\":true}}"));
            AssertEx.True(config.ExperimentalBreadcrumbs, "legacy flag enables breadcrumbs");
            AssertEx.True(config.ExperimentalInstructions, "legacy flag enables instructions");
        }

        static void RoundTripsEveryPatchField()
        {
            var expected = new DaemonConfig
            {
                ExperimentalBreadcrumbs = true,
                ExperimentalInstructions = true,
                UsagePollSecs = 17,
                UsageItems = new Dictionary<string, DaemonConfig.UsageItemConfig>
                {
                    ["claude_session"] = new DaemonConfig.UsageItemConfig
                    {
                        Poll = false,
                        IntervalSecs = 15,
                    },
                    ["openrouter_balance"] = new DaemonConfig.UsageItemConfig
                    {
                        Poll = true,
                        IntervalSecs = 0,
                    },
                },
                ClaudeCredentials = "~/.config/claude \"credentials\"",
                OpenrouterKeyFile = "/run/user/1000/openrouter.key",
                OpenaiCredentials = "~/.config/codex/auth.json",
                AgentTitles = "once",
                TitleModel = "provider/model:flash",
                SummaryPrompt = "Name this request in five words.",
                TitleMinChars = 42,
                PiTitles = "never",
                InstructionsTemplate = "# {{ project }}\n\n{{ runtime_context }}",
                InstructionsMountPath = "docs/SLOPWORLD.md",
                InstructionsBreadcrumb = "Read {{ mount_path }} for {{ project }}",
                InstructionsBreadcrumbEnabled = false,
                WorkerPrompt = "Retrieve $SLOPWORLD_TASK_ID, accept it, and finish it.",
                Agent = "codex --full-auto",
                AgentShell = "zsh",
                Shell = "bash -lc",
                Pager = "less -R",
                Editor = "micro --no-help",
                Highlighter = "highlight --out-format=xterm256",
            };
            var actual = DaemonConfig.FromJson(JVal.Parse(expected.ToPatchJson()));
            AssertEx.Equal(expected.ExperimentalBreadcrumbs, actual.ExperimentalBreadcrumbs,
                           "breadcrumb feature round trip");
            AssertEx.Equal(expected.ExperimentalInstructions, actual.ExperimentalInstructions,
                           "instruction feature round trip");

            AssertEx.Equal(expected.UsagePollSecs, actual.UsagePollSecs, "poll round trip");
            AssertEx.False(actual.UsageItems["claude_session"].Poll,
                           "usage item poll round trip");
            AssertEx.Equal(15, actual.UsageItems["claude_session"].IntervalSecs,
                           "usage item interval round trip");
            AssertEx.Equal(0, actual.UsageItems["openrouter_balance"].IntervalSecs,
                           "usage item inherited interval round trip");
            AssertEx.Equal(expected.ClaudeCredentials, actual.ClaudeCredentials,
                           "Claude credentials round trip");
            AssertEx.Equal(expected.OpenrouterKeyFile, actual.OpenrouterKeyFile,
                           "OpenRouter key round trip");
            AssertEx.Equal(expected.OpenaiCredentials, actual.OpenaiCredentials,
                           "OpenAI credentials round trip");
            AssertEx.Equal(expected.AgentTitles, actual.AgentTitles,
                           "agent titles round trip");
            AssertEx.Equal(expected.TitleModel, actual.TitleModel, "title model round trip");
            AssertEx.Equal(expected.SummaryPrompt, actual.SummaryPrompt,
                           "summary prompt round trip");
            AssertEx.Equal(expected.TitleMinChars, actual.TitleMinChars,
                           "title minimum prompt length round trip");
            AssertEx.Equal(expected.PiTitles, actual.PiTitles, "Pi titles round trip");
            AssertEx.Equal(expected.InstructionsTemplate, actual.InstructionsTemplate,
                           "instructions template round trip");
            AssertEx.Equal(expected.InstructionsMountPath, actual.InstructionsMountPath,
                           "instructions mount path round trip");
            AssertEx.Equal(expected.InstructionsBreadcrumb, actual.InstructionsBreadcrumb,
                           "instructions breadcrumb text round trip");
            AssertEx.Equal(expected.InstructionsBreadcrumbEnabled,
                           actual.InstructionsBreadcrumbEnabled,
                           "instructions breadcrumb round trip");
            AssertEx.Equal(expected.WorkerPrompt, actual.WorkerPrompt,
                           "worker prompt round trip");
            AssertEx.Equal(expected.Agent, actual.Agent, "agent round trip");
            AssertEx.Equal(expected.AgentShell, actual.AgentShell, "agent shell round trip");
            AssertEx.Equal(expected.Shell, actual.Shell, "shell round trip");
            AssertEx.Equal(expected.Pager, actual.Pager, "pager round trip");
            AssertEx.Equal(expected.Editor, actual.Editor, "editor round trip");
            AssertEx.Equal(expected.Highlighter, actual.Highlighter,
                           "highlighter round trip");
        }

        static void SplitsAndJoinsLineLists()
        {
            var lines = new List<string> { "first", "second", "third" };

            AssertEx.Equal("first\nsecond\nthird", DaemonConfig.Lines(lines), "line join");
            AssertEx.Sequence(lines, DaemonConfig.Split(" first \n\nsecond\n third \n"),
                              "line split and trim");
            AssertEx.True(!DaemonConfig.Split(null).Any(), "null line list");
        }

        static void DraftRefreshMerge()
        {
            var first = new DaemonConfig
            {
                TitleModel = "first-model",
                InstructionsTemplate = "base template",
            };
            var draft = new DaemonConfigDraft();
            draft.LoadServer(first);
            draft.MarkCurrentClean();
            draft.Config.InstructionsTemplate = "local draft";

            var refreshed = new DaemonConfig
            {
                TitleModel = "server model",
                InstructionsTemplate = "base template",
            };
            draft.LoadServer(refreshed);
            AssertEx.Equal("local draft", draft.Config.InstructionsTemplate,
                           "dirty template survives refresh");
            AssertEx.Equal("server model", draft.Config.TitleModel,
                           "untouched field follows refresh");
            AssertEx.True(!draft.HasConflicts, "unchanged draft field has no conflict");

            var conflicting = new DaemonConfig
            {
                TitleModel = "new server model",
                InstructionsTemplate = "external template",
            };
            draft.LoadServer(conflicting);
            AssertEx.Equal("local draft", draft.Config.InstructionsTemplate,
                           "conflicting draft is not overwritten");
            AssertEx.True(draft.HasConflicts, "same-field conflict is surfaced");
            AssertEx.True(draft.ConflictMessage.Contains("instructions.template"),
                           "conflict names the field");

            draft.ResetToBaseline();
            AssertEx.Equal("external template", draft.Config.InstructionsTemplate,
                           "discard uses latest server value");
            AssertEx.False(draft.IsDirty, "discard clears the draft");

            var raw = new DaemonConfigDraft();
            raw.LoadServer(new DaemonConfig { TitleMinChars = 20 });
            raw.MarkCurrentClean();
            AssertEx.Equal("20", raw.Text("title", "daemon.title_min_chars", "20"),
                           "raw field starts with server text");
            raw.SetText("title", "daemon.title_min_chars", "2");
            raw.LoadServer(new DaemonConfig { TitleMinChars = 30 });
            AssertEx.Equal("2", raw.Text("title", "daemon.title_min_chars", "30"),
                           "partial raw field survives refresh");
            AssertEx.True(raw.IsDirty, "partial raw field remains dirty");
            raw.SetText("title", "daemon.title_min_chars", "030");
            raw.QueueNormalization("title", "daemon.title_min_chars", "30");
            raw.Acknowledge(raw.Config.ToPatchJson(), raw.TextSnapshot());
            raw.ApplyQueuedNormalizations();
            AssertEx.Equal("30", raw.Text("title", "daemon.title_min_chars", "30"),
                           "accepted raw field normalizes after acknowledgement");
            AssertEx.False(raw.IsDirty, "normalization keeps the acknowledged field clean");
            raw.SetText("title", "daemon.title_min_chars", "31");
            raw.QueueNormalization("title", "daemon.title_min_chars", "30");
            raw.Acknowledge(raw.Config.ToPatchJson(), raw.TextSnapshot());
            raw.SetText("title", "daemon.title_min_chars", "32");
            raw.ApplyQueuedNormalizations();
            AssertEx.Equal("32", raw.Text("title", "daemon.title_min_chars", "30"),
                           "edited raw field is not normalized over a newer edit");
            raw.ResetToBaseline();
            AssertEx.Equal("30", raw.Text("title", "daemon.title_min_chars", "30"),
                           "discard replaces raw field with latest server text");
            AssertEx.False(raw.IsDirty, "raw discard clears dirty state");
        }

        static void UsageInheritanceText()
        {
            const string key = "usage.item.claude_session";
            const string path = "daemon.usage_items.claude_session.interval_secs";
            var server = new DaemonConfig();
            server.UsageItems["claude_session"] = new DaemonConfig.UsageItemConfig
            {
                Poll = true,
                IntervalSecs = 0,
            };
            var draft = new DaemonConfigDraft();
            draft.LoadServer(server);
            draft.Text(key, path, "", zeroMeansBlank: true);
            draft.LoadServer(DaemonConfig.FromJson(JVal.Parse(server.ToPatchJson())));
            AssertEx.Equal("", draft.Text(key, path, ""), "reload preserves inheritance");
            AssertEx.False(draft.IsDirty, "inherited interval remains clean");
            draft.SetText(key, path, "60");
            draft.ResetToBaseline();
            AssertEx.Equal("", draft.Text(key, path, ""), "discard restores inheritance");
            AssertEx.False(draft.IsDirty, "discarded interval remains clean");

            server.UsageItems["claude_session"].IntervalSecs = 90;
            draft.LoadServer(server);
            AssertEx.Equal("90", draft.Text(key, path, ""), "explicit interval stays numeric");
            draft.Text("minimum", "daemon.title_min_chars", "0");
            server.TitleMinChars = 0;
            draft.LoadServer(server);
            AssertEx.Equal("0", draft.Text("minimum", "daemon.title_min_chars", "0"),
                           "ordinary numeric zero stays visible");
        }

        static void PendingReloadEdits()
        {
            var draft = new DaemonConfigDraft();
            draft.LoadServer(new DaemonConfig { InstructionsTemplate = "server" });
            AssertEx.False(draft.IsDirty, "reload starts with a clean form");
            draft.BeginOperation();
            draft.Config.InstructionsTemplate = "typed while loading";
            draft.LoadServer(new DaemonConfig { InstructionsTemplate = "server" });
            AssertEx.True(draft.IsDirty, "merged draft requires saving before page defaults");
            AssertEx.True(draft.Config.ToPatchJson(draft.BaselineJson)
                .Contains("typed while loading"), "pending edit remains in save patch");
            draft.LoadServer(new DaemonConfig { InstructionsTemplate = "server" });
            AssertEx.Equal("typed while loading", draft.Config.InstructionsTemplate,
                           "another reload retains the unsaved edit");
        }

        static void NumericValidation()
        {
            int value;
            string error;
            AssertEx.True(DaemonConfigValidation.WholeSeconds("10", false, out value, out error),
                           "minimum poll interval accepted");
            AssertEx.Equal(10, value, "minimum poll value");
            AssertEx.True(DaemonConfigValidation.WholeSeconds("3600", false, out value, out error),
                           "maximum poll interval accepted");
            AssertEx.False(DaemonConfigValidation.WholeSeconds("9", false, out value, out error),
                           "below-range poll rejected");
            AssertEx.False(DaemonConfigValidation.WholeSeconds("3601", false, out value, out error),
                           "above-range poll rejected");
            AssertEx.False(DaemonConfigValidation.WholeSeconds("1.5", false, out value, out error),
                           "fractional poll rejected");
            AssertEx.True(DaemonConfigValidation.WholeSeconds("", true, out value, out error),
                           "blank row inherits");
            AssertEx.False(DaemonConfigValidation.WholeSeconds("", false, out value, out error),
                           "blank global poll rejected");
            AssertEx.True(DaemonConfigValidation.TitleMinimum("0", out value, out error),
                           "minimum title length accepted");
            AssertEx.True(DaemonConfigValidation.TitleMinimum("2000", out value, out error),
                           "maximum title length accepted");
            AssertEx.False(DaemonConfigValidation.TitleMinimum("2001", out value, out error),
                           "above-range title length rejected");
            AssertEx.False(DaemonConfigValidation.TitleMinimum("", out value, out error),
                           "blank title length rejected");
        }

        static void OperationGenerations()
        {
            var draft = new DaemonConfigDraft();
            int first = draft.BeginOperation();
            int second = draft.BeginOperation();
            AssertEx.False(draft.IsCurrent(first), "older operation rejected");
            AssertEx.True(draft.IsCurrent(second), "new operation accepted");
        }
    }
}
