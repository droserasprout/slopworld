using NUnit.Framework;

namespace SlopWorld.Tests
{
    public class PanelStatusTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void OnlyDaemonReadersExposeSessionIdentity(bool hasDaemonSession)
        {
            var status = PanelStatus.From(
                new SessionInfo { Name = "reader-12", ReaderPath = "/repo/readme.md" },
                RowAct.View,
                null,
                null,
                hasDaemonSession
            );
            Assert.That(status.Tooltip.Contains("Session:"), Is.EqualTo(hasDaemonSession));
            Assert.That(status.Tooltip, Does.Contain("/repo/readme.md"));
        }

        [Test]
        public void MainReaderShowsFilenameAndRootWithCheckoutRelativePath()
        {
            var status = PanelStatus.From(
                new SessionInfo
                {
                    Name = "opaque-reader-12",
                    Intent = "edit",
                    ReaderPath = "/repo/src/TopBar.cs",
                    Title = "generated summary",
                    Project = "SlopWorld",
                },
                RowAct.Edit,
                new ProjectInfo { Name = "SlopWorld", Dir = "/repo" },
                null
            );
            Assert.That(status.Title, Is.EqualTo("TopBar.cs"));
            Assert.That(status.Context, Is.EqualTo(" · src/TopBar.cs"));
            Assert.That(status.MainCheckout, Is.True);
            Assert.That(status.Tooltip, Does.Contain("/repo/src/TopBar.cs"));
            Assert.That(status.Tooltip, Does.Contain("opaque-reader-12"));
        }

        [Test]
        public void CapturedCheckoutWinsOverSessionCheckoutAndRenamedProject()
        {
            var status = PanelStatus.From(
                new SessionInfo
                {
                    ReaderScope = BrowseScope.Identity("project-id", "feature"),
                    ReaderPath = "/feature/file.cs",
                    Project = "old-project",
                    Worktree = "main",
                },
                RowAct.View,
                new ProjectInfo { Name = "renamed-project" },
                new BrowseScope
                {
                    Project = "renamed-project",
                    Worktree = "feature",
                    Name = "Fix labels",
                    Path = "/feature",
                }
            );
            Assert.That(status.Title, Is.EqualTo("file.cs"));
            Assert.That(status.Context, Is.EqualTo("Fix labels · file.cs"));
            Assert.That(status.MainCheckout, Is.False);
            Assert.That(status.Tooltip, Does.Contain("renamed-project"));
        }

        [Test]
        public void MissingCheckoutKeepsCapturedIdentityInsteadOfShowingMain()
        {
            var status = PanelStatus.From(
                new SessionInfo
                {
                    ReaderScope = BrowseScope.Identity("p", "removed"),
                    ReaderPath = "/removed/file.cs",
                    Project = "project",
                    Worktree = "main",
                    WorktreeName = "Main worktree",
                },
                RowAct.View,
                new ProjectInfo { Name = "project" },
                null
            );
            Assert.That(status.MainCheckout, Is.False);
            Assert.That(status.Context, Is.EqualTo("removed · /removed/file.cs"));
        }

        [TestCase("diff:src/TopBar.cs", "TopBar.cs")]
        [TestCase("diff:src/UI/", "UI")]
        [TestCase("diff:.", "Working changes")]
        [TestCase("", "Working changes")]
        [TestCase("opaque-key", "Working changes")]
        public void DiffLabelsUseExplicitTargetsInsteadOfGeneratedStrings(
            string key,
            string expected
        )
        {
            var status = PanelStatus.From(
                new SessionInfo
                {
                    ReaderKey = key,
                    Title = "generated-string",
                    Label = "diff-thing",
                },
                RowAct.Diff,
                null,
                null
            );
            Assert.That(status.Title, Is.EqualTo(expected));
            Assert.That(status.Context, Is.Empty);
        }

        [Test]
        public void ShellUsesCustomNameButAgentKeepsExistingPresentation()
        {
            var shell = new SessionInfo
            {
                Name = "host-123",
                Host = true,
                Label = "Build shell",
            };
            Assert.That(
                PanelStatus.From(shell, RowAct.None, null, null).Title,
                Is.EqualTo("Build shell")
            );
            shell.Label = "";
            Assert.That(
                PanelStatus.From(shell, RowAct.None, null, null).Title,
                Is.EqualTo("Terminal")
            );
            Assert.That(
                PanelStatus.From(new SessionInfo { Name = "Agent" }, RowAct.None, null, null),
                Is.Null
            );
        }
    }
}
