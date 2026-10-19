namespace SlopWorld.Tests
{
    static class ReaderDismissalTests
    {
        public static void ClosingPinnedSearchCancelsDelayedAppearanceReplacement()
        {
            SessionHub.Instance = new SessionHub();
            var hub = SessionHub.Instance;
            var info = new SessionInfo
            {
                Name = "search",
                Project = "p",
                Intent = "search",
                Ephemeral = true,
                Alive = true,
                ReaderPinned = true,
                ReaderPath = "/file",
                ReaderKey = "/file",
            };
            hub.Sessions[info.Name] = info;
            var search = new PagerTabs();
            search.AttachRestored(info);
            var reader = search.FindSession(info.Name);
            reader.RestartForAppearance(reader.Session, reader.Operation);
            var replacement = hub.SessionStore.Requests[0];
            hub.SessionStore.DeferStops = true;

            AssertEx.True(
                ReaderDismissal.Close(info.Name, search.FindSession(info.Name), info),
                "owner dismissed"
            );
            AssertEx.False(reader.Locked, "dismissal releases pin");
            AssertEx.Equal<string>(null, reader.Session, "owner invalidated before daemon stop");
            AssertEx.False(Pager.SessionVisibility.Includes(info), "row retires immediately");
            hub.SessionStore.Complete(replacement, "late-search");
            AssertEx.Equal<string>(null, reader.Session, "late restart cannot resurrect reader");
            AssertEx.False(
                Pager.SessionVisibility.Includes(hub.Get("late-search")),
                "late process stays hidden"
            );
            AssertEx.Equal(2, hub.SessionStore.Stops, "old and late processes each stopped once");
        }

        public static void OnlyUnownedDisposableReadersUseStopFallback()
        {
            SessionHub.Instance = new SessionHub();
            foreach (string intent in new[] { "view", "search", "diff" })
            {
                var info = new SessionInfo
                {
                    Name = intent,
                    Intent = intent,
                    Ephemeral = true,
                    Alive = true,
                };
                AssertEx.True(
                    ReaderDismissal.Close(info.Name, null, info),
                    "adopted reader can close"
                );
                AssertEx.False(Pager.SessionVisibility.Includes(info), "fallback retires row");
            }
            foreach (
                var info in new[]
                {
                    new SessionInfo
                    {
                        Name = "editor",
                        Intent = "edit",
                        Ephemeral = true,
                    },
                    new SessionInfo
                    {
                        Name = "durable",
                        Intent = "view",
                        Ephemeral = false,
                    },
                    new SessionInfo { Name = "shell", Ephemeral = true },
                }
            )
                AssertEx.False(
                    ReaderDismissal.Close(info.Name, null, info),
                    "non-reader preserved"
                );
            AssertEx.Equal(
                3,
                SessionHub.Instance.SessionStore.Stops,
                "only disposable readers stopped"
            );
        }
    }
}
