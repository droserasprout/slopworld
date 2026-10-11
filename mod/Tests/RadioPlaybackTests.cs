using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class RadioPlaybackTests
    {
        static RadioPlayback.UpdateKind Step(RadioPlayback playback, double now, bool online,
            RadioPlaybackPlan plan, float volume) => playback.Step(now, online, () => plan, volume, out _);

        public static void PendingSelectionAndReconnectSendImmediately()
        {
            var playback = new RadioPlayback();
            var plan = RadioPlaybackPlan.Station("radio", "high");
            Assert.That(Step(playback, 0, true, plan, 0.5f), Is.EqualTo(RadioPlayback.UpdateKind.Selection));
            Assert.That(Step(playback, 0.01, true, plan, 0.5f), Is.EqualTo(RadioPlayback.UpdateKind.None));
            playback.Invalidate();
            Assert.That(Step(playback, 0.02, true, plan, 0.5f), Is.EqualTo(RadioPlayback.UpdateKind.Selection));
            Assert.That(Step(playback, 0.03, false, plan, 0.5f), Is.EqualTo(RadioPlayback.UpdateKind.None));
            Assert.That(Step(playback, 0.04, true, plan, 0.5f), Is.EqualTo(RadioPlayback.UpdateKind.Selection));
        }

        public static void IdleAndOfflineFramesDoNotResolvePlaybackSources()
        {
            var playback = new RadioPlayback();
            int resolved = 0;
            RadioPlaybackPlan Resolve() { resolved++; return RadioPlaybackPlan.Spotify; }
            playback.Step(0, false, Resolve, 1, out _);
            Assert.That(resolved, Is.Zero);
            playback.Step(0, true, Resolve, 1, out var plan);
            Assert.That(plan.Kind, Is.EqualTo(RadioPlaybackPlan.SourceKind.Spotify));
            playback.Step(0.01, true, Resolve, 1, out _);
            Assert.That(resolved, Is.EqualTo(1));
        }

        public static void VolumeUpdatesRespectCadenceAndMinimumDifference()
        {
            var playback = new RadioPlayback();
            var plan = RadioPlaybackPlan.File("/ost");
            Step(playback, 0, true, plan, 0.5f);
            Assert.That(Step(playback, 0.01, true, plan, 0.6f), Is.EqualTo(RadioPlayback.UpdateKind.None));
            Assert.That(Step(playback, 0.2, true, plan, 0.505f), Is.EqualTo(RadioPlayback.UpdateKind.None));
            Assert.That(Step(playback, 0.4, true, plan, 0.6f), Is.EqualTo(RadioPlayback.UpdateKind.Volume));
            Assert.That(Step(playback, 0.6, true, plan, 0.6f), Is.EqualTo(RadioPlayback.UpdateKind.None));
        }

        public static void SourceIdentityIncludesStationStreamAndMuteStopsAfterReconnect()
        {
            var playback = new RadioPlayback();
            Step(playback, 0, true, RadioPlaybackPlan.Station("radio", "low"), 1);
            Assert.That(Step(playback, 0.2, true, RadioPlaybackPlan.Station("radio", "high"), 1),
                Is.EqualTo(RadioPlayback.UpdateKind.Selection));
            playback.Invalidate();
            Assert.That(Step(playback, 0.21, true, RadioPlaybackPlan.Stop, 1), Is.EqualTo(RadioPlayback.UpdateKind.Selection));
            Step(playback, 0.22, false, RadioPlaybackPlan.Stop, 1);
            Assert.That(Step(playback, 0.23, true, RadioPlaybackPlan.Stop, 1), Is.EqualTo(RadioPlayback.UpdateKind.Selection));
            Assert.That(RadioPlaybackPlan.File(null).Kind, Is.EqualTo(RadioPlaybackPlan.SourceKind.Stop));
            Assert.That(RadioPlaybackPlan.Station("radio", null).Kind, Is.EqualTo(RadioPlaybackPlan.SourceKind.Stop));
        }
    }
}
