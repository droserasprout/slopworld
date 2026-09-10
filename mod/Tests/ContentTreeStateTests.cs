using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class ContentTreeStateTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("folding and fold-all policy", Folding);
            yield return ("revision invalidation", Revision);
            yield return ("selection survives refresh and group removal", Selection);
        }

        static void Folding()
        {
            var state = new ContentTreeState();
            var groups = new[] { "one", "two" };
            state.SetAllFolded(groups, true);
            AssertEx.True(state.AllFolded(groups), "all groups are folded");
            AssertEx.True(state.ToggleCollapsed("one"), "opening reports true");
            AssertEx.False(state.AllFolded(groups), "one open means not all folded");
            AssertEx.False(state.ToggleCollapsed("one"), "closing reports false");
            AssertEx.True(state.AllFolded(groups), "closing restores all folded");
            state.SetAllFolded(groups, false);
            AssertEx.False(state.AllFolded(groups), "empty fold set is not all folded");
        }

        static void Revision()
        {
            var state = new ContentTreeState();
            int initial = state.Revision;
            state.Bump();
            AssertEx.True(state.Revision != initial, "explicit tree invalidation changes revision");
            int beforeToggle = state.Revision;
            state.ToggleCollapsed("one");
            AssertEx.True(state.Revision != beforeToggle, "fold change invalidates tree");
            int beforeNoop = state.Revision;
            state.SetCollapsed("one", true);
            AssertEx.Equal(beforeNoop, state.Revision, "same fold state does not invalidate twice");
        }

        static void Selection()
        {
            var state = new ContentTreeState();
            state.Select("project\nfile.txt");
            state.SetAllFolded(new[] { "project" }, true);
            state.SyncGroups(new[] { "project", "other" });
            AssertEx.True(state.IsSelected("project\nfile.txt"),
                "selection key survives a refresh");

            state.SetCollapsed("other", true);
            state.SyncGroups(new[] { "project" });
            AssertEx.False(state.IsCollapsed("other"), "removed group loses stale fold state");
            AssertEx.True(state.IsCollapsed("project"), "surviving group keeps fold state");
            AssertEx.True(state.IsSelected("project\nfile.txt"),
                "group removal does not erase a semantic selection key");
        }
    }
}
