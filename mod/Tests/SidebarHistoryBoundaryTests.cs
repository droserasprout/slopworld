using System.Collections.Generic;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class SidebarHistoryBoundaryTests
    {
        public static void EmptyHistoryAndExhaustedStacksDoNotInventLocations()
        {
            var history = new SidebarViewHistory();
            Assert.That(history.HasCurrent || history.CanBack || history.CanForward, Is.False);
            Assert.That(history.Back(out var empty), Is.False);
            Assert.That(empty, Is.EqualTo(default(SidebarViewLocation)));
            Assert.That(history.Forward(out empty), Is.False);
            Assert.That(empty, Is.EqualTo(default(SidebarViewLocation)));
            Assert.That(history.TryLast(SidebarTab.Tasks, out _), Is.False);
            var task = SidebarViewLocation.Task("task-id");
            history.Visit(task);
            history.Visit(task);
            Assert.That(history.HasCurrent, Is.True);
            Assert.That(history.Current, Is.EqualTo(task));
            Assert.That(history.CanBack, Is.False, "duplicate visits do not add history");
            Assert.That(history.TryLast(SidebarTab.Tasks, out var last), Is.True);
            Assert.That(last, Is.EqualTo(task));
            Assert.That(history.Back(out _), Is.False);
            Assert.That(history.Current, Is.EqualTo(task), "failed navigation preserves current target");
        }

        public static void HistoryRetainsOnlyLatest64BackEntriesAndCanReplayThem()
        {
            var history = new SidebarViewHistory();
            for (int i = 0; i < 70; i++) history.Visit(SidebarViewLocation.Task(i.ToString()));
            for (int i = 68; i >= 5; i--)
            {
                Assert.That(history.Back(out var location), Is.True);
                Assert.That(location, Is.EqualTo(SidebarViewLocation.Task(i.ToString())));
                Assert.That(history.Current, Is.EqualTo(location));
                Assert.That(history.TryLast(SidebarTab.Tasks, out var last), Is.True);
                Assert.That(last, Is.EqualTo(location), "back updates remembered target");
            }
            Assert.That(history.Back(out _), Is.False, "oldest five visits were evicted");
            Assert.That(history.CanForward, Is.True);
            for (int i = 6; i <= 69; i++)
            {
                Assert.That(history.Forward(out var location), Is.True);
                Assert.That(location, Is.EqualTo(SidebarViewLocation.Task(i.ToString())));
                Assert.That(history.TryLast(SidebarTab.Tasks, out var last), Is.True);
                Assert.That(last, Is.EqualTo(location), "forward updates remembered target");
            }
            Assert.That(history.Forward(out _), Is.False);
            Assert.That(history.CanForward, Is.False);
        }

        public static void TabOnlyNavigationPreservesRememberedTargetAndReselectionKeepsForwardHistory()
        {
            var history = new SidebarViewHistory();
            var file = SidebarViewLocation.File("project", "a.cs");
            history.Visit(file);
            history.VisitTab(SidebarTab.Tasks);
            history.VisitTab(SidebarTab.Files);
            Assert.That(history.TryLast(SidebarTab.Files, out var last), Is.True);
            Assert.That(last, Is.EqualTo(file));
            Assert.That(history.Current.HasTarget, Is.False);
            history.Back(out _);
            Assert.That(history.Current, Is.EqualTo(SidebarViewLocation.TabOnly(SidebarTab.Tasks)));
            history.VisitTab(SidebarTab.Tasks);
            Assert.That(history.CanForward, Is.True, "reselection leaves browser forward stack intact");
            history.Forward(out _);
            Assert.That(history.TryLast(SidebarTab.Files, out last), Is.True);
            Assert.That(last, Is.EqualTo(file));
            history.Back(out _);
            history.Visit(SidebarViewLocation.Task("new"));
            Assert.That(history.Forward(out _), Is.False, "new target replaces forward branch");
        }

        public static void SemanticLocationsSupportHashLookupWithoutCollapsingDifferentTargets()
        {
            var locations = new[]
            {
                SidebarViewLocation.TabOnly(SidebarTab.Tasks), SidebarViewLocation.Task("same"),
                SidebarViewLocation.Agent("same"), SidebarViewLocation.File("p", "same"),
                SidebarViewLocation.File("other", "same"), SidebarViewLocation.File("p", "different"),
                SidebarViewLocation.Search("p", "same", 1), SidebarViewLocation.Search("p", "same", 2),
                SidebarViewLocation.Git("p", "same"), SidebarViewLocation.Library("same"),
                SidebarViewLocation.Library("same", true), default(SidebarViewLocation),
            };
            var set = new HashSet<SidebarViewLocation>(locations);
            Assert.That(set.Count, Is.EqualTo(locations.Length));
            foreach (var location in locations)
            {
                Assert.That(set.Contains(location), Is.True);
                object boxed = location;
                Assert.That(location.Equals(boxed), Is.True);
                Assert.That(location.Equals("unrelated"), Is.False);
                Assert.That(location.Equals(null), Is.False);
            }
            var one = SidebarViewLocation.Task("same");
            var equal = SidebarViewLocation.Task("same");
            Assert.That(one == equal, Is.True);
            Assert.That(one != equal, Is.False);
            Assert.That(one.GetHashCode(), Is.EqualTo(equal.GetHashCode()));
            Assert.That(one != SidebarViewLocation.Task("other"), Is.True);
            Assert.That(SidebarViewLocation.File(null, null), Is.EqualTo(SidebarViewLocation.File("", "")));
        }
    }
}
