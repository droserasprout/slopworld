using System.Linq;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class ContentTreeGroupTests
    {
        static BrowseScope Scope(string project, string checkout, string branch) =>
            new BrowseScope
            {
                ProjectId = project,
                Project = project,
                Worktree = checkout,
                Name = checkout,
                Path = "/" + project + "/" + checkout,
                Branch = branch,
            };

        static ContentTreeGroup Group(BrowseScope scope) =>
            new ContentTreeGroup(
                scope.Key,
                ContentTreeGroup.CheckoutLabel(scope, scope.Branch),
                scope.Path,
                scope,
                null
            );

        public static void LoneRootUsesProjectFoldAndPreservesActionIdentity()
        {
            var root = Scope("project", "main", "trunk");
            var group = ContentTreeGroup.Checkouts(new[] { root }, Group).Single();
            Assert.That(group.Key, Is.EqualTo(root.ProjectKey));
            Assert.That(group.ScopeKey, Is.EqualTo(root.Key));
            Assert.That(group.Label, Is.EqualTo("project"));
            Assert.That(group.ParentKey, Is.Null);
            Assert.That(group.MainCheckout, Is.True);
            Assert.That(group.Value, Is.SameAs(root));
            Assert.That(group.Path, Is.EqualTo(root.Path));
        }

        public static void CheckoutFoldsSurviveMergingWithoutHidingTheMergedRoot()
        {
            var root = Scope("project", "main", "trunk");
            var branch = Scope("project", "feature", "topic");
            var selected = new[] { root, branch };
            var controller = new ContentTreeController(() =>
                ContentTreeGroup.Checkouts(selected, Group)
            );
            var groups = controller.Groups();
            Assert.That(groups.Count, Is.EqualTo(3));
            Assert.That(groups[0].MainCheckout, Is.False);
            Assert.That(groups[1].Label, Is.EqualTo("trunk"));
            Assert.That(groups[2].Label, Is.EqualTo("feature  · topic"));
            Assert.That(groups[1].ParentKey, Is.EqualTo(root.ProjectKey));
            controller.SetGroupCollapsed(root.Key, true);
            Assert.That(controller.IsScopeUnfolded(root.Key), Is.False);

            selected = new[] { root };
            Assert.That(controller.IsScopeUnfolded(root.Key), Is.True);
            controller.SetGroupCollapsed(root.ProjectKey, true);
            Assert.That(controller.IsScopeUnfolded(root.Key), Is.False);
            controller.SetAllFolded(false);
            Assert.That(controller.IsScopeUnfolded(root.Key), Is.True);

            selected = new[] { root, branch };
            Assert.That(controller.IsScopeUnfolded(root.Key), Is.False);
            Assert.That(controller.IsScopeUnfolded(branch.Key), Is.True);
        }

        public static void LoneNonRootKeepsCheckoutHeadingAndProjectsMergeIndependently()
        {
            var branch = Scope("one", "feature", "topic");
            var root = Scope("two", "main", "trunk");
            var groups = ContentTreeGroup.Checkouts(new[] { branch, root }, Group);
            Assert.That(groups.Count, Is.EqualTo(3));
            Assert.That(groups[1].ParentKey, Is.EqualTo(branch.ProjectKey));
            Assert.That(groups[1].MainCheckout, Is.False);
            Assert.That(groups[2].Key, Is.EqualTo(root.ProjectKey));
            Assert.That(groups[2].MainCheckout, Is.True);
        }

        public static void GitRetainsRootBranchOrCommitInMergedAndNestedHeadings()
        {
            var root = Scope("project", "main", "trunk");
            var branch = Scope("project", "feature", "topic");
            Assert.That(
                ContentTreeGroup
                    .Checkouts(new[] { root }, Group, showRootBranch: true)
                    .Single()
                    .Label,
                Is.EqualTo("project  · trunk")
            );
            root.Branch = "a1b2c3d";
            Assert.That(
                ContentTreeGroup
                    .Checkouts(new[] { root, branch }, Group, showRootBranch: true)[1]
                    .Label,
                Is.EqualTo("· a1b2c3d")
            );
            root.Branch = null;
            Assert.That(
                ContentTreeGroup
                    .Checkouts(new[] { root }, Group, showRootBranch: true)
                    .Single()
                    .Label,
                Is.EqualTo("project")
            );
        }
    }
}
