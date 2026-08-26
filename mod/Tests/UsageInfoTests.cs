using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class UsageInfoTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("keeps provider failure local", KeepsProviderFailureLocal);
            yield return ("legacy global failure remains stale", LegacyGlobalFailureRemainsStale);
        }

        static void KeepsProviderFailureLocal()
        {
            var usage = UsageInfo.FromJson(JVal.Parse(
                "{\"ok\":false,\"error\":\"Claude failed\",\"sources\":[\"anthropic\",\"openai\"]," +
                "\"failed_sources\":[\"anthropic\"],\"windows\":[]}"));

            AssertEx.True(usage.SourceFailed("anthropic"), "failed provider is stale");
            AssertEx.False(usage.SourceFailed("openai"), "healthy provider stays live");
        }

        static void LegacyGlobalFailureRemainsStale()
        {
            var usage = UsageInfo.FromJson(JVal.Parse(
                "{\"ok\":false,\"error\":\"old daemon failure\",\"windows\":[]}"));

            AssertEx.True(usage.SourceFailed("anthropic"), "old daemon fallback for Claude");
            AssertEx.True(usage.SourceFailed("openai"), "old daemon fallback for OpenAI");
        }
    }
}
