using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class LibraryItemTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("round trips file action modes", RoundTripsFileActionModes);
            yield return ("missing mode keeps the invocation menu", MissingModeKeepsInvocationMenu);
            yield return ("errand execution choice round trips", ErrandExecution);
            yield return (
                "timer drafts retain references without sharing mutable state",
                TimerRoundTrip
            );
        }

        static void TimerRoundTrip()
        {
            var source = new Wire.LibraryItem
            {
                Name = "Review",
                Kind = WireProtocol.LibraryKind.Timer,
                Timer = new Wire.TimerConfig
                {
                    Enabled = true,
                    Schedule = "Mon..Fri 09:00",
                    Trigger = "calendar",
                    Action = "worker",
                    Template = "Reviewer",
                    Project = "repo",
                    Shell = "Cleanup",
                    Timezone = "America/Montevideo",
                    Target = "0123456789abcdef",
                    Prompt = "Review prompt",
                },
                TimerStatus = new Wire.TimerStatus { LastAt = 60, Outcome = "Submitted" },
            };
            var item = LibraryItemInfo.FromWire(source);
            var draft = item.Copy();
            draft.Timer.Prompt = "Changed";
            AssertEx.Equal(
                "Review prompt",
                item.Timer.Prompt,
                "draft does not change accepted reference"
            );
            AssertEx.Equal(LibraryItemKind.Timer, item.Kind, "Timer kind is retained");
            AssertEx.Equal(
                "0123456789abcdef",
                item.ToWire().Timer.Target,
                "stable target survives save"
            );
            AssertEx.True(
                item.ToWire().TimerStatus == null,
                "read-only status is not submitted with edits"
            );
            AssertEx.Equal("Submitted", item.TimerStatus.Outcome, "status remains visible");
            AssertEx.Equal("worker", item.ToWire().Timer.Action, "action survives save");
            AssertEx.Equal("Reviewer", item.Copy().Timer.Template, "template survives copying");
            AssertEx.Equal("repo", item.ToWire().Timer.Project, "worker project survives save");
            AssertEx.Equal("Cleanup", item.Copy().Timer.Shell, "shell reference survives copying");
            AssertEx.Equal("calendar", item.ToWire().Timer.Trigger, "trigger survives save");
            AssertEx.Equal(
                "America/Montevideo",
                item.Copy().Timer.Timezone,
                "named zone survives copying"
            );
        }

        static void ErrandExecution()
        {
            var empty = LibraryItemInfo.FromWire(
                ProtobufFixtures.Read<Wire.LibraryItem>(JVal.Parse("{}"))
            );
            AssertEx.True(
                !empty.Host && empty.AgentTemplate == "",
                "missing wire fields leave the execution choice unset"
            );
            AssertEx.False(
                empty.Runnable,
                "unbound Prompts are reusable text, not temporary agent launches"
            );
            var item = new LibraryItemInfo { Name = "review", AgentTemplate = "reviewer" };
            AssertEx.True(item.Runnable, "explicit execution makes a Prompt runnable");
            var parsed = LibraryItemInfo.FromWire(
                ProtobufFixtures.Read<Wire.LibraryItem>(JVal.Parse(item.ToJson()))
            );
            AssertEx.Equal("reviewer", parsed.AgentTemplate, "template choice survives wire");
            AssertEx.Equal(
                parsed.AgentTemplate,
                parsed.Copy().AgentTemplate,
                "template choice survives draft copy"
            );
            item.Host = true;
            item.AgentTemplate = "";
            AssertEx.True(
                LibraryItemInfo
                    .FromWire(ProtobufFixtures.Read<Wire.LibraryItem>(JVal.Parse(item.ToJson())))
                    .Host,
                "host choice survives wire"
            );
            AssertEx.True(item.Copy().Host, "host choice survives draft copy");
        }

        static void RoundTripsFileActionModes()
        {
            var item = new LibraryItemInfo
            {
                Name = "size",
                Kind = LibraryItemKind.FileAction,
                Command = "du -sh",
                Mode = FileActionMode.OpenTerminal,
            };

            var wire = JVal.Parse(item.ToJson());
            AssertEx.Equal(
                "open_terminal",
                wire["mode"].AsString(),
                "file action mode is serialized"
            );
            AssertEx.Equal(
                FileActionMode.OpenTerminal,
                LibraryItemInfo.FromWire(ProtobufFixtures.Read<Wire.LibraryItem>(wire)).Mode,
                "file action mode is parsed"
            );
            AssertEx.Equal(
                FileActionMode.OpenTerminal,
                item.Copy().Mode,
                "file action mode is copied"
            );

            item.Mode = FileActionMode.Nothing;
            wire = JVal.Parse(item.ToJson());
            AssertEx.Equal("nothing", wire["mode"].AsString(), "nothing mode is serialized");
            AssertEx.Equal(
                FileActionMode.Nothing,
                LibraryItemInfo.FromWire(ProtobufFixtures.Read<Wire.LibraryItem>(wire)).Mode,
                "nothing mode is parsed"
            );
        }

        static void MissingModeKeepsInvocationMenu()
        {
            var item = LibraryItemInfo.FromWire(
                ProtobufFixtures.Read<Wire.LibraryItem>(
                    JVal.Parse(
                        "{\"name\":\"size\",\"kind\":\"fa\",\"link\":\"project\","
                            + "\"project\":\"\",\"text\":\"\",\"command\":\"du -sh\"}"
                    )
                )
            );

            AssertEx.Equal(
                FileActionMode.Ask,
                item.Mode,
                "old file actions keep the per-invocation choice"
            );
        }
    }
}
