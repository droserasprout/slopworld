using System;
using System.Collections.Generic;
using Google.Protobuf;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class GitStoreTests
    {
        sealed class Request
        {
            internal string Url;
            internal Action<Wire.GitResult> Ok;
            internal Action<string> Fail;
        }

        sealed class Fixture
        {
            internal string Directory = "/checkout";
            internal float Now;
            internal readonly List<Request> Requests = new List<Request>();
            internal readonly GitStore Store;

            internal Fixture()
            {
                Store = new GitStore(
                    _ => Directory,
                    _ => true,
                    () => Now,
                    (url, ok, fail, timeout) =>
                        Requests.Add(
                            new Request
                            {
                                Url = url,
                                Ok = ok,
                                Fail = fail,
                            }
                        )
                );
            }
        }

        static Wire.GitResult Status(string status = " M", bool counts = false) =>
            new Wire.GitResult
            {
                Repo = true,
                Root = "/checkout",
                Branch = "main",
                Changed = 1,
                CountsComplete = counts,
                Added = counts ? 3UL : 0UL,
                Files =
                {
                    new Wire.GitFile { Path = "a/b/file", Status = status },
                },
            };

        public static void UnchangedStatusRetainsTreeAndCounts()
        {
            var f = new Fixture();
            f.Store.Fetch("p");
            f.Requests[0].Ok(Status());
            var counts = Status(counts: true);
            counts.Files[0].Added = 3;
            counts.Files[0].Deleted = 0;
            f.Requests[1].Ok(counts);
            var repo = f.Store.Get("p");
            var tree = repo.Tree;
            Assert.That(tree.Kids[0].Name, Is.EqualTo("a/b"));
            Assert.That(tree.Kids[0].Kids[0].Depth, Is.EqualTo(1));
            Assert.That(tree.Kids[0].Kids[0].Added, Is.EqualTo(3));
            f.Store.Fetch("p");
            f.Requests[2].Ok(Status());
            f.Requests[3].Fail("counting unavailable");
            Assert.That(repo.Tree, Is.SameAs(tree));
            Assert.That(repo.CountsComplete, Is.True);
            Assert.That(repo.Added, Is.EqualTo(3));
            Assert.That(repo.Error, Is.Null);
        }

        public static void CountsCannotOverwriteNewerStatusOrDifferentPathSet()
        {
            var f = new Fixture();
            f.Store.Fetch("p");
            f.Requests[0].Ok(Status());
            f.Store.Fetch("p");
            f.Requests[2].Ok(Status("M "));
            f.Requests[1].Ok(Status(counts: true));
            var repo = f.Store.Get("p");
            Assert.That(repo.CountsComplete, Is.False);
            Assert.That(repo.Changes["a/b/file"], Is.EqualTo("M "));
            var changedPaths = Status("M ", counts: true);
            changedPaths.Files[0].Path = "other";
            f.Requests[3].Ok(changedPaths);
            Assert.That(repo.CountsComplete, Is.False);
            Assert.That(repo.Tree.Kids[0].Kids[0].Added, Is.EqualTo(-1));
        }

        public static void MovedDirectoryRejectsOldCallbacksAndRemovedScopesStayRemoved()
        {
            var f = new Fixture();
            f.Store.Fetch("p");
            var old = f.Store.Get("p");
            f.Directory = "/new";
            f.Store.Fetch("p");
            var current = f.Store.Get("p");
            f.Requests[0].Ok(Status());
            Assert.That(old.Tree, Is.Null);
            Assert.That(current.Tree, Is.Null);
            f.Store.Invalidate(_ => false);
            f.Requests[1].Ok(Status());
            Assert.That(f.Store.Known("p"), Is.Null);
            Assert.That(current.Tree, Is.Null);
        }

        public static void RefreshWaitersSettleAfterFollowUpAndFailuresRecover()
        {
            var f = new Fixture();
            int settled = 0;
            f.Store.Fetch("p", () => settled++);
            f.Store.Fetch("p", () => settled++);
            f.Requests[0].Ok(Status());
            Assert.That(settled, Is.Zero);
            Assert.That(f.Requests.Count, Is.EqualTo(2), "follow-up replaces optional counting");
            f.Requests[1].Fail("offline");
            Assert.That(settled, Is.EqualTo(2));
            Assert.That(f.Store.Known("p"), Is.Null);
            Assert.That(f.Store.Get("p").Changes, Is.Empty);
            f.Now = 6;
            f.Store.RefreshIfDue(new[] { "p" });
            f.Requests[2].Ok(Status());
            Assert.That(f.Store.Known("p"), Is.Not.Null);
            Assert.That(f.Store.Get("p").Error, Is.Null);
        }

        public static void StatusAndCountsShareFourRequestSlots()
        {
            var f = new Fixture();
            for (int i = 0; i < 6; i++)
                f.Store.Fetch("p" + i);
            Assert.That(f.Requests.Count, Is.EqualTo(4), "only four requests reach transport");
            f.Requests[0].Ok(Status());
            Assert.That(f.Requests.Count, Is.EqualTo(5), "completion admits one queued status");
            Assert.That(f.Requests[4].Url, Does.Contain("counts=false"));
            Assert.That(f.Store.Get("p0").Loading, Is.False, "status settles before counts");
            f.Requests[1].Ok(new Wire.GitResult { Repo = false });
            Assert.That(f.Requests.Count, Is.EqualTo(6));
            Assert.That(f.Requests[5].Url, Does.Contain("counts=false"));
            f.Requests[2].Fail("offline");
            Assert.That(f.Requests.Count, Is.EqualTo(7), "failure releases a slot for counts");
            Assert.That(f.Requests[6].Url, Does.Not.Contain("counts=false"));
            Assert.That(f.Store.Get("p2").Error, Is.EqualTo("offline"));
            f.Requests[3].Ok(new Wire.GitResult { Repo = false });
            f.Requests[4].Ok(new Wire.GitResult { Repo = false });
            f.Requests[5].Ok(new Wire.GitResult { Repo = false });
            f.Requests[6].Ok(Status(counts: true));
            Assert.That(f.Store.Get("p0").CountsComplete, Is.True);
            Assert.That(f.Requests.Count, Is.EqualTo(7), "queue drained");
        }

        public static void InvalidationSkipsQueuedStatusAndCountsUntilRunningWorkDrains()
        {
            var f = new Fixture();
            for (int i = 0; i < 5; i++)
                f.Store.Fetch("p" + i);
            f.Requests[0].Ok(Status());
            // p4 now holds the freed slot; p0's optional counts are queued behind it.
            f.Store.Fetch("removed");
            var removed = f.Store.Get("removed");
            var old = f.Store.Get("p1");
            f.Store.Invalidate(_ => false);
            f.Store.Invalidate(_ => false);
            f.Store.Fetch("p1");
            var replacement = f.Store.Get("p1");
            Assert.That(f.Requests.Count, Is.EqualTo(5), "invalidation cannot free running slots");
            f.Requests[1].Ok(Status());
            Assert.That(
                f.Requests.Count,
                Is.EqualTo(6),
                "stale queue entries skipped for replacement"
            );
            Assert.That(f.Requests[5].Url, Does.Contain("counts=false"));
            Assert.That(old.Tree, Is.Null, "late old status ignored");
            Assert.That(removed.Loading, Is.False);
            Assert.That(f.Store.Known("removed"), Is.Null);
            f.Requests[2].Fail("old error");
            f.Requests[3].Ok(new Wire.GitResult { Repo = false });
            f.Requests[4].Ok(Status());
            Assert.That(f.Requests.Count, Is.EqualTo(6), "old callbacks cannot enqueue counts");
            Assert.That(replacement.Error, Is.Null);
            f.Requests[5].Ok(Status());
            f.Requests[6].Ok(Status(counts: true));
            Assert.That(replacement.CountsComplete, Is.True, "replacement remains usable");
        }

        public static void FailedCountsPreserveStatusReleaseCapacityAndRetryOnNextPoll()
        {
            var f = new Fixture();
            f.Store.Fetch("p");
            f.Requests[0].Ok(Status());
            var repo = f.Store.Get("p");
            var tree = repo.Tree;
            for (int i = 0; i < 4; i++)
                f.Store.Fetch("other" + i);
            Assert.That(f.Requests.Count, Is.EqualTo(5), "count request consumes one slot");
            f.Requests[1].Fail("count timeout");
            Assert.That(f.Requests.Count, Is.EqualTo(6), "count failure admits queued status");
            Assert.That(f.Store.Known("p"), Is.SameAs(repo));
            Assert.That(repo.Tree, Is.SameAs(tree));
            Assert.That(repo.Error, Is.Null);
            Assert.That(repo.CountsComplete, Is.False);
            Assert.That(tree.Kids[0].Kids[0].Added, Is.EqualTo(-1));
            for (int i = 2; i < 6; i++)
                f.Requests[i].Ok(new Wire.GitResult { Repo = false });
            f.Now = 4;
            f.Store.RefreshIfDue(new[] { "p" });
            Assert.That(f.Requests.Count, Is.EqualTo(6), "poll waits for deadline");
            f.Now = 5;
            f.Store.RefreshIfDue(new[] { "p" });
            f.Requests[6].Ok(Status());
            var counts = Status(counts: true);
            counts.Files[0].Added = 3;
            f.Requests[7].Ok(counts);
            Assert.That(repo.Tree, Is.SameAs(tree));
            Assert.That(repo.CountsComplete, Is.True);
            Assert.That(tree.Kids[0].Kids[0].Added, Is.EqualTo(3));
        }

        public static void TruncatedStatusRemainsPartialWithoutRequestingCounts()
        {
            var f = new Fixture();
            f.Store.Fetch("p");
            var status = Status();
            status.Truncated = true;
            f.Requests[0].Ok(status);
            var repo = f.Store.Get("p");
            Assert.That(f.Requests.Count, Is.EqualTo(1));
            Assert.That(repo.Truncated, Is.True);
            Assert.That(repo.CountsComplete, Is.False);
            Assert.That(repo.Loading, Is.False);
            Assert.That(repo.Tree.Kids[0].Kids[0].Added, Is.EqualTo(-1));
        }

        public static void NonRepositoryClearsStatusAndLateCounts()
        {
            var f = new Fixture();
            f.Store.Fetch("p");
            f.Requests[0].Ok(Status());
            f.Store.Fetch("p");
            f.Requests[2].Ok(new Wire.GitResult { Repo = false });
            f.Requests[1].Ok(Status(counts: true));
            var repo = f.Store.Get("p");
            Assert.That(repo.IsRepo, Is.False);
            Assert.That(repo.Tree, Is.Null);
            Assert.That(repo.Changes, Is.Empty);
            Assert.That(repo.Root, Is.Null);
            Assert.That(repo.CountsComplete, Is.False);
        }
    }
}
