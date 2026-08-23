using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld.Tests
{
    static class SlopConfigTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("reads daemon defaults", ReadsDaemonDefaults);
            yield return ("round trips every patch field", RoundTripsEveryPatchField);
            yield return ("splits and joins line lists", SplitsAndJoinsLineLists);
        }

        static void ReadsDaemonDefaults()
        {
            var config = SlopConfig.FromJson(JVal.Parse("{}"));

            AssertEx.True(config.Usage, "usage default");
            AssertEx.Equal(60, config.UsagePollSecs, "usage poll default");
            AssertEx.Equal(0, config.UsageItems.Count, "usage item defaults");
            AssertEx.Equal("~/.claude/.credentials.json", config.ClaudeCredentials,
                           "Claude credentials default");
            AssertEx.False(config.Openrouter, "OpenRouter default");
            AssertEx.Equal("", config.OpenrouterKeyFile, "OpenRouter key default");
            AssertEx.True(config.Openai, "OpenAI default");
            AssertEx.Equal("~/.codex/auth.json", config.OpenaiCredentials,
                           "OpenAI credentials default");
            AssertEx.Equal("never", config.AgentTitles, "agent title default");
            AssertEx.Equal("google/gemini-3.1-flash-lite", config.TitleModel,
                           "title model default");
            AssertEx.Equal("always", config.PiTitles, "Pi title default");
            AssertEx.True(config.HostTitles, "host title default");
            AssertEx.Equal("claude", config.Agent, "agent command default");
            AssertEx.Equal("bash", config.Shell, "shell command default");
            AssertEx.Equal("less", config.Pager, "pager default");
            AssertEx.Equal("micro", config.Editor, "editor default");
            AssertEx.Equal("highlight --out-format=xterm256", config.Highlighter,
                           "highlighter default");
        }

        static void RoundTripsEveryPatchField()
        {
            var expected = new SlopConfig
            {
                Usage = false,
                UsagePollSecs = 17,
                UsageItems = new Dictionary<string, SlopConfig.UsageItemConfig>
                {
                    ["claude_session"] = new SlopConfig.UsageItemConfig
                    {
                        Poll = false,
                        IntervalSecs = 15,
                    },
                    ["openrouter_balance"] = new SlopConfig.UsageItemConfig
                    {
                        Poll = true,
                        IntervalSecs = 0,
                    },
                },
                ClaudeCredentials = "~/.config/claude \"credentials\"",
                Openrouter = true,
                OpenrouterKeyFile = "/run/user/1000/openrouter.key",
                Openai = false,
                OpenaiCredentials = "~/.config/codex/auth.json",
                AgentTitles = "once",
                TitleModel = "provider/model:flash",
                PiTitles = "never",
                HostTitles = false,
                Agent = "codex --full-auto",
                Shell = "bash -lc",
                Pager = "less -R",
                Editor = "micro --no-help",
                Highlighter = "highlight --out-format=xterm256",
            };
            var actual = SlopConfig.FromJson(JVal.Parse(expected.ToPatchJson()));

            AssertEx.True(!actual.Usage, "usage round trip");
            AssertEx.Equal(expected.UsagePollSecs, actual.UsagePollSecs, "poll round trip");
            AssertEx.False(actual.UsageItems["claude_session"].Poll,
                           "usage item poll round trip");
            AssertEx.Equal(15, actual.UsageItems["claude_session"].IntervalSecs,
                           "usage item interval round trip");
            AssertEx.Equal(0, actual.UsageItems["openrouter_balance"].IntervalSecs,
                           "usage item inherited interval round trip");
            AssertEx.Equal(expected.ClaudeCredentials, actual.ClaudeCredentials,
                           "Claude credentials round trip");
            AssertEx.Equal(expected.Openrouter, actual.Openrouter, "OpenRouter round trip");
            AssertEx.Equal(expected.OpenrouterKeyFile, actual.OpenrouterKeyFile,
                           "OpenRouter key round trip");
            AssertEx.Equal(expected.Openai, actual.Openai, "OpenAI round trip");
            AssertEx.Equal(expected.OpenaiCredentials, actual.OpenaiCredentials,
                           "OpenAI credentials round trip");
            AssertEx.Equal(expected.AgentTitles, actual.AgentTitles,
                           "agent titles round trip");
            AssertEx.Equal(expected.TitleModel, actual.TitleModel, "title model round trip");
            AssertEx.Equal(expected.PiTitles, actual.PiTitles, "Pi titles round trip");
            AssertEx.Equal(expected.HostTitles, actual.HostTitles, "host titles round trip");
            AssertEx.Equal(expected.Agent, actual.Agent, "agent round trip");
            AssertEx.Equal(expected.Shell, actual.Shell, "shell round trip");
            AssertEx.Equal(expected.Pager, actual.Pager, "pager round trip");
            AssertEx.Equal(expected.Editor, actual.Editor, "editor round trip");
            AssertEx.Equal(expected.Highlighter, actual.Highlighter,
                           "highlighter round trip");
        }

        static void SplitsAndJoinsLineLists()
        {
            var lines = new List<string> { "first", "second", "third" };

            AssertEx.Equal("first\nsecond\nthird", SlopConfig.Lines(lines), "line join");
            AssertEx.Sequence(lines, SlopConfig.Split(" first \n\nsecond\n third \n"),
                              "line split and trim");
            AssertEx.True(!SlopConfig.Split(null).Any(), "null line list");
        }
    }
}
