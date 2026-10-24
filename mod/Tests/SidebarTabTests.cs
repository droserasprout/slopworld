using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class SidebarTabTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("persisted names and unknown fallback", Registry);
            yield return ("tab capabilities", Capabilities);
            yield return ("switch and reselection dispatch", Activation);
        }

        static void Registry()
        {
            var registry = NewRegistry();
            var expected = new[] { "agents", "files", "git", "search", "tasks", "library" };
            int index = 0;
            foreach (var definition in registry.Definitions)
            {
                AssertEx.Equal(
                    expected[index],
                    definition.PersistedName,
                    "persisted tab name stays in navigation order"
                );
                AssertEx.Equal(
                    definition.Tab,
                    registry.FromPersisted(definition.PersistedName).Tab,
                    "persisted tab resolves"
                );
                index++;
            }
            AssertEx.Equal(expected.Length, index, "all tabs are registered");
            AssertEx.Equal(
                SidebarTab.Agents,
                registry.FromPersisted("unknown").Tab,
                "unknown persisted tab falls back to agents"
            );
            AssertEx.Equal(
                SidebarTab.Agents,
                registry.For((SidebarTab)999).Tab,
                "unknown enum tab falls back to agents"
            );
        }

        static void Capabilities()
        {
            var registry = NewRegistry();
            AssertEx.True(registry.For(SidebarTab.Agents).CanFold, "agents fold");
            AssertEx.True(registry.For(SidebarTab.Files).CanFold, "files fold");
            AssertEx.True(registry.For(SidebarTab.Search).CanFold, "search folds results");
            AssertEx.True(
                registry.For(SidebarTab.Files).CanToggleDotfiles,
                "files filter visibility"
            );
            AssertEx.False(
                registry.For(SidebarTab.Search).CanToggleDotfiles,
                "search does not share Files visibility controls"
            );
            AssertEx.True(registry.For(SidebarTab.Search).HasActions, "search has fold actions");
            AssertEx.False(
                registry.For(SidebarTab.Git).CanToggleDotfiles,
                "git has no visibility toggle"
            );
        }

        static void Activation()
        {
            var events = new List<string>();
            var registry = NewRegistry(events);
            foreach (var current in registry.Definitions)
            foreach (var target in registry.Definitions)
            {
                events.Clear();
                bool switched = SidebarTabActivation.Activate(
                    current,
                    target,
                    registry.Definitions,
                    () => events.Add("menus"),
                    () => events.Add("persist")
                );
                bool reselected = current.Tab == target.Tab;
                AssertEx.Equal(!reselected, switched, "only a different tab switches");
                var expected = new List<string> { "menus" };
                if (reselected)
                    expected.Add("reselected-" + target.PersistedName);
                else
                {
                    foreach (var definition in registry.Definitions)
                        if (definition.Tab != target.Tab)
                            expected.Add("close-" + definition.PersistedName);
                    expected.Add("persist");
                    expected.Add("entered-" + target.PersistedName);
                }
                AssertEx.Equal(
                    string.Join(",", expected),
                    string.Join(",", events),
                    current.PersistedName
                        + " to "
                        + target.PersistedName
                        + " preserves activation order"
                );
            }
        }

        static SidebarTabRegistry NewRegistry(List<string> events = null) =>
            new SidebarTabRegistry(definition =>
            {
                if (events == null)
                    return;
                string name = definition.PersistedName;
                definition.Close = () => events.Add("close-" + name);
                definition.Entered = () => events.Add("entered-" + name);
                definition.Reselected = () => events.Add("reselected-" + name);
            });
    }
}
