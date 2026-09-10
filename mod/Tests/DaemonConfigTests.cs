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
            yield return ("renders instructions breadcrumb variables", RendersInstructionsBreadcrumb);
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
            AssertEx.Equal(false, config.ExperimentalBreadcrumbs, "breadcrumb feature defaults off");
            AssertEx.Equal(false, config.ExperimentalInstructions, "instruction feature defaults off");

            AssertEx.Equal(60, config.UsagePollSecs, "usage poll default");
            AssertEx.Equal(0, config.UsageItems.Count, "usage item defaults");
            AssertEx.Equal("~/.claude/.credentials.json", config.ClaudeCredentials,
                           "Claude credentials default");
            AssertEx.Equal("", config.OpenrouterKeyFile, "OpenRouter key default");
            AssertEx.Equal("~/.codex/auth.json", config.OpenaiCredentials,
                           "OpenAI credentials default");
            AssertEx.Equal("never", config.AgentTitles, "agent title default");
            AssertEx.Equal("google/gemini-3.1-flash-lite", config.TitleModel,
                           "title model default");
            AssertEx.Equal(DaemonConfig.DefaultSummaryPrompt, config.SummaryPrompt,
                           "summary prompt default");
            AssertEx.Equal(20, config.TitleMinChars, "title minimum prompt length default");
            AssertEx.Equal("always", config.PiTitles, "Pi title default");
            AssertEx.Equal(DaemonConfig.DefaultInstructionsTemplate, config.InstructionsTemplate,
                           "instructions template default");
            AssertEx.Equal("SLOPWORLD.md", config.InstructionsMountPath,
                           "instructions mount path default");
            AssertEx.Equal(DaemonConfig.DefaultInstructionsBreadcrumb, config.InstructionsBreadcrumb,
                           "instructions breadcrumb text default");
            AssertEx.True(config.InstructionsBreadcrumbEnabled, "instructions breadcrumb default");
            AssertEx.Equal(DaemonConfig.DefaultWorkerPrompt, config.WorkerPrompt,
                           "worker prompt default");
            AssertEx.Equal("claude", config.Agent, "agent command default");
            AssertEx.Equal("bash", config.AgentShell, "agent shell default");
            AssertEx.Equal("bash", config.Shell, "shell command default");
            AssertEx.Equal("less", config.Pager, "pager default");
            AssertEx.Equal("micro", config.Editor, "editor default");
            AssertEx.Equal("highlight --out-format=xterm256", config.Highlighter,
                           "highlighter default");
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

        static void RendersInstructionsBreadcrumb()
        {
            var config = new DaemonConfig
            {
                InstructionsBreadcrumb =
                    "Read {{ mount_path }} ({{project}}; {{ file }}) {{ unknown }}",
                InstructionsMountPath = "docs/SLOPWORLD.md",
            };

            AssertEx.Equal("Read docs/SLOPWORLD.md (repo; SLOPWORLD.md) {{ unknown }}",
                           config.RenderInstructionsBreadcrumb("repo"),
                           "instructions breadcrumb variables");
        }
    }
}
