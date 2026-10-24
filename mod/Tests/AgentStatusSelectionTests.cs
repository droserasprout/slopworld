using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class AgentStatusSelectionTests
    {
        public static void AllCheckboxClearsAllAndRestoresAllFromEveryOtherSelection()
        {
            for (int mask = 0; mask <= (int)AgentStatusFilter.All; mask++)
            {
                var selected = (AgentStatusFilter)mask;
                var next = AgentStatusSelection.ToggleAll(selected);
                Assert.That(
                    next,
                    Is.EqualTo(
                        selected == AgentStatusFilter.All
                            ? AgentStatusFilter.None
                            : AgentStatusFilter.All
                    )
                );
                Assert.That(
                    AgentStatusSelection.Parse(AgentStatusSelection.Format(next)),
                    Is.EqualTo(next)
                );
            }
        }

        public static void AllChecksEveryStatusAndUncheckingOneKeepsTheOthers()
        {
            var selected = AgentStatusSelection.Parse("all");
            foreach (
                var status in new[]
                {
                    AgentStatusFilter.Active,
                    AgentStatusFilter.Idle,
                    AgentStatusFilter.Down,
                    AgentStatusFilter.Shells,
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
                    AgentStatusFilter.Shells,
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
            for (int mask = 0; mask <= (int)AgentStatusFilter.All; mask++)
            {
                var selected = (AgentStatusFilter)mask;
                Assert.That(
                    AgentStatusSelection.Parse(AgentStatusSelection.Format(selected)),
                    Is.EqualTo(selected)
                );
            }
            foreach (var value in new[] { null, "", "unknown", "all" })
                Assert.That(AgentStatusSelection.Parse(value), Is.EqualTo(AgentStatusFilter.All));
            Assert.That(
                AgentStatusSelection.Parse("active,idle,down"),
                Is.EqualTo(
                    AgentStatusFilter.Active | AgentStatusFilter.Idle | AgentStatusFilter.Down
                )
            );
            Assert.That(
                AgentStatusSelection.Parse(" Active | down "),
                Is.EqualTo(AgentStatusFilter.Active | AgentStatusFilter.Down)
            );
        }

        public static void ShellsAreIndependentOfAgentStatusAndWorkersKeepTheirStatus()
        {
            foreach (var state in new[] { AgentState.Working, AgentState.Idle, AgentState.Down })
            {
                foreach (
                    var shell in new[]
                    {
                        new SessionInfo { Host = true, State = state },
                        new SessionInfo { Ephemeral = true, State = state },
                    }
                )
                {
                    Assert.That(
                        AgentStatusSelection.Matches(AgentStatusFilter.Shells, shell),
                        Is.True
                    );
                    Assert.That(
                        AgentStatusSelection.Matches(
                            AgentStatusFilter.All ^ AgentStatusFilter.Shells,
                            shell
                        ),
                        Is.False
                    );
                    Assert.That(
                        AgentStatusSelection.Matches(AgentStatusFilter.None, shell),
                        Is.False
                    );
                }
                var status =
                    state == AgentState.Working ? AgentStatusFilter.Active
                    : state == AgentState.Idle ? AgentStatusFilter.Idle
                    : AgentStatusFilter.Down;
                foreach (bool worker in new[] { false, true })
                {
                    var agent = new SessionInfo
                    {
                        Worker = worker,
                        Ephemeral = worker,
                        State = state,
                    };
                    Assert.That(
                        AgentStatusSelection.Matches(AgentStatusFilter.Shells, agent),
                        Is.False
                    );
                    Assert.That(AgentStatusSelection.Matches(status, agent), Is.True);
                }
            }
        }
    }
}
