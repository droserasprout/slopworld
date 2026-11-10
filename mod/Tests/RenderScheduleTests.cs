using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class RenderScheduleTests
    {
        [TestCase(5)]
        [TestCase(15)]
        [TestCase(60)]
        public void RenderingDoesNotLimitInputOrServiceIterations(int fps)
        {
            var schedule = new RenderSchedule();
            schedule.Configure(fps);
            int renders = 0;
            int inputs = 0;
            // Each event wakes the poll immediately, before its service timeout.
            // All 1000 inputs must be handled even with only five presentations.
            for (int i = 0; i < 1000; i++)
            {
                double now = i / 1000d;
                Assert.That(
                    schedule.WaitSeconds(now, true),
                    Is.InRange(0d, RenderSchedule.ServiceInterval)
                );
                schedule.Polled(now);
                inputs++;
                if (schedule.TryRender(now, true))
                    renders++;
            }
            Assert.That(inputs, Is.EqualTo(1000));
            Assert.That(renders, Is.InRange(fps - 1, fps));
        }

        [Test]
        public void BackpressureLeavesUpdateDeadlineBoundedAndDoesNotSpendRenderDeadline()
        {
            var schedule = new RenderSchedule();
            schedule.Configure(5);
            schedule.Polled(1);
            Assert.That(schedule.TryRender(1, false), Is.False);
            Assert.That(
                schedule.WaitSeconds(1, false),
                Is.EqualTo(RenderSchedule.ServiceInterval).Within(1e-12)
            );
            Assert.That(schedule.TryRender(1.001, true), Is.True);
            Assert.That(schedule.TryRender(1.002, true), Is.False);
            Assert.That(schedule.WaitSeconds(2, false), Is.Zero);
        }

        [Test]
        public void RateChangesAndLongStallsDoNotDelayFirstFrameOrCauseCatchUpBursts()
        {
            var schedule = new RenderSchedule();
            schedule.Configure(5);
            Assert.That(schedule.TryRender(1, true), Is.True);
            Assert.That(schedule.TryRender(1.01, true), Is.False);
            schedule.Configure(15);
            Assert.That(schedule.TryRender(1.01, true), Is.True);
            Assert.That(schedule.TryRender(100, true), Is.True);
            Assert.That(schedule.TryRender(100.001, true), Is.False);
            schedule.Configure(-1);
            Assert.That(schedule.TryRender(100.001, true), Is.True);
            Assert.That(schedule.TryRender(100.002, true), Is.True);
            Assert.That(schedule.TryRender(100.003, false), Is.False);
        }

        [Test]
        public void MinimizedWindowWaitsForServicesAndResumesRenderingImmediately()
        {
            var schedule = new RenderSchedule();
            schedule.Configure(5);
            schedule.Polled(1);
            schedule.SurfaceUnavailable();
            Assert.That(
                schedule.WaitSeconds(1, true),
                Is.EqualTo(RenderSchedule.ServiceInterval).Within(1e-12)
            );
            Assert.That(schedule.TryRender(1.01, true), Is.True);
        }

        [Test]
        public void RenderDeadlineCanWakeEarlierThanNextServiceWithoutDelayingServices()
        {
            var schedule = new RenderSchedule();
            schedule.Configure(1000);
            schedule.Polled(1);
            Assert.That(schedule.TryRender(1, true), Is.True);
            Assert.That(schedule.WaitSeconds(1, true), Is.EqualTo(0.001).Within(1e-12));
            Assert.That(schedule.WaitSeconds(1.005, true), Is.Zero);
        }
    }
}
