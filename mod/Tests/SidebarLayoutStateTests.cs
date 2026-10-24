using System.Collections.Generic;
using NUnit.Framework;
using RimWorld;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class SidebarLayoutStateTests
    {
        public static void ClearingZeroPawnLayoutRequiresRebuildWithUnchangedInputs() =>
            Sidebar.AssertClearedLayoutRequiresRebuild();
    }
}

namespace SlopWorld
{
    public static partial class Sidebar
    {
        // Only collection element identities are needed to exercise the production cache.
        struct Row { }

        struct Head { }

        internal static void AssertClearedLayoutRequiresRebuild()
        {
            var layout = new LayoutState();
            var entries = new List<ColonistBar.Entry>();
            var locations = new List<Vector2>();
            bool Matches() =>
                layout.Matches(
                    entries,
                    0,
                    SidebarTab.Agents,
                    1,
                    2,
                    3,
                    AgentStatusFilter.All,
                    200f,
                    800f,
                    700f,
                    20f,
                    4,
                    "less",
                    "micro"
                );
            void Remember() =>
                layout.RememberInputs(
                    entries,
                    0,
                    SidebarTab.Agents,
                    1,
                    2,
                    3,
                    AgentStatusFilter.All,
                    200f,
                    800f,
                    700f,
                    20f,
                    locations,
                    4,
                    "less",
                    "micro"
                );

            Assert.That(Matches(), Is.False, "first Agents placement must build");
            layout.Rows.Add(new Row());
            layout.Heads.Add(new Head());
            layout.TopWorkers.Add(new SessionInfo { Name = "worker" });
            layout.AgentContentH = 900f;
            layout.LastScale = 1f;
            Remember();
            Assert.That(Matches(), Is.True, "unchanged Agents inputs reuse built output");

            // Files placement clears Agents output without changing any daemon revision.
            layout.BeginFrame();
            Assert.That(layout.Rows.Count, Is.Zero);
            Assert.That(layout.Heads.Count, Is.Zero);
            Assert.That(layout.TopWorkers.Count, Is.Zero);
            Assert.That(layout.AgentContentH, Is.Zero);
            Assert.That(Matches(), Is.False, "returning to Agents must rebuild cleared output");
            layout.BeginFrame();
            Assert.That(Matches(), Is.False, "repeated Files placement remains invalid");

            layout.Rows.Add(new Row());
            Remember();
            Assert.That(Matches(), Is.True, "completed rebuild restores cache hits");
            Assert.That(layout.Rows.Count, Is.EqualTo(1));
        }
    }
}

namespace Verse
{
    public class Pawn { }
}

namespace RimWorld
{
    public class ColonistBar
    {
        public struct Entry
        {
            public Verse.Pawn pawn;
        }
    }
}
