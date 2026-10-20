namespace SlopWorld.Tests
{
    static class SpotifyTests
    {
        public static void ReplacedLaunchCannotDeliverToEitherCaller()
        {
            Radio.ResetSpotifyTest();
            int first = 0,
                second = 0;
            Radio.OpenSpotify(_ => first++, _ => first++);
            Radio.ReportSpotifyTest("first");
            var staleSuccess = SessionHub.Instance.SessionStore.Refreshed;
            var staleFailure = SessionHub.Instance.SessionStore.RefreshFailed;

            Radio.OpenSpotify(_ => second++, _ => second++);
            staleSuccess();
            staleFailure("old failure");
            AssertEx.Equal(0, first, "replaced caller receives no delayed result");
            AssertEx.Equal(0, second, "old result cannot use the new caller's callbacks");

            Radio.ReportSpotifyTest("second");
            SessionHub.Instance.SessionStore.Refreshed();
            AssertEx.Equal(1, second, "current caller receives its settled launch");
        }

        public static void DuplicateLaunchKeepsOriginalCaller()
        {
            Radio.ResetSpotifyTest();
            string opened = null;
            int duplicate = 0;
            Radio.OpenSpotify(session => opened = session, _ => { });
            Radio.OpenSpotify(_ => duplicate++, _ => duplicate++);
            Radio.ReportSpotifyTest("player");
            SessionHub.Instance.SessionStore.Refreshed();
            AssertEx.Equal("player", opened, "pending launch retains its caller");
            AssertEx.Equal(0, duplicate, "duplicate request cannot replace callbacks");
        }

        public static void NonNcspotReportDoesNotCompletePendingLaunch()
        {
            Radio.ResetSpotifyTest();
            Radio.OpenSpotify(
                session => WorkspaceWindow.Open(session),
                error => UiLayout.Fail(error)
            );
            Radio.ReportSpotifyTest("unrelated", source: "radio");
            AssertEx.Equal(
                null,
                SessionHub.Instance.SessionStore.Refreshed,
                "shared production report filter rejects another source"
            );
            Radio.ReportSpotifyTest("player");
            SessionHub.Instance.SessionStore.Refreshed();
            AssertEx.Equal(
                "player",
                WorkspaceWindow.Current,
                "ncspot reply still completes pending launch"
            );
        }

        public static void SessionRefreshFailureIsReportedAndAllowsRetry()
        {
            Radio.ResetSpotifyTest();
            Radio.OpenSpotify(
                session => WorkspaceWindow.Open(session),
                error => UiLayout.Fail(error)
            );
            Radio.ReportSpotifyTest("player");
            AssertEx.Throws<System.Exception>(
                () => SessionHub.Instance.SessionStore.RefreshFailed("offline"),
                "refresh failure reaches user"
            );
            AssertEx.Equal(null, WorkspaceWindow.Current, "failed refresh opens nothing");
            Radio.OpenSpotify(
                session => WorkspaceWindow.Open(session),
                error => UiLayout.Fail(error)
            );
            Radio.ReportSpotifyTest("retry");
            SessionHub.Instance.SessionStore.Refreshed();
            AssertEx.Equal(
                "retry",
                WorkspaceWindow.Current,
                "retry completes after refresh failure"
            );
        }

        public static void MissingCapabilityFallsBackAndRejectsLateTerminalOpen()
        {
            Radio.ResetSpotifyTest();
            Radio.OpenSpotify(
                session => WorkspaceWindow.Open(session),
                error => UiLayout.Fail(error)
            );
            Radio.ReportSpotifyTest("player");
            SessionHub.Instance.Capabilities.Known = true;
            SessionHub.Instance.Capabilities.Ncspot = false;
            Radio.CapabilitiesChanged();
            AssertEx.True(!Radio.Spotify, "unavailable saved selection falls back to OST");
            SessionHub.Instance.SessionStore.Refreshed();
            AssertEx.Equal(
                null,
                WorkspaceWindow.Current,
                "capability fallback invalidates pending terminal refresh"
            );
        }

        public static void UnknownCapabilitiesKeepSpotifyAvailable()
        {
            Radio.ResetSpotifyTest();
            SessionHub.Instance.Capabilities.Ncspot = false;
            AssertEx.True(Radio.SpotifyAvailable, "wait for the capability snapshot");
            SessionHub.Instance.Capabilities.Known = true;
            AssertEx.True(!Radio.SpotifyAvailable, "known missing player disables Spotify");
            AssertEx.Throws<System.Exception>(
                () =>
                    Radio.OpenSpotify(
                        session => WorkspaceWindow.Open(session),
                        error => UiLayout.Fail(error)
                    ),
                "unavailable player cannot launch"
            );
            AssertEx.True(!Radio.Spotify, "rejected launch preserves selection");
        }

        public static void LaunchWaitsForAudioReply()
        {
            Radio.ResetSpotifyTest();
            Radio.OpenSpotify(
                session => WorkspaceWindow.Open(session),
                error => UiLayout.Fail(error)
            );
            AssertEx.True(Radio.SpotifyRequested, "launch is an ordinary pending audio selection");
            AssertEx.Equal(
                null,
                SessionHub.Instance.SessionStore.Refreshed,
                "no terminal before the reply"
            );
            Radio.ReportSpotifyTest("player");
            SessionHub.Instance.SessionStore.Refreshed();
            AssertEx.Equal(
                "player",
                WorkspaceWindow.Current,
                "the audio reply identifies the terminal"
            );
        }

        public static void QuitRejectsLateLaunchReply()
        {
            Radio.ResetSpotifyTest();
            Radio.OpenSpotify(
                session => WorkspaceWindow.Open(session),
                error => UiLayout.Fail(error)
            );
            Radio.QuitSpotifyTest();
            Radio.ReportSpotifyTest("player");
            AssertEx.Equal(
                null,
                SessionHub.Instance.SessionStore.Refreshed,
                "quit ignores a late launch reply"
            );
        }

        public static void QuitRejectsLateSessionRefresh()
        {
            Radio.ResetSpotifyTest();
            Radio.OpenSpotify(
                session => WorkspaceWindow.Open(session),
                error => UiLayout.Fail(error)
            );
            Radio.ReportSpotifyTest("player");
            Radio.QuitSpotifyTest();
            SessionHub.Instance.SessionStore.Refreshed();
            AssertEx.Equal(
                null,
                WorkspaceWindow.Current,
                "quit also invalidates an in-flight refresh"
            );
        }

        public static void SourceChangeRejectsLateSessionRefresh()
        {
            Radio.ResetSpotifyTest();
            Radio.OpenSpotify(
                session => WorkspaceWindow.Open(session),
                error => UiLayout.Fail(error)
            );
            Radio.ReportSpotifyTest("player");
            Radio.ReplaceSpotifyTest();
            SessionHub.Instance.SessionStore.Refreshed();
            AssertEx.Equal(
                null,
                WorkspaceWindow.Current,
                "a replaced selection cannot reopen its terminal"
            );
        }

        public static void StaleRefreshFailuresAreIgnored()
        {
            Radio.ResetSpotifyTest();
            Radio.OpenSpotify(
                session => WorkspaceWindow.Open(session),
                error => UiLayout.Fail(error)
            );
            Radio.ReportSpotifyTest("player");
            Radio.ReplaceSpotifyTest();
            SessionHub.Instance.SessionStore.RefreshFailed("late selection error");
            Radio.OpenSpotify(
                session => WorkspaceWindow.Open(session),
                error => UiLayout.Fail(error)
            );
            Radio.ReportSpotifyTest("player");
            Radio.QuitSpotifyTest();
            SessionHub.Instance.SessionStore.RefreshFailed("late shutdown error");
            AssertEx.Equal(null, WorkspaceWindow.Current, "stale failures open no terminal");
        }

        public static void NcspotReportsRequireSpotifySelectionEvenOnFailure()
        {
            Radio.ResetSpotifyTest();
            AssertEx.False(
                Radio.AcceptsReport("ncspot", "late error"),
                "OST rejects retired Spotify failure"
            );
            AssertEx.False(
                Radio.AcceptsReport("ncspot", null),
                "OST rejects retired Spotify metadata"
            );
            AssertEx.True(
                Radio.AcceptsReport(null, "station failure"),
                "unidentified current source error remains accepted"
            );
            Radio.OpenSpotify(
                session => WorkspaceWindow.Open(session),
                error => UiLayout.Fail(error)
            );
            AssertEx.True(
                Radio.AcceptsReport("ncspot", "current error"),
                "selected Spotify accepts its failure"
            );
            AssertEx.False(
                Radio.AcceptsReport("radio", null),
                "Spotify rejects other source metadata"
            );
            Radio.ReplaceSpotifyTest();
            AssertEx.False(
                Radio.AcceptsReport("ncspot", "late error"),
                "source replacement rejects old failure"
            );
        }

        public static void LaunchFailureAllowsRetry()
        {
            Radio.ResetSpotifyTest();
            Radio.OpenSpotify(
                session => WorkspaceWindow.Open(session),
                error => UiLayout.Fail(error)
            );
            var error = AssertEx.Throws<System.Exception>(
                () => Radio.ReportSpotifyTest(null, "install ncspot on the daemon host first"),
                "launch failure reaches the user"
            );
            AssertEx.Equal(
                "install ncspot on the daemon host first",
                error.Message,
                "failure text"
            );
            Radio.OpenSpotify(
                session => WorkspaceWindow.Open(session),
                error => UiLayout.Fail(error)
            );
            Radio.ReportSpotifyTest("retry");
            SessionHub.Instance.SessionStore.Refreshed();
            AssertEx.Equal("retry", WorkspaceWindow.Current, "failure releases the pending launch");
        }
    }
}
