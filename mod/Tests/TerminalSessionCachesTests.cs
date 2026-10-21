using System.Collections.Generic;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class TerminalSessionCachesTests
    {
        public static void RemovedSessionsDoNotAccumulateAcrossRepeatedVisits()
        {
            var cache = new Dictionary<string, object>();
            var sessions = new HashSet<string> { "kept", "stopped" };
            var kept = new object();
            cache["kept"] = kept;
            cache["stopped"] = new object();
            for (int i = 0; i < 1000; i++)
            {
                string name = "temporary-" + i;
                sessions.Add(name);
                cache[name] = new object();
                TerminalSessionCaches.Prune(cache, sessions.Contains);
                Assert.That(cache.Count, Is.EqualTo(3));
                sessions.Remove(name);
                TerminalSessionCaches.Prune(cache, sessions.Contains);
                TerminalSessionCaches.Prune(cache, sessions.Contains);
                Assert.That(cache.Count, Is.EqualTo(2));
                Assert.That(cache["kept"], Is.SameAs(kept));
            }
            sessions.Clear();
            TerminalSessionCaches.Prune(cache, sessions.Contains);
            Assert.That(cache, Is.Empty);
        }
    }
}
