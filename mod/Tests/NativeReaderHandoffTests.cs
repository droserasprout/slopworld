using System;
using System.Linq;

namespace SlopWorld.Tests
{
    static class NativeReaderHandoffTests
    {
        public static void MarkdownRowRemainsUntilPagerOrDiffIsShown()
        {
            foreach (bool diff in new[] { false, true })
            {
                SessionHub.Instance = new SessionHub();
                var native = NativeReaders();
                var view = native.Preview.View;
                var tabs = new PagerTabs(() =>
                {
                    var retire = native.Preview.PrepareReplacement();
                    return () =>
                    {
                        AssertEx.False(
                            ReferenceEquals(view, TerminalWindow.Showing),
                            "terminal already shown before native retirement"
                        );
                        retire();
                    };
                });
                if (diff)
                    tabs.ForPreview().Open("p", "git diff", "diff", "diff:file", "", "diff");
                else
                    tabs.ForPreview().ViewFile("p", "/file", "file");
                AssertEx.Equal(1, native.All.Count(), "native row stays during launch");
                AssertEx.True(
                    ReferenceEquals(view, TerminalWindow.Showing),
                    "Markdown stays visible during launch"
                );
                SessionHub.Instance.SessionStore.Complete("replacement");
                AssertEx.Equal(0, native.All.Count(), "native row retires at handoff");
                AssertEx.Equal(1, tabs.All.Count(), "replacement supplies the single row");
                AssertEx.Equal("replacement", TerminalWindow.Current, "pager is shown");
            }
        }

        public static void FailedPagerLaunchKeepsMarkdownRowAndContent()
        {
            SessionHub.Instance = new SessionHub();
            var native = NativeReaders();
            var view = native.Preview.View;
            var tabs = new PagerTabs(() => native.Preview.PrepareReplacement());
            tabs.ForPreview().ViewFile("p", "/file", "file");
            var store = SessionHub.Instance.SessionStore;
            try
            {
                store.Fail(store.Requests[0], "launch failed");
            }
            catch (Exception error) when (error.Message == "launch failed") { }
            AssertEx.Equal(1, native.All.Count(), "failure keeps the old row");
            AssertEx.True(
                ReferenceEquals(view, TerminalWindow.Showing),
                "failure keeps Markdown visible"
            );
        }

        public static void ReturningToMarkdownCancelsPendingPagerHandoff()
        {
            SessionHub.Instance = new SessionHub();
            var native = NativeReaders();
            var view = native.Preview.View;
            var tabs = new PagerTabs(() => native.Preview.PrepareReplacement());
            tabs.ForPreview().ViewFile("p", "/file", "file");
            AssertEx.True(
                native.Reopen("p", "/readme.md"),
                "existing Markdown can be selected during launch"
            );
            tabs.ReleasePreview();
            SessionHub.Instance.SessionStore.Complete("late-pager");
            AssertEx.Equal(1, native.All.Count(), "late reply does not remove native row");
            AssertEx.True(
                ReferenceEquals(view, TerminalWindow.Showing),
                "late reply does not steal content"
            );
            AssertEx.Equal(0, tabs.All.Count(), "cancelled process is retired");
        }

        public static void PagerHandoffPreservesPinnedMarkdown()
        {
            SessionHub.Instance = new SessionHub();
            var native = NativeReaders();
            native.Lock("native-header");
            var tabs = new PagerTabs(() => native.Preview.PrepareReplacement());
            tabs.ForPreview().ViewFile("p", "/file", "file");
            SessionHub.Instance.SessionStore.Complete("pager");
            AssertEx.Equal(1, native.All.Count(), "pinned native row survives handoff");
            AssertEx.Equal(1, tabs.All.Count(), "pager has its own row");
            AssertEx.True(native.IsLocked("native-header"), "native pin survives");
        }

        public static void ReopeningPagerRetiresNativeOnlyAfterShowingTerminal()
        {
            SessionHub.Instance = new SessionHub();
            var native = new PreviewTabs<NativeFileTab>(() => new NativeFileTab());
            var tabs = new PagerTabs(() => native.Preview.PrepareReplacement());
            tabs.ForPreview().ViewFile("p", "/file", "file");
            SessionHub.Instance.SessionStore.Complete("pager");
            tabs.Lock("pager");
            ShowMarkdown(native);
            AssertEx.True(tabs.ReuseFile("p", "/file"), "existing pager reused");
            AssertEx.Equal(0, native.All.Count(), "reuse retires replaceable Markdown");
            AssertEx.Equal("pager", TerminalWindow.Current, "reuse shows existing terminal");
        }

        static PreviewTabs<NativeFileTab> NativeReaders()
        {
            var native = new PreviewTabs<NativeFileTab>(() => new NativeFileTab());
            ShowMarkdown(native);
            return native;
        }

        public static void DelayedRetirementCannotReleaseNewerNativePreviewOrPin()
        {
            var native = NativeReaders();
            var retire = native.Preview.PrepareReplacement();
            ShowMarkdown(native);
            retire();
            AssertEx.Equal(1, native.All.Count(), "replacement document owns a new lifetime");
            retire = native.Preview.PrepareReplacement();
            native.Lock("native-header");
            retire();
            AssertEx.Equal(1, native.All.Count(), "pin acquired during handoff survives");
        }

        static void ShowMarkdown(PreviewTabs<NativeFileTab> native)
        {
            var tab = native.ForPreview();
            tab.OriginLabel = "view-readme.md";
            tab.OriginProject = "p";
            tab.Project = "p";
            tab.Path = "/readme.md";
            tab.Header = "native-header";
            tab.View = new MarkdownPreview("# Markdown", "readme.md");
            TerminalWindow.OpenContent(tab.View);
        }
    }
}
