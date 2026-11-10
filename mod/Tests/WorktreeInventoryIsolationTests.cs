using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class WorktreeInventoryIsolationTests
    {
        sealed class Request
        {
            internal string Project;
            internal Action<Wire.WorktreesReply> Ok;
            internal Action<string> Fail;
        }

        sealed class Fixture
        {
            internal readonly List<Request> Requests = new List<Request>();
            internal readonly WorktreeInventory Inventory;

            internal Fixture() =>
                Inventory = new WorktreeInventory(
                    (project, ok, fail) =>
                        Requests.Add(
                            new Request
                            {
                                Project = project,
                                Ok = ok,
                                Fail = fail,
                            }
                        )
                );
        }

        static List<ProjectInfo> Projects(params string[] names) =>
            names.Select(name => new ProjectInfo { Name = name, Dir = "/" + name }).ToList();

        static Wire.WorktreesReply Reply(string project, string label) =>
            new Wire.WorktreesReply
            {
                Worktrees =
                {
                    new Wire.Worktree
                    {
                        Id = "z",
                        Name = "Zulu " + label,
                        Path = "/" + project + "/z",
                    },
                    new Wire.Worktree
                    {
                        Id = "a",
                        Name = "alpha " + label,
                        Path = "/" + project + "/a",
                    },
                    new Wire.Worktree
                    {
                        Id = "main",
                        Name = "main " + label,
                        Path = "/" + project,
                    },
                },
            };

        public static void ReverseRepliesPublishOnlyTheirOwnProjectAndSortEachInventory()
        {
            var f = new Fixture();
            f.Inventory.Update(Projects("a", "b"), 1);
            Assert.That(
                f.Requests.Select(request => request.Project),
                Is.EqualTo(new[] { "a", "b" })
            );
            int revision = f.Inventory.Revision;
            f.Inventory.Update(Projects("a", "b"), 1);
            Assert.That(f.Requests.Count, Is.EqualTo(2));
            Assert.That(f.Inventory.Revision, Is.EqualTo(revision));

            var bReply = Reply("b", "B");
            f.Requests[1].Ok(bReply);
            var bRows = f.Inventory.WorktreesByProject["b"];
            Assert.That(f.Inventory.WorktreesByProject.ContainsKey("a"), Is.False);
            Assert.That(f.Inventory.Revision, Is.EqualTo(revision + 1));

            var aReply = Reply("a", "A");
            f.Requests[0].Ok(aReply);
            Assert.That(f.Inventory.WorktreesByProject["b"], Is.SameAs(bRows));
            Assert.That(f.Inventory.Revision, Is.EqualTo(revision + 2));
            foreach (string project in new[] { "a", "b" })
            {
                var rows = f.Inventory.WorktreesByProject[project];
                Assert.That(
                    rows.Select(row => row.Tree.Id),
                    Is.EqualTo(new[] { "main", "a", "z" })
                );
                Assert.That(rows.All(row => row.Project == project), Is.True);
                Assert.That(
                    rows[0].Tree,
                    Is.SameAs((project == "a" ? aReply : bReply).Worktrees[2])
                );
            }
            Assert.That(f.Inventory.WorktreeErrors, Is.Empty);
        }

        public static void MixedRefreshResultsRetainFailedRowsAndRetryClearsOnlyRecoveredError()
        {
            var f = new Fixture();
            f.Inventory.Update(Projects("a", "b"), 1);
            f.Requests[0].Ok(Reply("a", "old A"));
            f.Requests[1].Ok(Reply("b", "old B"));
            var aRows = f.Inventory.WorktreesByProject["a"];
            var bRows = f.Inventory.WorktreesByProject["b"];

            f.Inventory.Invalidate();
            f.Inventory.Update(Projects("a", "b"), 1);
            f.Requests[2].Fail("A offline");
            Assert.That(f.Inventory.WorktreesByProject["a"], Is.SameAs(aRows));
            Assert.That(f.Inventory.WorktreesByProject["b"], Is.SameAs(bRows));
            Assert.That(f.Inventory.WorktreeErrors["a"], Is.EqualTo("a: A offline"));
            f.Requests[3].Ok(Reply("b", "new B"));
            bRows = f.Inventory.WorktreesByProject["b"];
            Assert.That(bRows[0].Tree.Name, Is.EqualTo("main new B"));
            Assert.That(f.Inventory.WorktreeErrors["a"], Is.EqualTo("a: A offline"));
            Assert.That(f.Inventory.WorktreeErrors.ContainsKey("b"), Is.False);

            f.Inventory.Update(Projects("a", "b"), 1);
            Assert.That(f.Requests.Count, Is.EqualTo(4));
            f.Inventory.Invalidate();
            f.Inventory.Update(Projects("a", "b"), 1);
            f.Requests[5].Fail("B offline");
            f.Requests[4].Ok(Reply("a", "recovered A"));
            Assert.That(
                f.Inventory.WorktreesByProject["a"][0].Tree.Name,
                Is.EqualTo("main recovered A")
            );
            Assert.That(f.Inventory.WorktreesByProject["b"], Is.SameAs(bRows));
            Assert.That(f.Inventory.WorktreeErrors.Keys, Is.EquivalentTo(new[] { "b" }));
            Assert.That(f.Inventory.WorktreeErrors["b"], Is.EqualTo("b: B offline"));

            f.Inventory.Invalidate();
            f.Inventory.Update(Projects("a", "b"), 1);
            f.Requests[7].Ok(Reply("b", "recovered B"));
            f.Requests[6].Ok(Reply("a", "latest A"));
            Assert.That(f.Inventory.WorktreeErrors, Is.Empty);
            Assert.That(
                f.Inventory.WorktreesByProject["b"][0].Tree.Name,
                Is.EqualTo("main recovered B")
            );
        }

        public static void RemovingADeletesItsRowsAndErrorWhileBRemainsAndOldCallbacksAreInert()
        {
            var f = new Fixture();
            f.Inventory.Update(Projects("a", "b"), 1);
            f.Requests[0].Ok(Reply("a", "old A"));
            f.Requests[1].Ok(Reply("b", "old B"));
            var bRows = f.Inventory.WorktreesByProject["b"];
            f.Inventory.Invalidate();
            f.Inventory.Update(Projects("a", "b"), 1);
            f.Requests[2].Fail("A offline");

            f.Inventory.Update(Projects("b"), 2);
            Assert.That(f.Requests.Count, Is.EqualTo(5));
            Assert.That(f.Requests[4].Project, Is.EqualTo("b"));
            Assert.That(f.Inventory.WorktreesByProject.Keys, Is.EquivalentTo(new[] { "b" }));
            Assert.That(f.Inventory.WorktreesByProject["b"], Is.SameAs(bRows));
            Assert.That(f.Inventory.WorktreeErrors, Is.Empty);
            int revision = f.Inventory.Revision;

            // Removing A starts a new inventory generation, including B's pending request.
            f.Requests[2].Ok(Reply("a", "removed A"));
            f.Requests[2].Fail("late A error");
            f.Requests[3].Ok(Reply("b", "stale B"));
            f.Requests[3].Fail("late B error");
            Assert.That(f.Inventory.Revision, Is.EqualTo(revision));
            Assert.That(f.Inventory.WorktreesByProject.Keys, Is.EquivalentTo(new[] { "b" }));
            Assert.That(f.Inventory.WorktreesByProject["b"], Is.SameAs(bRows));
            Assert.That(f.Inventory.WorktreeErrors, Is.Empty);

            f.Requests[4].Ok(Reply("b", "current B"));
            Assert.That(
                f.Inventory.WorktreesByProject["b"][0].Tree.Name,
                Is.EqualTo("main current B")
            );
            Assert.That(f.Inventory.Revision, Is.EqualTo(revision + 1));
        }

        public static void ReaddingAAndRetryingRejectsCallbacksFromItsPriorLifetime()
        {
            var f = new Fixture();
            f.Inventory.Update(Projects("a", "b"), 1);
            f.Requests[1].Ok(Reply("b", "original B"));
            f.Inventory.Update(Projects("b"), 2);
            f.Requests[2].Ok(Reply("b", "remaining B"));
            f.Inventory.Update(Projects("a", "b"), 3);
            f.Requests[4].Ok(Reply("b", "current B"));
            f.Requests[3].Fail("new A offline");
            int revision = f.Inventory.Revision;

            f.Requests[0].Ok(Reply("a", "prior lifetime"));
            f.Requests[0].Fail("prior lifetime error");
            Assert.That(f.Inventory.Revision, Is.EqualTo(revision));
            Assert.That(f.Inventory.WorktreesByProject.ContainsKey("a"), Is.False);
            Assert.That(f.Inventory.WorktreeErrors["a"], Is.EqualTo("a: new A offline"));
            var bRows = f.Inventory.WorktreesByProject["b"];

            f.Inventory.Invalidate();
            f.Inventory.Update(Projects("a", "b"), 3);
            f.Requests[5].Ok(Reply("a", "retried A"));
            revision = f.Inventory.Revision;
            f.Requests[3].Ok(Reply("a", "before retry"));
            f.Requests[3].Fail("before retry error");
            Assert.That(f.Inventory.Revision, Is.EqualTo(revision));
            Assert.That(
                f.Inventory.WorktreesByProject["a"][0].Tree.Name,
                Is.EqualTo("main retried A")
            );
            Assert.That(f.Inventory.WorktreesByProject["b"], Is.SameAs(bRows));
            Assert.That(f.Inventory.WorktreeErrors, Is.Empty);
            f.Requests[6].Ok(Reply("b", "retried B"));
            Assert.That(
                f.Inventory.WorktreesByProject["b"][0].Tree.Name,
                Is.EqualTo("main retried B")
            );
        }
    }
}
