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
