using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class NativeFileTabTests
    {
        static NativeFileTab Tab() =>
            new NativeFileTab
            {
                Header = "native-reader",
                Project = "",
                OriginProject = "captured-project",
                OriginLabel = "readme.md",
                Path = "/checkout/readme.md",
                PreviewRoot = "/checkout",
                View = new MarkdownPreview("# Readme", "readme.md"),
            };

        public static void OnlyMatchingVisiblePreviewCanBePinned()
        {
            var showing = WorkspaceWindow.Showing;
            try
            {
                var tab = Tab();
                WorkspaceWindow.Showing = null;
                Assert.That(tab.LockPreview(null, tab.Path), Is.False);
                WorkspaceWindow.OpenContent(tab.View);
                Assert.That(tab.LockPreview("other", tab.Path), Is.False);
                Assert.That(tab.LockPreview(null, "/other.md"), Is.False);
                Assert.That(tab.Locked, Is.False);
                Assert.That(tab.LockPreview(null, tab.Path), Is.True);
                WorkspaceWindow.Showing = null;
                Assert.That(tab.LockPreview(null, tab.Path), Is.True, "existing pin is reusable");
                Assert.That(tab.Reopen(), Is.True);
                Assert.That(WorkspaceWindow.Showing, Is.SameAs(tab.View));
            }
            finally
            {
                WorkspaceWindow.Showing = showing;
            }
        }

        public static void ClosingRequiresExactIdentityAndRetiresPinnedReader()
        {
            var showing = WorkspaceWindow.Showing;
            try
            {
                foreach (bool visible in new[] { false, true })
                {
                    var tab = Tab();
                    var view = tab.View;
                    WorkspaceWindow.Showing = visible ? view : null;
                    tab.Lock();
                    Assert.That(tab.CloseTab(null), Is.False);
                    Assert.That(tab.CloseTab("other"), Is.False);
                    tab.CloseIf(tab.Session);
                    Assert.That(
                        tab.View,
                        Is.SameAs(view),
                        "daemon session closure does not own native tabs"
                    );
                    Assert.That(tab.FilePath, Is.EqualTo("/checkout/readme.md"));
                    Assert.That(tab.CloseTab(tab.Session), Is.True);
                    AssertRetired(tab);
                    Assert.That(tab.CloseTab("native-reader"), Is.False);
                    tab.Release();
                    tab.Invalidate();
                    AssertRetired(tab);
                }
            }
            finally
            {
                WorkspaceWindow.Showing = showing;
            }
        }

        public static void InvalidatingLiveReaderClearsItsBoundaryAndPin()
        {
            var tab = Tab();
            tab.Lock();
            tab.Invalidate();
            AssertRetired(tab);
        }

        static void AssertRetired(NativeFileTab tab)
        {
            Assert.That(tab.Alive || tab.Locked, Is.False);
            Assert.That(tab.View, Is.Null);
            Assert.That(tab.Session, Is.Null);
            Assert.That(tab.Path, Is.Null);
            Assert.That(tab.FilePath, Is.Null);
            Assert.That(tab.PreviewRoot, Is.Null);
            Assert.That(tab.Reopen(), Is.False);
            Assert.That(tab.Lock(), Is.False);
            Assert.That(tab.LockPreview(null, "/checkout/readme.md"), Is.False);
        }

        public static void HeaderRetainsCapturedOriginAfterCheckoutDisappears()
        {
            var hub = SessionHub.Instance;
            var scope = new BrowseScope
            {
                ProjectId = "native-header-project",
                Project = "renamed-project",
                Worktree = "feature",
                Name = "Feature checkout",
            };
            SidebarScopes.TestScopes.Add(scope);
            try
            {
                SessionHub.Instance = new SessionHub();
                SessionHub.Instance.Projects.Add(new ProjectInfo { Name = scope.Project });
                var tab = Tab();
                tab.Project = scope.Key;
                var info = tab.HeaderInfo();
                Assert.That(info.Project, Is.EqualTo(scope.Project));
                Assert.That(info.WorktreeName, Is.EqualTo(scope.Name));
                SidebarScopes.TestScopes.Remove(scope);
                info = tab.HeaderInfo();
                Assert.That(info.Project, Is.EqualTo(tab.OriginProject));
                Assert.That(info.WorktreeName, Is.EqualTo(""));
                Assert.That(info.Worktree, Is.EqualTo("feature"));
                Assert.That(info.Name, Is.EqualTo(tab.Session));
                Assert.That(info.Label, Is.EqualTo(tab.OriginLabel));
                Assert.That(info.ReaderScope, Is.EqualTo(scope.Key));
                Assert.That(info.ReaderPath, Is.EqualTo(tab.FilePath));
                Assert.That(info.ReaderKey, Is.EqualTo(tab.FilePath));
                Assert.That(info.Intent, Is.EqualTo("view"));
                Assert.That(info.Alive && info.Ephemeral, Is.True);
            }
            finally
            {
                SidebarScopes.TestScopes.Remove(scope);
                SessionHub.Instance = hub;
            }
        }
    }
}
