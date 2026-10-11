using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class WorktreeInventoryTests
    {
        sealed class Request
        {
            internal Action<Wire.WorktreesReply> Ok;
            internal Action<string> Fail;
        }
        sealed class Fixture
        {
            internal readonly List<Request> Requests = new List<Request>();
            internal readonly WorktreeInventory Inventory;
            internal Fixture() => Inventory = new WorktreeInventory(
                (project, ok, fail) => Requests.Add(new Request { Ok = ok, Fail = fail }));
        }
        static List<ProjectInfo> Projects(string path = "/p") => new List<ProjectInfo>
        { new ProjectInfo { Name = "p", Dir = path } };
        static Wire.WorktreesReply Reply() => new Wire.WorktreesReply
        { Worktrees = { new Wire.Worktree { Id = "branch", Name = "branch", Path = "/branch" } } };

        public static void MissingMainCheckoutIsInsertedAndUnchangedInventorySharesRequests()
        {
            var f = new Fixture();
            f.Inventory.Update(Projects(), 1);
            f.Inventory.Update(Projects(), 1);
            Assert.That(f.Requests.Count, Is.EqualTo(1));
            f.Requests[0].Ok(Reply());
            var rows = f.Inventory.WorktreesByProject["p"];
            Assert.That(rows.Select(row => row.Tree.Id), Is.EqualTo(new[] { "main", "branch" }));
            Assert.That(rows[0].Tree.Path, Is.EqualTo("/p"));
            Assert.That(rows[0].Tree.Phase, Is.EqualTo("ready"));
        }

        public static void FailureRetainsRowsAndExplicitRetryClearsError()
        {
            var f = new Fixture();
            f.Inventory.Update(Projects(), 1);
            f.Requests[0].Ok(Reply());
            var previous = f.Inventory.WorktreesByProject["p"];
            f.Inventory.Invalidate();
            f.Inventory.Update(Projects(), 1);
            f.Requests[1].Fail("offline");
            Assert.That(f.Inventory.WorktreesByProject["p"], Is.SameAs(previous));
            Assert.That(f.Inventory.WorktreeErrors["p"], Is.EqualTo("p: offline"));
            f.Inventory.Invalidate();
            f.Inventory.Update(Projects(), 1);
            f.Requests[2].Ok(Reply());
            Assert.That(f.Inventory.WorktreeErrors, Is.Empty);
        }

        public static void RefreshAndRemovalRejectDelayedReplies()
        {
            var f = new Fixture();
            f.Inventory.Update(Projects(), 1);
            f.Inventory.Invalidate();
            f.Requests[0].Ok(Reply());
            Assert.That(f.Inventory.WorktreesByProject, Is.Empty);
            f.Inventory.Update(Projects(), 1);
            f.Inventory.Update(new List<ProjectInfo>(), 2);
            f.Requests[1].Ok(Reply());
            f.Requests[1].Fail("late error");
            Assert.That(f.Inventory.WorktreesByProject, Is.Empty);
            Assert.That(f.Inventory.WorktreeErrors, Is.Empty);
        }

        public static void ReplacementCapturesDirectoryAndRejectsPriorGeneration()
        {
            var f = new Fixture();
            var projects = Projects();
            f.Inventory.Update(projects, 1);
            projects[0].Dir = "/new";
            f.Inventory.Update(projects, 2);
            f.Requests[0].Ok(Reply());
            Assert.That(f.Inventory.WorktreesByProject, Is.Empty);
            projects[0].Dir = "/later";
            f.Requests[1].Ok(Reply());
            Assert.That(f.Inventory.WorktreesByProject["p"][0].Tree.Path, Is.EqualTo("/new"));
        }
    }
}
