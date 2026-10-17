using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class AgentStatusSelectionTests
    {
        public static void AllChecksEveryStatusAndUncheckingOneKeepsTheOthers()
        {
            var selected = AgentStatusSelection.Parse("all");
            foreach (
                var status in new[]
                {
                    AgentStatusFilter.Active,
                    AgentStatusFilter.Idle,
                    AgentStatusFilter.Down,
                }
            )
            {
                Assert.That((selected & status) != 0, Is.True);
                var next = AgentStatusSelection.Toggle(selected, status);
                Assert.That((next & status) == 0, Is.True);
                Assert.That(next | status, Is.EqualTo(AgentStatusFilter.All));
                Assert.That(AgentStatusSelection.Toggle(next, status), Is.EqualTo(selected));
            }
        }

        public static void UncheckingEveryStatusStaysEmptyAcrossPersistence()
        {
            var selected = AgentStatusFilter.All;
            foreach (
                var status in new[]
                {
                    AgentStatusFilter.Active,
                    AgentStatusFilter.Idle,
                    AgentStatusFilter.Down,
                }
            )
                selected = AgentStatusSelection.Parse(
                    AgentStatusSelection.Format(AgentStatusSelection.Toggle(selected, status))
                );
            Assert.That(selected, Is.EqualTo(AgentStatusFilter.None));
            Assert.That(AgentStatusSelection.Format(selected), Is.EqualTo("none"));
            Assert.That(
                AgentStatusSelection.Toggle(selected, AgentStatusFilter.Idle),
                Is.EqualTo(AgentStatusFilter.Idle)
            );
        }

        public static void EverySelectionRoundTripsAndExistingPreferencesKeepTheirMeaning()
        {
            for (int mask = 0; mask <= 7; mask++)
            {
                var selected = (AgentStatusFilter)mask;
                Assert.That(
                    AgentStatusSelection.Parse(AgentStatusSelection.Format(selected)),
                    Is.EqualTo(selected)
                );
            }
            foreach (var value in new[] { null, "", "unknown", "all", "active,idle,down" })
                Assert.That(AgentStatusSelection.Parse(value), Is.EqualTo(AgentStatusFilter.All));
            Assert.That(
                AgentStatusSelection.Parse(" Active | down "),
                Is.EqualTo(AgentStatusFilter.Active | AgentStatusFilter.Down)
            );
        }
    }
}
