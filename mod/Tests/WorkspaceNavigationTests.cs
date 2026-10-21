using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class WorkspaceNavigationTests
    {
        public static void ReturningRestoresEachGroupBeforeCycling()
        {
            var navigation = new WorkspaceNavigation();
            var sessions = new List<string> { "agent", "shell", "worker" };
            var readers = new List<string> { "editor", "view-native-1", "diff" };
            navigation.Remember("shell", reader: false);
            navigation.Remember("view-native-1", reader: true);
            AssertEx.Equal(
                "shell",
                navigation.Select(sessions, "view-native-1", 1, false),
                "return to shell without advancing"
            );
            AssertEx.Equal(
                "view-native-1",
                navigation.Select(readers, "shell", 1, true),
                "return to native reader without advancing"
            );
            AssertEx.Equal(
                "shell",
                navigation.Select(sessions, "editor", -1, false),
                "reverse shortcut also restores"
            );
            AssertEx.Equal(
                "view-native-1",
                navigation.Select(readers, "worker", -1, true),
                "reverse reader shortcut also restores"
            );
            AssertEx.Equal(
                "worker",
                navigation.Select(sessions, "shell", 1, false),
                "within sessions advances"
            );
            AssertEx.Equal(
                "editor",
                navigation.Select(readers, "view-native-1", -1, true),
                "within readers goes backward"
            );
            AssertEx.Equal(
                "agent",
                navigation.Select(sessions, "worker", 1, false),
                "forward wraps"
            );
            AssertEx.Equal(
                "diff",
                navigation.Select(readers, "editor", -1, true),
                "backward wraps"
            );
            navigation.Remember("worker", reader: false);
            navigation.Remember("diff", reader: true);
            navigation.Remember(null, reader: false);
            AssertEx.Equal(
                "worker",
                navigation.Select(sessions, null, 1, false),
                "latest selection survives deselection"
            );
            AssertEx.Equal(
                "diff",
                navigation.Select(readers, "worker", 1, true),
                "latest reader wins independently"
            );
        }

        public static void BackgroundReaderReplacementPreservesTheRememberedVisit()
        {
            var navigation = new WorkspaceNavigation();
            var sessions = new List<string> { "first-agent", "agent", "last-agent" };
            var readers = new List<string> { "first-reader", "old-reader", "last-reader" };
            navigation.Remember("old-reader", reader: true);
            navigation.Remember("agent", reader: false);

            navigation.ReplaceReader("old-reader", "new-reader");
            readers[1] = "new-reader";
            foreach (int direction in new[] { -1, 1 })
                AssertEx.Equal(
                    "new-reader",
                    navigation.Select(readers, "agent", direction, reader: true),
                    "return from agent restores the refreshed reader in either direction"
                );
            AssertEx.Equal(
                "agent",
                navigation.Select(sessions, "new-reader", 1, reader: false),
                "reader replacement preserves the session cursor"
            );

            navigation.ReplaceReader("new-reader", "latest-reader");
            readers[1] = "latest-reader";
            navigation.ReplaceReader("old-reader", "stale-reader");
            navigation.ReplaceReader("latest-reader", null);
            AssertEx.Equal(
                "latest-reader",
                navigation.Select(readers, "agent", 1, reader: true),
                "repeated refresh follows the cursor and ignores stale or invalid handoffs"
            );

            navigation.Remember("view-native-1", reader: true);
            readers.Add("view-native-1");
            AssertEx.Equal(
                "background-reader",
                navigation.ReplaceReader("latest-reader", "background-reader", "latest-reader"),
                "refresh remaps the session hidden behind the native reader without a visit"
            );
            readers[1] = "background-reader";
            AssertEx.Equal(
                "view-native-1",
                navigation.Select(readers, "agent", 1, reader: true),
                "background refresh cannot replace a newer native reader visit"
            );
            AssertEx.Equal(
                "agent",
                navigation.ReplaceReader("background-reader", "next-reader", "agent"),
                "refresh leaves an unrelated selection unchanged"
            );
        }

        public static void MissingOrFilteredTargetsFallBackToRequestedEnd()
        {
            var navigation = new WorkspaceNavigation();
            var order = new List<string> { "first", "last" };
            AssertEx.Equal(
                "first",
                navigation.Select(order, null, 1, true),
                "initial forward target"
            );
            AssertEx.Equal(
                "last",
                navigation.Select(order, null, -1, false),
                "initial reverse target"
            );
            navigation.Remember("closed", reader: true);
            navigation.Remember("filtered", reader: false);
            AssertEx.Equal(
                "first",
                navigation.Select(order, "closed", 1, true),
                "closed reader falls back"
            );
            AssertEx.Equal(
                "last",
                navigation.Select(order, "other", -1, false),
                "filtered session falls back"
            );
            order.Clear();
            AssertEx.Equal<string>(
                null,
                navigation.Select(order, null, 1, true),
                "empty inventory"
            );
            order.Add("only");
            AssertEx.Equal(
                "only",
                navigation.Select(order, "only", -1, true),
                "single target wraps to itself"
            );
        }
    }
}
