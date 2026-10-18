using System;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class DaemonConfigEndpointTests
    {
        public static void RepeatedReadsShareOneResolutionAndNextDrawSeesEndpointChanges()
        {
            int reads = 0;
            string live = "first";
            var endpoint = new DaemonConfigEndpoint(() =>
            {
                reads++;
                return live;
            });
            endpoint.DuringDraw(() =>
            {
                for (int i = 0; i < 100; i++)
                    Assert.That(endpoint.Current, Is.EqualTo("first"));
                live = "second";
                endpoint.DuringDraw(() => Assert.That(endpoint.Current, Is.EqualTo("first")));
            });
            Assert.That(reads, Is.EqualTo(1));
            endpoint.DuringDraw(() => Assert.That(endpoint.Current, Is.EqualTo("second")));
            Assert.That(reads, Is.EqualTo(2));
        }

        public static void FreshChecksBypassDrawingSnapshotAndFailureReleasesIt()
        {
            string live = "first";
            var endpoint = new DaemonConfigEndpoint(() => live);
            Assert.Throws<InvalidOperationException>(() =>
                endpoint.DuringDraw(() =>
                {
                    live = "second";
                    Assert.That(endpoint.Current, Is.EqualTo("first"));
                    Assert.That(endpoint.Fresh, Is.EqualTo("second"));
                    throw new InvalidOperationException();
                })
            );
            Assert.That(endpoint.Current, Is.EqualTo("second"));
            live = null;
            endpoint.DuringDraw(() => Assert.That(endpoint.Current, Is.EqualTo("")));
        }
    }
}
