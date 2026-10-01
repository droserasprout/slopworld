using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class JukeboxPresetTests
    {
        public static void PreservesUnsignedRatesThroughEditingCopy()
        {
            var station = new Wire.Station
            {
                DefaultRate = uint.MaxValue,
                Streams = { new Wire.StationStream { Rate = uint.MaxValue, Url = "https://example.org" } },
            };
            var saved = JukeboxPresetInfo.FromWire(station).Copy().ToWire();
            Assert.That(saved.DefaultRate, Is.EqualTo(uint.MaxValue));
            Assert.That(saved.Streams[0].Rate, Is.EqualTo(uint.MaxValue));
        }

        public static void RefreshFailureDoesNotUndoAcknowledgedMutation()
        {
            foreach (string operation in new[] { "create", "update", "delete" })
            {
                DaemonClient.Requests.Clear();
                int succeeded = 0;
                string failed = null;
                if (operation == "delete")
                    JukeboxPresetStore.Remove("station", () => succeeded++, error => failed = error);
                else
                    JukeboxPresetStore.Save(new JukeboxPresetInfo { Id = "station" },
                        operation == "create", "station", () => succeeded++, error => failed = error);
                DaemonClient.Requests[0].Ok(ProtobufFixtures.Json(new Wire.Ack()));
                Assert.That(succeeded, Is.EqualTo(1), operation);
                DaemonClient.Requests[1].Fail("catalog unavailable");
                Assert.That(failed, Is.Null, operation);
                Assert.That(JukeboxPresetStore.Error, Is.EqualTo("catalog unavailable"));
                Assert.That(JukeboxPresetStore.Loading, Is.False);
            }
        }
    }
}
