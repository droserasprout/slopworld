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
        }

        static void ErrandExecution()
        {
            var empty = LibraryItemInfo.FromWire(ProtobufFixtures.Read<Wire.LibraryItem>(JVal.Parse("{}")));
            AssertEx.True(!empty.Host && empty.AgentTemplate == "", "missing wire fields leave the execution choice unset");
            var item = new LibraryItemInfo { Name = "review", AgentTemplate = "reviewer" };
            var parsed = LibraryItemInfo.FromWire(ProtobufFixtures.Read<Wire.LibraryItem>(JVal.Parse(item.ToJson())));
            AssertEx.Equal("reviewer", parsed.AgentTemplate, "template choice survives wire");
            AssertEx.Equal(parsed.AgentTemplate, parsed.Copy().AgentTemplate, "template choice survives draft copy");
            item.Host = true;
            item.AgentTemplate = "";
            AssertEx.True(LibraryItemInfo.FromWire(ProtobufFixtures.Read<Wire.LibraryItem>(JVal.Parse(item.ToJson()))).Host, "host choice survives wire");
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
            AssertEx.Equal("open_terminal", wire["mode"].AsString(),
                           "file action mode is serialized");
            AssertEx.Equal(FileActionMode.OpenTerminal,
                           LibraryItemInfo.FromWire(ProtobufFixtures.Read<Wire.LibraryItem>(wire)).Mode,
                           "file action mode is parsed");
            AssertEx.Equal(FileActionMode.OpenTerminal, item.Copy().Mode,
                           "file action mode is copied");

            item.Mode = FileActionMode.Nothing;
            wire = JVal.Parse(item.ToJson());
            AssertEx.Equal("nothing", wire["mode"].AsString(),
                           "nothing mode is serialized");
            AssertEx.Equal(FileActionMode.Nothing,
                           LibraryItemInfo.FromWire(ProtobufFixtures.Read<Wire.LibraryItem>(wire)).Mode,
                           "nothing mode is parsed");
        }

        static void MissingModeKeepsInvocationMenu()
        {
            var item = LibraryItemInfo.FromWire(ProtobufFixtures.Read<Wire.LibraryItem>(JVal.Parse(
                "{\"name\":\"size\",\"kind\":\"fa\",\"link\":\"project\"," +
                "\"project\":\"\",\"text\":\"\",\"command\":\"du -sh\"}")));

            AssertEx.Equal(FileActionMode.Ask, item.Mode,
                           "old file actions keep the per-invocation choice");
        }
    }
}
