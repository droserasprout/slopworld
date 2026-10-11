using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class SearchStoreTests
    {
        sealed class Request
        {
            internal string Url;
            internal Action<Wire.SearchResult> Ok;
            internal Action<string> Fail;
        }
        sealed class Fixture
        {
            internal readonly List<Request> Requests = new List<Request>();
            internal readonly SearchStore Store;
            internal Fixture() => Store = new SearchStore(_ => true,
                (url, ok, fail) => Requests.Add(new Request { Url = url, Ok = ok, Fail = fail }));
        }
        static BrowseScope Scope(string name) => new BrowseScope
        { ProjectId = name, Project = name, Worktree = "main", Name = "Main", Path = "/" + name, Phase = "ready" };
        static SearchSubmission Query(string query) => new SearchSubmission(query, false, false, false, false);

        public static void QueryReplacementAndClearingRejectOldResults()
        {
            var f = new Fixture();
            var scopes = new[] { Scope("p") };
            f.Store.Search(Query("old"), scopes, false);
            f.Store.Search(Query("new"), scopes, false);
            f.Requests[0].Fail("old error");
            Assert.That(f.Store.Loading, Is.True);
            Assert.That(f.Store.Groups[0].Error, Is.Null);
            f.Store.Search(Query(""), scopes, false);
            f.Requests[1].Ok(new Wire.SearchResult { Matches = { new Wire.SearchMatch { Path = "late" } } });
            Assert.That(f.Store.Groups, Is.Empty);
            Assert.That(f.Store.Loading, Is.False);
        }

        public static void PartialFailureKeepsSortedResultsAndSubmittedOptions()
        {
            var f = new Fixture();
            var submitted = new SearchSubmission(" a & b ", true, true, true, true);
            f.Store.Search(submitted, new[] { Scope("p"), Scope("q") }, true);
            Assert.That(f.Requests[0].Url, Does.Contain("q=a%20%26%20b&regex=1&case=1&word=1&gitignore=0&hidden=1"));
            f.Requests[0].Ok(new Wire.SearchResult
            {
                Truncated = true,
                Matches =
                {
                    new Wire.SearchMatch { Path = "z", Line = 1 },
                    new Wire.SearchMatch { Path = "a", Line = 5 },
                    new Wire.SearchMatch { Path = "a", Line = 2, Column = 3, Text = "match" },
                }
            });
            Assert.That(f.Store.Loading, Is.True);
            f.Requests[1].Fail("unavailable");
            Assert.That(f.Store.Loading, Is.False);
            var group = f.Store.Groups[0];
            Assert.That(group.Truncated, Is.True);
            Assert.That(group.Matches[0].Line, Is.EqualTo(2));
            Assert.That(group.Matches[0].Root, Is.EqualTo("/p"));
            Assert.That(group.Matches[0].Project, Is.EqualTo(Scope("p").Key));
            Assert.That(f.Store.Groups[1].Error, Is.EqualTo("unavailable"));
            Assert.That(f.Store.Submitted, Is.SameAs(submitted));
        }

        public static void ScopeReplacementKeepsSubmissionAndRejectsOldRoots()
        {
            var f = new Fixture();
            var submission = new SearchSubmission("needle", true, true, true, true);
            f.Store.Search(submission, new[] { Scope("p"), Scope("removed") }, false);
            var moved = Scope("p");
            moved.Path = "/moved";
            f.Store.Search(submission, new[] { moved }, false);
            f.Requests[0].Ok(new Wire.SearchResult { Matches = { new Wire.SearchMatch { Path = "old" } } });
            f.Requests[1].Fail("removed checkout");
            Assert.That(f.Store.Loading, Is.True);
            Assert.That(f.Store.Groups.Count, Is.EqualTo(1));
            Assert.That(f.Store.Groups[0].Matches, Is.Empty);
            Assert.That(f.Store.Groups[0].Error, Is.Null);
            Assert.That(f.Requests[2].Url, Does.Contain("path=%2Fmoved&q=needle&regex=1&case=1&word=1&gitignore=0"));
            f.Requests[2].Ok(new Wire.SearchResult { Matches = { new Wire.SearchMatch { Path = "current" } } });
            Assert.That(f.Store.Groups[0].Matches[0].Root, Is.EqualTo("/moved"));
            Assert.That(f.Store.Submitted, Is.SameAs(submission));
            Assert.That(f.Store.Loading, Is.False);
            f.Store.Search(submission, Array.Empty<BrowseScope>(), false);
            Assert.That(f.Store.Groups, Is.Empty);
            Assert.That(f.Store.Loading, Is.False);
        }

        public static void ReplacementSkipsQueuedOldScopesWithoutExceedingBound()
        {
            var f = new Fixture();
            f.Store.Search(Query("old"), new[] { Scope("1"), Scope("2"), Scope("3"), Scope("4"), Scope("5") }, false);
            Assert.That(f.Requests.Count, Is.EqualTo(4));
            f.Store.Search(Query("new"), new[] { Scope("current") }, false);
            f.Requests[0].Ok(new Wire.SearchResult());
            Assert.That(f.Requests.Count, Is.EqualTo(5));
            Assert.That(f.Requests[4].Url, Does.Contain("q=new"));
            f.Requests[4].Ok(new Wire.SearchResult());
            Assert.That(f.Store.Loading, Is.False);
            Assert.That(f.Store.Groups.Count, Is.EqualTo(1));
        }
    }
}
