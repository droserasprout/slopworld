using System;
using System.Collections.Generic;
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
                Store = new GitStore(_ => Directory, _ => true, () => Now,
                    (url, ok, fail, timeout) => Requests.Add(new Request { Url = url, Ok = ok, Fail = fail }));
            }
        }

        static Wire.GitResult Status(string status = " M", bool counts = false) => new Wire.GitResult
        {
            Repo = true,
            Root = "/checkout",
            Branch = "main",
            Changed = 1,
            CountsComplete = counts,
            Added = counts ? 3UL : 0UL,
            Files = { new Wire.GitFile { Path = "a/b/file", Status = status } }
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
