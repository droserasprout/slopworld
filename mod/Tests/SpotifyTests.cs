namespace SlopWorld.Tests
{
    static class SpotifyTests
    {
        public static void NonNcspotReportDoesNotCompletePendingLaunch()
        {
            Radio.ResetSpotifyTest();
            Radio.OpenSpotify();
            Radio.ReportSpotifyTest("unrelated", source: "radio");
            AssertEx.Equal(null, SessionHub.Instance.SessionStore.Refreshed, "shared production report filter rejects another source");
            Radio.ReportSpotifyTest("player");
            SessionHub.Instance.SessionStore.Refreshed();
            AssertEx.Equal("player", TerminalWindow.Current, "ncspot reply still completes pending launch");
        }

        public static void SessionRefreshFailureIsReportedAndAllowsRetry()
        {
            Radio.ResetSpotifyTest();
            Radio.OpenSpotify();
            Radio.ReportSpotifyTest("player");
            AssertEx.Throws<System.Exception>(() => SessionHub.Instance.SessionStore.RefreshFailed("offline"), "refresh failure reaches user");
            AssertEx.Equal(null, TerminalWindow.Current, "failed refresh opens nothing");
            Radio.OpenSpotify();
            Radio.ReportSpotifyTest("retry");
            SessionHub.Instance.SessionStore.Refreshed();
            AssertEx.Equal("retry", TerminalWindow.Current, "retry completes after refresh failure");
        }

        public static void MissingCapabilityFallsBackAndRejectsLateTerminalOpen()
        {
            Radio.ResetSpotifyTest();
            Radio.OpenSpotify();
            Radio.ReportSpotifyTest("player");
            SessionHub.Instance.Capabilities.Known = true;
            SessionHub.Instance.Capabilities.Ncspot = false;
            Radio.CapabilitiesChanged();
            AssertEx.True(!Radio.Spotify, "unavailable saved selection falls back to OST");
            SessionHub.Instance.SessionStore.Refreshed();
            AssertEx.Equal(null, TerminalWindow.Current, "capability fallback invalidates pending terminal refresh");
        }

        public static void UnknownCapabilitiesKeepSpotifyAvailable()
        {
            Radio.ResetSpotifyTest();
            SessionHub.Instance.Capabilities.Ncspot = false;
            AssertEx.True(Radio.SpotifyAvailable, "wait for the capability snapshot");
            SessionHub.Instance.Capabilities.Known = true;
            AssertEx.True(!Radio.SpotifyAvailable, "known missing player disables Spotify");
            AssertEx.Throws<System.Exception>(() => Radio.OpenSpotify(), "unavailable player cannot launch");
            AssertEx.True(!Radio.Spotify, "rejected launch preserves selection");
        }

        public static void LaunchWaitsForAudioReply()
        {
            Radio.ResetSpotifyTest();
            Radio.OpenSpotify();
            AssertEx.True(Radio.SpotifyRequested, "launch is an ordinary pending audio selection");
            AssertEx.Equal(null, SessionHub.Instance.SessionStore.Refreshed, "no terminal before the reply");
            Radio.ReportSpotifyTest("player");
            SessionHub.Instance.SessionStore.Refreshed();
            AssertEx.Equal("player", TerminalWindow.Current, "the audio reply identifies the terminal");
        }

        public static void QuitRejectsLateLaunchReply()
        {
            Radio.ResetSpotifyTest();
            Radio.OpenSpotify();
            Radio.QuitSpotifyTest();
            Radio.ReportSpotifyTest("player");
            AssertEx.Equal(null, SessionHub.Instance.SessionStore.Refreshed, "quit ignores a late launch reply");
        }

        public static void QuitRejectsLateSessionRefresh()
        {
            Radio.ResetSpotifyTest();
            Radio.OpenSpotify();
            Radio.ReportSpotifyTest("player");
            Radio.QuitSpotifyTest();
            SessionHub.Instance.SessionStore.Refreshed();
            AssertEx.Equal(null, TerminalWindow.Current, "quit also invalidates an in-flight refresh");
        }

        public static void SourceChangeRejectsLateSessionRefresh()
        {
            Radio.ResetSpotifyTest();
            Radio.OpenSpotify();
            Radio.ReportSpotifyTest("player");
            Radio.ReplaceSpotifyTest();
            SessionHub.Instance.SessionStore.Refreshed();
            AssertEx.Equal(null, TerminalWindow.Current, "a replaced selection cannot reopen its terminal");
        }

        public static void LaunchFailureAllowsRetry()
        {
            Radio.ResetSpotifyTest();
            Radio.OpenSpotify();
            var error = AssertEx.Throws<System.Exception>(() =>
                Radio.ReportSpotifyTest(null, "install ncspot on the daemon host first"),
                "launch failure reaches the user");
            AssertEx.Equal("install ncspot on the daemon host first", error.Message, "failure text");
            Radio.OpenSpotify();
            Radio.ReportSpotifyTest("retry");
            SessionHub.Instance.SessionStore.Refreshed();
            AssertEx.Equal("retry", TerminalWindow.Current, "failure releases the pending launch");
        }
    }
}
