using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class SessionGizmoEligibilityTests
    {
        public static void WorkersCannotEditRegardlessOfLifetimeOrProcessState()
        {
            foreach (bool durable in new[] { false, true })
            foreach (bool alive in new[] { false, true })
            foreach (bool ephemeral in new[] { false, true })
                Assert.That(
                    SessionGizmoEligibility.CanEdit(
                        new SessionInfo
                        {
                            Worker = true,
                            Durable = durable,
                            Alive = alive,
                            Ephemeral = ephemeral,
                            Project = "project",
                        }
                    ),
                    Is.False
                );
        }

        public static void OrdinaryAgentsCanEditWithOrWithoutAProjectWhileRunningOrStopped()
        {
            foreach (bool alive in new[] { false, true })
            foreach (string project in new[] { "", "project" })
                Assert.That(
                    SessionGizmoEligibility.CanEdit(
                        new SessionInfo { Alive = alive, Project = project }
                    ),
                    Is.True
                );
        }

        public static void MissingHostAndEphemeralSessionsCannotEdit()
        {
            Assert.That(SessionGizmoEligibility.CanEdit(null), Is.False);
            foreach (bool alive in new[] { false, true })
            {
                Assert.That(
                    SessionGizmoEligibility.CanEdit(new SessionInfo { Host = true, Alive = alive }),
                    Is.False
                );
                Assert.That(
                    SessionGizmoEligibility.CanEdit(
                        new SessionInfo { Ephemeral = true, Alive = alive }
                    ),
                    Is.False
                );
            }
        }
    }
}
