using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class UsageInfoTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("keeps provider failure local", KeepsProviderFailureLocal);
            yield return ("reads windows and their budget fields", ReadsWindowsAndBudgetFields);
            yield return ("keeps the previous timestamp on failure", KeepsPreviousTimestamp);
            yield return ("keeps missing usage values explicit", KeepsMissingValuesExplicit);
        }

        static void KeepsProviderFailureLocal()
        {
            var usage = UsageInfo.FromWire(ProtobufFixtures.Read<Wire.UsageSnapshot>(JVal.Parse(
                "{\"ok\":false,\"error\":\"Claude failed\",\"sources\":[\"anthropic\",\"openai\"]," +
                "\"failed_sources\":[\"anthropic\"],\"windows\":[]}")));

            AssertEx.True(usage.SourceFailed("anthropic"), "failed provider is stale");
            AssertEx.False(usage.SourceFailed("openai"), "healthy provider stays live");
        }

        static void ReadsWindowsAndBudgetFields()
        {
            UnityEngine.Time.realtimeSinceStartup = 100f;
            var usage = UsageInfo.FromWire(ProtobufFixtures.Read<Wire.UsageSnapshot>(JVal.Parse(
                "{" +
                "\"ok\":true,\"error\":null,\"plan\":\"pro\", " +
                "\"sources\":[\"anthropic\",\"openai\"]," +
                "\"failed_sources\":[\"anthropic\"]," +
                "\"windows\":[" +
                "{\"key\":\"extra\",\"label\":\"Extra\",\"pct\":40," +
                "\"unit\":\"usd\",\"amount\":2.5,\"limit\":10,\"resets_in\":30}," +
                "{\"key\":\"daily\",\"label\":\"Daily\",\"pct\":75}" +
                "]}")));

            AssertEx.True(usage.Ok, "successful usage snapshot");
            AssertEx.True(usage.Error == null, "successful snapshot has no error");
            AssertEx.Equal("pro", usage.Plan, "plan");
            AssertEx.Sequence(new[] { "anthropic", "openai" }, usage.Sources, "sources");
            AssertEx.Sequence(new[] { "anthropic" }, usage.FailedSources, "failed sources");
            AssertEx.True(usage.Any, "windows make the snapshot non-empty");
            AssertEx.Equal(2, usage.Windows.Count, "window count");
            AssertEx.Equal("extra", usage.Windows[0].Key, "window key");
            AssertEx.Equal("Extra", usage.Windows[0].Label, "window label");
            AssertEx.Equal(40f, usage.Windows[0].Pct, "window percentage");
            AssertEx.True(usage.Windows[0].IsMoney, "budget window has money fields");
            AssertEx.Equal(2.5f, usage.Windows[0].Amount, "budget amount");
            AssertEx.Equal(10f, usage.Windows[0].Limit, "budget limit");
            AssertEx.Equal(30L, usage.Windows[0].ResetsIn, "reset duration");

            AssertEx.Equal("pct", usage.Windows[1].Unit, "missing unit defaults to percentage");
            AssertEx.Equal(-1f, usage.Windows[1].Amount, "missing amount sentinel");
            AssertEx.Equal(-1f, usage.Windows[1].Limit, "missing limit sentinel");
            AssertEx.Equal(-1L, usage.Windows[1].ResetsIn, "missing reset sentinel");
            AssertEx.False(usage.Windows[1].IsMoney, "percentage window is not money");

            UnityEngine.Time.realtimeSinceStartup = 107f;
            AssertEx.Equal(7f, usage.Age, "snapshot age");
            AssertEx.Equal(23L, usage.Remaining(usage.Windows[0]), "countdown ages from heard time");
            AssertEx.Equal(-1L, usage.Remaining(usage.Windows[1]), "unknown reset stays unknown");
            AssertEx.True(usage.SourceFailed("anthropic"), "failed source lookup");
            AssertEx.False(usage.SourceFailed("openai"), "healthy source lookup");

            UnityEngine.Time.realtimeSinceStartup = 200f;
            AssertEx.Equal(0L, usage.Remaining(new UsageWindow { ResetsIn = 2 }),
                           "expired countdown is floored at zero");
        }

        static void KeepsPreviousTimestamp()
        {
            UnityEngine.Time.realtimeSinceStartup = 50f;
            var previous = UsageInfo.FromWire(ProtobufFixtures.Read<Wire.UsageSnapshot>(JVal.Parse(
                "{\"ok\":true,\"plan\":\"old\",\"sources\":[]," +
                "\"failed_sources\":[],\"windows\":[]}")));
            UnityEngine.Time.realtimeSinceStartup = 90f;

            var failed = UsageInfo.FromWire(ProtobufFixtures.Read<Wire.UsageSnapshot>(JVal.Parse(
                "{\"ok\":false,\"error\":\"provider down\",\"plan\":\"old\"," +
                "\"sources\":[\"openai\"],\"failed_sources\":[\"openai\"]," +
                "\"windows\":[]}")), previous);

            AssertEx.False(failed.Ok, "failed snapshot status");
            AssertEx.Equal("provider down", failed.Error, "failure message");
            AssertEx.Equal(50f, failed.Heard, "failure keeps the last good timestamp");
            AssertEx.False(failed.Any, "failed empty windows remain empty");
        }

        static void KeepsMissingValuesExplicit()
        {
            var usage = UsageInfo.FromWire(ProtobufFixtures.Read<Wire.UsageSnapshot>(JVal.Parse(
                "{\"ok\":false,\"sources\":[\"anthropic\"]," +
                "\"catalog\":[{\"key\":\"claude_session\",\"label\":\"Claude session\",\"provider\":\"anthropic\",\"unit\":\"pct\",\"rank\":0,\"default_poll\":true}]," +
                "\"rows\":[{\"key\":\"claude_session\",\"label\":\"Claude session\",\"provider\":\"anthropic\",\"unit\":\"pct\",\"rank\":0,\"poll\":true,\"stale\":true,\"window\":null}]," +
                "\"windows\":[]}")));
            AssertEx.Equal(1, usage.Rows.Count, "daemon row remains visible");
            AssertEx.True(usage.Rows[0].Window == null, "missing value is not fabricated");
            AssertEx.True(usage.Rows[0].Stale, "stale state survives");
        }
    }
}
