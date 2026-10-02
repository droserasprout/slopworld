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
                AssertEx.Equal(expected[index], definition.PersistedName,
                    "persisted tab name stays in navigation order");
                AssertEx.Equal(definition.Tab, registry.FromPersisted(definition.PersistedName).Tab,
                    "persisted tab resolves");
                index++;
            }
            AssertEx.Equal(expected.Length, index, "all tabs are registered");
            AssertEx.Equal(SidebarTab.Agents, registry.FromPersisted("unknown").Tab,
                "unknown persisted tab falls back to agents");
            AssertEx.Equal(SidebarTab.Agents, registry.For((SidebarTab)999).Tab,
                "unknown enum tab falls back to agents");
        }

        static void Capabilities()
        {
            var registry = NewRegistry();
            AssertEx.True(registry.For(SidebarTab.Agents).CanFold, "agents fold");
            AssertEx.True(registry.For(SidebarTab.Files).CanFold, "files fold");
            AssertEx.False(registry.For(SidebarTab.Search).CanFold, "search does not fold");
            AssertEx.True(registry.For(SidebarTab.Files).CanToggleDotfiles, "files filter visibility");
            AssertEx.True(registry.For(SidebarTab.Search).CanToggleDotfiles, "search filters visibility");
            AssertEx.False(registry.For(SidebarTab.Git).CanToggleDotfiles, "git has no visibility toggle");
        }

        static void Activation()
        {
            var events = new List<string>();
            var registry = NewRegistry(events);

            bool switched = SidebarTabActivation.Activate(
                registry.For(SidebarTab.Agents), registry.For(SidebarTab.Git),
                registry.Definitions, () => events.Add("menus"), () => events.Add("persist"));
            AssertEx.True(switched, "different tab switches");
            AssertEx.Equal("menus,close-agents,close-files,close-search,close-tasks,close-library,persist,entered-git",
                string.Join(",", events.ToArray()), "switch order closes menus first");

            events.Clear();
            switched = SidebarTabActivation.Activate(
                registry.For(SidebarTab.Git), registry.For(SidebarTab.Git),
                registry.Definitions, () => events.Add("menus"), () => events.Add("persist"));
            AssertEx.False(switched, "same tab is a reselection");
            AssertEx.Equal("menus,reselected-git", string.Join(",", events.ToArray()),
                "reselection skips switch cleanup and persistence");
        }

        static SidebarTabRegistry NewRegistry(List<string> events = null) =>
            SidebarTabCatalog.CreateRegistry(tab =>
            {
                string name = SidebarTabCatalog.Definition(tab, new SidebarTabHandlers()).PersistedName;
                return new SidebarTabHandlers
                {
                    Close = events == null ? (Action)(() => { }) : () => events.Add("close-" + name),
                    Entered = events == null ? (Action)(() => { }) : () => events.Add("entered-" + name),
                    Reselected = events == null ? (Action)(() => { }) : () => events.Add("reselected-" + name),
                };
            });
    }
}
