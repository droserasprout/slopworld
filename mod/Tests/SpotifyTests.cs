namespace SlopWorld.Tests
{
    static class SpotifyTests
    {
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
