using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class LibraryItemTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("repository source is read only and not written back", RepositorySource);
            yield return ("round trips file action modes", RoundTripsFileActionModes);
            yield return ("missing mode keeps the invocation menu", MissingModeKeepsInvocationMenu);
            yield return ("errand execution choice round trips", ErrandExecution);
        }

        static void ErrandExecution()
        {
            var empty = LibraryItemInfo.FromJson(JVal.Parse("{}"));
            AssertEx.True(!empty.Host && empty.AgentTemplate == "", "entries require an execution choice");
            var item = new LibraryItemInfo { Name = "review", AgentTemplate = "repo::reviewer" };
            var parsed = LibraryItemInfo.FromJson(JVal.Parse(item.ToJson()));
            AssertEx.Equal("repo::reviewer", parsed.AgentTemplate, "template choice survives wire");
            AssertEx.Equal(parsed.AgentTemplate, parsed.Copy().AgentTemplate, "template choice survives draft copy");
            item.Host = true;
            item.AgentTemplate = "";
            AssertEx.True(LibraryItemInfo.FromJson(JVal.Parse(item.ToJson())).Host, "host choice survives wire");
            AssertEx.True(item.Copy().Host, "host choice survives draft copy");
        }

        static void RepositorySource()
        {
            var item = LibraryItemInfo.FromJson(JVal.Parse(
                "{\"name\":\"repo::review\",\"source\":\"/repo/.slopworld/library/review.toml\"}"));
            AssertEx.True(item.ReadOnly, "repository entries are read only");
            AssertEx.Equal(item.Source, item.Copy().Source, "inspection retains source");
            AssertEx.True(JVal.Parse(item.ToJson())["source"].IsNull,
                "personal copies do not write repository ownership");
            AssertEx.True(!LibraryItemInfo.FromJson(JVal.Parse("{}")).ReadOnly,
                "ordinary entries remain editable");
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
                           LibraryItemInfo.FromJson(wire).Mode,
                           "file action mode is parsed");
            AssertEx.Equal(FileActionMode.OpenTerminal, item.Copy().Mode,
                           "file action mode is copied");

            item.Mode = FileActionMode.Nothing;
            wire = JVal.Parse(item.ToJson());
            AssertEx.Equal("nothing", wire["mode"].AsString(),
                           "nothing mode is serialized");
            AssertEx.Equal(FileActionMode.Nothing,
                           LibraryItemInfo.FromJson(wire).Mode,
                           "nothing mode is parsed");
        }

        static void MissingModeKeepsInvocationMenu()
        {
            var item = LibraryItemInfo.FromJson(JVal.Parse(
                "{\"name\":\"size\",\"kind\":\"fa\",\"link\":\"project\"," +
                "\"project\":\"\",\"text\":\"\",\"command\":\"du -sh\"}"));

            AssertEx.Equal(FileActionMode.Ask, item.Mode,
                           "old file actions keep the per-invocation choice");
        }
    }
}
