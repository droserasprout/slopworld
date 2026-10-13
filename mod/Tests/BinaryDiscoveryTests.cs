using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class BinaryDiscoveryTests
    {
        sealed class Fixture
        {
            internal readonly List<Action<Wire.WhereIsReply>> Replies =
                new List<Action<Wire.WhereIsReply>>();
            internal readonly List<Action<string>> Failures = new List<Action<string>>();
            internal readonly List<Action> Workers = new List<Action>(),
                Completions = new List<Action>();
            internal readonly BinaryDiscovery Discovery;
            internal bool LocateFails;

            internal Fixture() =>
                Discovery = new BinaryDiscovery(
                    (ok, fail) =>
                    {
                        Replies.Add(ok);
                        Failures.Add(fail);
                    },
                    name => LocateFails ? throw new Exception("missing") : "/host/" + name,
                    Workers.Add,
                    Completions.Add
                );
        }

        static Wire.WhereIsReply DaemonReply(string path) =>
            new Wire.WhereIsReply
            {
                Binaries =
                {
                    new Wire.BinaryLocation { Name = "git", Path = path },
                },
            };

        public static void ReplacementRejectsBothDaemonAndHostCompletions()
        {
            var f = new Fixture();
            f.Discovery.Load();
            Assert.That(
                f.Discovery.Results.Single(row => row.Spec.Name == "git").Status,
                Is.EqualTo("Checking daemon")
            );
            Assert.That(
                f.Discovery.Results.Single(row => row.Spec.Name == "slopctl").Status,
                Is.EqualTo("Checking")
            );
            f.Workers[0]();
            int oldCompletions = f.Completions.Count;
            f.Discovery.Load();
            f.Replies[0](DaemonReply("/old/git"));
            f.Failures[0]("late failure");
            foreach (var complete in f.Completions)
                complete();
            Assert.That(f.Discovery.HostLoading && f.Discovery.DaemonLoading, Is.True);
            Assert.That(f.Discovery.Results.All(row => !row.Resolved), Is.True);
            f.Replies[1](DaemonReply("/daemon/git"));
            f.Workers[1]();
            foreach (var complete in f.Completions.Skip(oldCompletions))
                complete();
            Assert.That(f.Discovery.HostLoading || f.Discovery.DaemonLoading, Is.False);
            Assert.That(
                f.Discovery.Results.Single(row => row.Spec.Name == "git").Path,
                Is.EqualTo("/daemon/git")
            );
            Assert.That(
                f.Discovery.Results.Single(row => row.Spec.Name == "slopctl").Path,
                Is.EqualTo("/host/slopctl")
            );
            Assert.That(
                f.Discovery.Results.Single(row => row.Spec.Name == "slopctl").Status,
                Is.Null
            );
        }

        public static void DaemonFailurePreservesHostResultsAndDistinguishesUnavailable()
        {
            var f = new Fixture();
            f.Discovery.Load();
            f.Failures[0]("offline");
            f.Workers[0]();
            foreach (var complete in f.Completions)
                complete();
            Assert.That(
                f.Discovery.Results.Single(row => row.Spec.Name == "git").Outcome,
                Is.EqualTo(BinaryDiscovery.ProbeOutcome.Unavailable)
            );
            Assert.That(
                f.Discovery.Results.Single(row => row.Spec.Name == "git").Status,
                Is.EqualTo("daemon unavailable")
            );
            Assert.That(
                f.Discovery.Results.Single(row => row.Spec.Name == "slopctl").Found,
                Is.True
            );
            Assert.That(f.Discovery.HostLoading || f.Discovery.DaemonLoading, Is.False);
        }

        public static void MissingHostCommandsDoNotPreventDaemonSuccess()
        {
            var f = new Fixture { LocateFails = true };
            f.Discovery.Load();
            f.Replies[0](DaemonReply("/daemon/git"));
            f.Workers[0]();
            foreach (var complete in f.Completions)
                complete();
            Assert.That(
                f.Discovery.Results.Single(row => row.Spec.Name == "slopctl").Outcome,
                Is.EqualTo(BinaryDiscovery.ProbeOutcome.Missing)
            );
            Assert.That(
                f.Discovery.Results.Single(row => row.Spec.Name == "slopctl").Status,
                Is.EqualTo("not found")
            );
            Assert.That(f.Discovery.Results.Single(row => row.Spec.Name == "git").Found, Is.True);
            Assert.That(f.Discovery.HostLoading, Is.False);
        }
    }
}
