using System;

namespace SlopWorld.Tests
{
    static class AudioSourceDraftTests
    {
        public static void InvalidLaterRowPreservesDraftAndRetryAllocatesUniqueKeys()
        {
            var source = new JukeboxPresetInfo { Id = "station", Name = " Station ", DefaultRate = 128 };
            source.Streams.Add(new JukeboxStreamInfo { Rate = 128, Key = "station-128", Url = " HTTPS://Example.com/Case " });
            source.Streams.Add(new JukeboxStreamInfo { Rate = 128, Url = "HTTP://example.com/Other" });
            AssertEx.False(AudioSourceDraft.TryPrepare(source, new[] { "256", "invalid" }, out var invalid, out _), "invalid row rejected");
            AssertEx.Equal<JukeboxPresetInfo>(null, invalid, "no partial candidate");
            AssertEx.Equal(128u, source.Streams[0].Rate, "earlier row unchanged");
            AssertEx.Equal(" HTTPS://Example.com/Case ", source.Streams[0].Url, "URL unchanged on failure");
            AssertEx.True(AudioSourceDraft.TryPrepare(source, new[] { "256", "128" }, out var candidate, out _), "retry succeeds");
            AssertEx.Equal("station-128", candidate.Streams[0].Key, "stable key preserved");
            AssertEx.Equal("station-128-2", candidate.Streams[1].Key, "generated key avoids existing key");
            AssertEx.Equal("https://Example.com/Case", candidate.Streams[0].Url, "only scheme canonicalized");
            AssertEx.Equal("Station", candidate.Name, "name trimmed");
        }

        public static void DuplicateKeysAndRatesAreRejected()
        {
            var source = new JukeboxPresetInfo { Id = "station", Name = "Station" };
            source.Streams.Add(new JukeboxStreamInfo { Key = "same", Url = "http://one" });
            source.Streams.Add(new JukeboxStreamInfo { Key = "same", Url = "http://two" });
            AssertEx.False(AudioSourceDraft.TryPrepare(source, new[] { "128", "256" }, out _, out _), "duplicate keys rejected");
            source.Streams[1].Key = "";
            AssertEx.False(AudioSourceDraft.TryPrepare(source, new[] { "128", "128" }, out _, out _), "duplicate rates rejected");
            AssertEx.True(AudioSourceDraft.TryPrepare(source, new[] { "128", "4294967295" }, out var candidate, out _), "unsigned maximum preserved");
            AssertEx.Equal(128u, candidate.DefaultRate, "missing default uses first preset");
        }
    }
}
