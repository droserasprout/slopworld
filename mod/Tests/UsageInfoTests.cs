using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class UsageInfoTests
    {
        public static void RepeatedPollKeepsMonotonicAnchorAcrossWallClockChanges()
        {
            var snapshot = new Wire.UsageSnapshot
            {
                Ok = true,
                SourceFetchedMs = { { "provider", 1_000_000 } },
                Rows =
                {
                    new Wire.UsageRow { Key = "quota", Provider = "provider" },
                },
            };
            var previous = UsageInfo.FromWire(snapshot, 100, 1_010_000);
            AssertEx.Equal(
                10f,
                previous.AgeFor("quota", 100),
                "initial provider timestamp accounts for transit age"
            );
            var repeated = UsageInfo.FromWire(snapshot, 120, 2_000_000, previous);
            AssertEx.Equal(
                30f,
                repeated.AgeFor("quota", 120),
                "same poll keeps its original monotonic anchor"
            );
            AssertEx.Equal(
                0f,
                repeated.AgeFor("quota", 80),
                "clock before the anchor never produces a negative age"
            );
        }

        public static void PartialFailureKeepsIndependentProviderCountdowns()
        {
            const ulong unixMs = 1_000_000;
            var snapshot = new Wire.UsageSnapshot
            {
                Ok = true,
                Sources = { "a", "b" },
                SourceFetchedMs = { { "a", unixMs }, { "b", unixMs } },
                Rows =
                {
                    new Wire.UsageRow
                    {
                        Key = "a",
                        Provider = "a",
                        Window = new Wire.UsageWindow { Key = "a", ResetsIn = 100 },
                    },
                    new Wire.UsageRow
                    {
                        Key = "b",
                        Provider = "b",
                        Window = new Wire.UsageWindow { Key = "b", ResetsIn = 100 },
                    },
                },
            };
            float now = 100;
            var previous = UsageInfo.FromWire(snapshot, now, unixMs);
            now = 140;
            snapshot.Ok = false;
            snapshot.FailedSources.Add("a");
            snapshot.SourceFetchedMs["b"] = unixMs + 40000;
            var partial = UsageInfo.FromWire(snapshot, now, unixMs + 40000, previous);
            AssertEx.Equal(previous.Heard, partial.Heard, "aggregate timestamp preserved");
            AssertEx.Equal(
                60L,
                partial.Remaining(partial.Row("a").Window, now),
                "failed provider retains anchor"
            );
            AssertEx.Equal(
                100L,
                partial.Remaining(partial.Row("b").Window, now),
                "healthy provider gets fresh anchor"
            );
            now = 150;
            var repeated = UsageInfo.FromWire(snapshot, now, unixMs + 50000, partial);
            AssertEx.Equal(
                50L,
                repeated.Remaining(repeated.Row("a").Window, now),
                "retained countdown continues"
            );
            AssertEx.Equal(
                90L,
                repeated.Remaining(repeated.Row("b").Window, now),
                "unchanged healthy poll is not refreshed twice"
            );
        }

        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("keeps provider failure local", KeepsProviderFailureLocal);
            yield return ("reads windows and their budget fields", ReadsWindowsAndBudgetFields);
            yield return ("keeps the previous timestamp on failure", KeepsPreviousTimestamp);
            yield return ("keeps missing usage values explicit", KeepsMissingValuesExplicit);
        }

        static void KeepsProviderFailureLocal()
        {
            const float now = 0;
            var usage = UsageInfo.FromWire(
                ProtobufFixtures.Read<Wire.UsageSnapshot>(
                    JVal.Parse(
                        "{\"ok\":false,\"error\":\"Claude failed\",\"sources\":[\"anthropic\",\"openai\"],"
                            + "\"failed_sources\":[\"anthropic\"],\"windows\":[]}"
                    )
                ),
                now,
                1_000_000
            );

            AssertEx.True(usage.SourceFailed("anthropic"), "failed provider is stale");
            AssertEx.False(usage.SourceFailed("openai"), "healthy provider stays live");
        }

        static void ReadsWindowsAndBudgetFields()
        {
            float now = 100f;
            var usage = UsageInfo.FromWire(
                ProtobufFixtures.Read<Wire.UsageSnapshot>(
                    JVal.Parse(
                        "{"
                            + "\"ok\":true,\"error\":null,\"plan\":\"pro\", "
                            + "\"sources\":[\"anthropic\",\"openai\"],"
                            + "\"failed_sources\":[\"anthropic\"],"
                            + "\"windows\":["
                            + "{\"key\":\"extra\",\"label\":\"Extra\",\"pct\":40,"
                            + "\"unit\":\"usd\",\"amount\":2.5,\"limit\":10,\"resets_in\":30},"
                            + "{\"key\":\"daily\",\"label\":\"Daily\",\"pct\":75}"
                            + "]}"
                    )
                ),
                now,
                1_000_000
            );

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

            now = 107f;
            AssertEx.Equal(7f, usage.Age(now), "snapshot age");
            AssertEx.Equal(
                23L,
                usage.Remaining(usage.Windows[0], now),
                "countdown ages from heard time"
            );
            AssertEx.Equal(
                -1L,
                usage.Remaining(usage.Windows[1], now),
                "unknown reset stays unknown"
            );
            AssertEx.True(usage.SourceFailed("anthropic"), "failed source lookup");
            AssertEx.False(usage.SourceFailed("openai"), "healthy source lookup");

            now = 200f;
            AssertEx.Equal(
                0L,
                usage.Remaining(new UsageWindow { ResetsIn = 2 }, now),
                "expired countdown is floored at zero"
            );
        }

        static void KeepsPreviousTimestamp()
        {
            float now = 50f;
            var previous = UsageInfo.FromWire(
                ProtobufFixtures.Read<Wire.UsageSnapshot>(
                    JVal.Parse(
                        "{\"ok\":true,\"plan\":\"old\",\"sources\":[],"
                            + "\"failed_sources\":[],\"windows\":[]}"
                    )
                ),
                now,
                1_000_000
            );
            now = 90f;

            var failed = UsageInfo.FromWire(
                ProtobufFixtures.Read<Wire.UsageSnapshot>(
                    JVal.Parse(
                        "{\"ok\":false,\"error\":\"provider down\",\"plan\":\"old\","
                            + "\"sources\":[\"openai\"],\"failed_sources\":[\"openai\"],"
                            + "\"windows\":[]}"
                    )
                ),
                now,
                1_000_000,
                previous
            );

            AssertEx.False(failed.Ok, "failed snapshot status");
            AssertEx.Equal("provider down", failed.Error, "failure message");
            AssertEx.Equal(50f, failed.Heard, "failure keeps the last good timestamp");
            AssertEx.False(failed.Any, "failed empty windows remain empty");
        }

        static void KeepsMissingValuesExplicit()
        {
            const float now = 0;
            var usage = UsageInfo.FromWire(
                ProtobufFixtures.Read<Wire.UsageSnapshot>(
                    JVal.Parse(
                        "{\"ok\":false,\"sources\":[\"anthropic\"],"
                            + "\"catalog\":[{"
                            + "\"key\":\"claude_session\","
                            + "\"label\":\"Claude session\","
                            + "\"provider\":\"anthropic\","
                            + "\"unit\":\"pct\","
                            + "\"rank\":0,"
                            + "\"default_poll\":true}],"
                            + "\"rows\":[{"
                            + "\"key\":\"claude_session\","
                            + "\"label\":\"Claude session\","
                            + "\"provider\":\"anthropic\","
                            + "\"unit\":\"pct\","
                            + "\"rank\":0,"
                            + "\"poll\":true,"
                            + "\"stale\":true,"
                            + "\"window\":null}],"
                            + "\"windows\":[]}"
                    )
                ),
                now,
                1_000_000
            );
            AssertEx.Equal(1, usage.Rows.Count, "daemon row remains visible");
            AssertEx.True(usage.Rows[0].Window == null, "missing value is not fabricated");
            AssertEx.True(usage.Rows[0].Stale, "stale state survives");
        }
    }
}
