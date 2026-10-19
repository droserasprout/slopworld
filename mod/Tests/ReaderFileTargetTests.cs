using NUnit.Framework;

namespace SlopWorld.Tests
{
    public class ReaderFileTargetTests
    {
        [Test]
        public void CapturedScopeSurvivesProjectRenameAndCurrentCheckoutChanges()
        {
            string scope = BrowseScope.Identity("project-id", "old-checkout");
            var target = ReaderFileTarget.From(
                new SessionInfo
                {
                    Name = "opaque-handle",
                    Project = "old-name",
                    Worktree = "different-checkout",
                    ReaderScope = scope,
                    ReaderPath = "/work/old-checkout/readme.md",
                    ReaderLine = 42,
                },
                new ProjectInfo { Id = "project-id", Name = "new-name" }
            );
            Assert.That(target.Scope, Is.EqualTo(scope));
            Assert.That(target.Path, Is.EqualTo("/work/old-checkout/readme.md"));
            Assert.That(target.Line, Is.EqualTo(42));
        }

        [TestCase("", "main")]
        [TestCase("feature", "feature")]
        public void RestoredMetadataKeepsTheOriginalWorktree(string worktree, string expected)
        {
            var target = ReaderFileTarget.From(
                new SessionInfo
                {
                    Project = "project",
                    Worktree = worktree,
                    ReaderPath = "/file",
                },
                new ProjectInfo { Id = "project-id", Name = "project" }
            );
            Assert.That(target.Scope, Is.EqualTo(BrowseScope.Identity("project-id", expected)));
        }

        [Test]
        public void UnscopedStorageReadersStayUnscoped()
        {
            var target = ReaderFileTarget.From(
                new SessionInfo { ReaderPath = "/storage/file" },
                null
            );
            Assert.That(target.Scope, Is.Null.Or.Empty);
            Assert.That(target.Path, Is.EqualTo("/storage/file"));
        }

        [Test]
        public void MissingCheckoutDoesNotFallBackToMain()
        {
            string scope = BrowseScope.Identity("removed", "removed-checkout");
            var target = ReaderFileTarget.From(
                new SessionInfo { ReaderScope = scope, ReaderPath = "/removed/file" },
                null
            );
            Assert.That(target.Scope, Is.EqualTo(scope));
        }

        [Test]
        public void CommandReaderKeysAndLabelsAreNeverTreatedAsFilePaths()
        {
            Assert.That(ReaderFileTarget.From(null, null), Is.Null);
            Assert.That(
                ReaderFileTarget.From(
                    new SessionInfo
                    {
                        Name = "/session",
                        Label = "/label",
                        ReaderKey = "diff:all",
                    },
                    null
                ),
                Is.Null
            );
        }

        [Test]
        public void LargeSourceLinesStayPositive()
        {
            var target = ReaderFileTarget.From(
                new SessionInfo { ReaderPath = "/file", ReaderLine = uint.MaxValue },
                null
            );
            Assert.That(target.Line, Is.EqualTo(int.MaxValue));
        }
    }
}
