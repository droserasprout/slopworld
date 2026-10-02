using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class SidebarViewHistoryTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("keeps the latest target per tab", LatestPerTab);
            yield return ("moves backward and forward", BackAndForward);
            yield return ("same-named Library templates retain distinct history", LibraryKinds);
            yield return ("a new visit clears forward history", NewVisitClearsForward);
            yield return ("reselecting a tab is not a history entry", ReselectingTab);
        }

        static void LibraryKinds()
        {
            var history = new SidebarViewHistory();
            var prompt = SidebarViewLocation.Library("review");
            var template = SidebarViewLocation.Library("review", template: true);
            history.Visit(prompt);
            history.Visit(template);
            AssertEx.True(history.Back(out var previous), "template selection creates a distinct visit");
            AssertEx.Equal(prompt, previous, "back restores the prompt, not the same-named template");
            AssertEx.True(history.Forward(out var next), "template remains in forward history");
            AssertEx.Equal(template, next, "forward restores template identity");
        }

        static void LatestPerTab()
        {
            var history = new SidebarViewHistory();
            var agent = SidebarViewLocation.Agent("alpha");
            var file = SidebarViewLocation.File("project", "/project/README.md");

            history.Visit(SidebarViewLocation.Agent("earlier"));
            history.Visit(agent);
            history.Visit(file);

            AssertEx.True(history.TryLast(SidebarTab.Agents, out var lastAgent),
                "agent target is retained");
            AssertEx.Equal(agent, lastAgent, "agent target identity");
            AssertEx.True(history.TryLast(SidebarTab.Files, out var lastFile),
                "file target is retained");
            AssertEx.Equal(file, lastFile, "file target identity");
        }

        static void BackAndForward()
        {
            var history = new SidebarViewHistory();
            var agent = SidebarViewLocation.Agent("alpha");
            var file = SidebarViewLocation.File("project", "/project/README.md");
            var git = SidebarViewLocation.Git("project", "README.md");
            history.Visit(SidebarViewLocation.Agent("earlier"));
            history.Visit(agent);
            history.Visit(file);
            history.Visit(git);

            AssertEx.True(history.Back(out var location), "back is available");
            AssertEx.Equal(file, location, "back reaches the previous location");
            AssertEx.True(history.Forward(out location), "forward is available");
            AssertEx.Equal(git, location, "forward returns to the next location");
        }

        static void NewVisitClearsForward()
        {
            var history = new SidebarViewHistory();
            history.Visit(SidebarViewLocation.Agent("alpha"));
            history.Visit(SidebarViewLocation.File("project", "a.cs"));
            AssertEx.True(history.Back(out _), "back reaches the agent");
            history.Visit(SidebarViewLocation.Search("project", "a.cs", 4));
            AssertEx.False(history.CanForward, "new location clears forward history");
        }

        static void ReselectingTab()
        {
            var history = new SidebarViewHistory();
            history.VisitTab(SidebarTab.Agents);
            history.Visit(SidebarViewLocation.Agent("alpha"));
            history.VisitTab(SidebarTab.Agents);

            AssertEx.True(history.Back(out var location), "the original tab point remains");
            AssertEx.Equal(SidebarViewLocation.TabOnly(SidebarTab.Agents), location,
                "reselecting the current tab does not add another point");
            AssertEx.False(history.CanBack, "there is no duplicate tab point");
        }
    }
}
