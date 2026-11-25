using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1001",
        Justification = "NUnit TearDown disposes the per-test profile and socket."
    )]
    public sealed class BrowserJukeboxTests
    {
        FakeBrowserJukebox _player;
        RadioTestProfile _profile;
        RadioPlaybackIntegrationTests.Socket _socket;
        HubTransport _transport;
        float _master;

        [SetUp]
        public void SetUp()
        {
            _profile = new RadioTestProfile();
            _master = Verse.Prefs.VolumeMaster;
            Verse.Prefs.VolumeMaster = 1;
            _socket = new RadioPlaybackIntegrationTests.Socket();
            _transport = new HubTransport(() => _socket, action => action());
            _transport.Connect();
            _transport.Update();
            StartClient();
        }

        void StartClient()
        {
            Radio.ResetSpotifyTest();
            _player = new FakeBrowserJukebox();
            BrowserJukebox.Current = _player;
            SessionHub.Instance.Audio = new AudioBus(_transport);
        }

        [TearDown]
        public void TearDown()
        {
            _transport.Disconnect();
            _socket.Dispose();
            _profile.Dispose();
            Verse.Prefs.VolumeMaster = _master;
            Radio.ResetSpotifyTest();
        }

        static void Announce(
            bool output = true,
            bool relay = true,
            bool spotify = true,
            bool sharedTransport = true
        )
        {
            SessionHub.Instance.Capabilities = DaemonCapabilities.FromWire(
                new Wire.Capabilities
                {
                    AudioPlayback = output,
                    RadioRelay = relay,
                    SharedAudioTransport = sharedTransport,
                    Ncspot = spotify,
                    Hostname = "music-host",
                }
            );
            Radio.CapabilitiesChanged();
        }

        static void Catalog()
        {
            var station = new Wire.Station
            {
                Id = "radio",
                Metadata = new Wire.StationMetadata
                {
                    Name = "Station",
                    TitleRegex = "^(?<artist>.+)/(?<title>.+)$",
                },
                DefaultRate = 64,
            };
            station.Streams.Add(new Wire.StationStream { Rate = 64, Key = "low" });
            station.Streams.Add(new Wire.StationStream { Rate = 128, Key = "high" });
            var catalog = new Wire.JukeboxCatalog();
            catalog.Stations.Add(station);
            Radio.SetStations(catalog);
        }

        static Wire.AudioState SharedTrack(bool playing = false) =>
            new Wire.AudioState
            {
                Selection = new Wire.AudioSelection { Track = "dive-20261012" },
                Playing = playing,
                Volume = .42f,
            };

        [TestCase(false)]
        [TestCase(true)]
        public void LocalStartupAndShutdownAreIndependentOfSharedPlayback(bool muted)
        {
            Settings.S.radioMute = muted;
            SessionHub.Instance.Online = false;
            Radio.Update();
            Assert.That(_player.Starts, Is.EqualTo(muted ? 0 : 1));

            SessionHub.Instance.Online = true;
            Announce();
            Radio.ReceiveAudio(SharedTrack(true));
            Radio.Update();
            Assert.That(_player.Starts, Is.EqualTo(muted ? 0 : 1));
            Radio.Quit();
            Radio.Quit();
            Assert.That(_player.Playing, Is.False);
            Assert.That(_socket.Sent, Is.Empty);
        }

        [Test]
        public void LocalOstUsesTheInstalledAdapterForNavigationAndTrackMetadata()
        {
            Radio.PickOst();
            Radio.Previous();
            Assert.That(_player.Direction, Is.EqualTo(-1));
            Assert.That(Radio.NowPlaying, Is.EqualTo("terry fail - pace"));
            Assert.That(_socket.Sent, Is.Empty);
        }

        [Test]
        public void DaemonWithoutSharedTransportLeavesLocalPlaybackAvailable()
        {
            Announce(relay: false, sharedTransport: false);
            Radio.Update();
            Radio.SelectOutput(true);
            Assert.That(Radio.ServerOutput, Is.False);
            Assert.That(Radio.SpotifyAvailable, Is.False);
            Assert.That(_player.Playing, Is.True);
            Assert.That(_socket.Sent, Is.Empty);
        }

        [Test]
        public void LocalRadioUsesSidecarRelayAndRetiresOnlyItsListener()
        {
            Settings.S.radio = "radio:high";
            Radio.Update();
            Announce(output: false, spotify: false);
            Assert.That(Radio.ServerUnavailable, Does.Contain("host audio"));
            Catalog();
            Radio.Update();
            Assert.That(_player.Station, Is.EqualTo("radio"));
            Assert.That(_player.Streams, Is.EqualTo(new[] { "high", "low" }));
            _player.Stream = "low";
            _player.Title = "artist/song";
            Radio.Update();
            Assert.That(Radio.PickedRate, Is.EqualTo(64));
            Assert.That(Settings.Radio, Is.EqualTo("radio:high"));
            Assert.That(Radio.NowPlaying, Is.EqualTo("artist - song"));

            Radio.SelectOutput(true);
            Assert.That(Radio.ServerOutput, Is.False);
            Assert.That(_player.Playing, Is.True);
            SessionHub.Instance.Online = false;
            Radio.ToggleMute();
            Assert.That(_player.Playing, Is.False);
            SessionHub.Instance.Online = true;
            Radio.ToggleMute();
            Assert.That(_player.Starts, Is.EqualTo(2));
            Radio.Update();
            Assert.That(Radio.Title, Is.EqualTo(null));
            Radio.Quit();
            Assert.That(_socket.Sent, Is.Empty);
        }

        [Test]
        public void ExplicitSharedStopCancelsSilentSelectionWithoutChangingSavedGain()
        {
            Settings.S.radioOutput = "server";
            Announce();
            var opening = SharedTrack();
            opening.Active = true;
            Radio.ReceiveAudio(opening);
            Radio.Update();
            Assert.That(Radio.NowPlaying, Is.Null);
            Assert.That(Radio.SharedPlaybackLabel, Is.EqualTo("Starting · terry fail - dive"));
            Radio.ToggleMute();
            Assert.That(_socket.Sent[0].Transport, Is.EqualTo("pause"));
            Assert.That(_socket.Sent[0].HasVolume, Is.False);
        }

        [Test]
        public void SharedCommandsUseCurrentDaemonSelectionAndGain()
        {
            Settings.S.radioOutput = "server";
            Settings.S.radioMute = true;
            Announce();
            Radio.ReceiveAudio(SharedTrack());
            Radio.Update();
            Assert.That(Radio.SharedPlaybackLabel, Is.EqualTo("Paused · terry fail - dive"));
            Assert.That(Radio.Muted, Is.True);
            Assert.That(Radio.OutputVolume, Is.EqualTo(.42f));
            Assert.That(_player.Starts, Is.Zero);
            Assert.That(_socket.Sent, Is.Empty);
            Radio.ToggleMute();
            Radio.ReceiveAudio(SharedTrack(true));
            Assert.That(Radio.NowPlaying, Is.EqualTo("terry fail - dive"));
            Radio.ToggleMute();
            Settings.S.radioHiddenSources = "filtered";
            Radio.Previous();
            Radio.Next();
            Radio.PickRandom();
            Radio.PickOst();
            Assert.That(
                _socket.Sent.ConvertAll(r => r.Transport),
                Is.EqualTo(new[] { "play", "pause", "previous", "next", "shuffle", "ost" })
            );
            Catalog();
            Radio.Pick(Radio.Stations[0], 128);
            Assert.That(_socket.Sent[3].HiddenSources, Is.EqualTo(new[] { "filtered" }));
            Assert.That(_socket.Sent[6].Selection.Stream, Is.EqualTo("high"));
            Assert.That(_socket.Sent[6].KeepVolume, Is.True);
            Assert.That(_socket.Sent.TrueForAll(r => !r.HasVolume), Is.True);
            Radio.SetOutputVolume(.27f);
            Assert.That(_socket.Sent[7].Volume, Is.EqualTo(.27f));
            Assert.That(Settings.S.radioVolume, Is.EqualTo(.8f));
            Assert.That(Settings.Radio, Is.EqualTo("ost"));
        }

        [Test]
        public void OutputChangesRestoreLocalChoicesAndLeaveBothPlayersStoppedOrUnchanged()
        {
            Announce();
            Catalog();
            Radio.Pick(Radio.Stations[0], 128);
            Radio.SetOutputVolume(.31f);
            Radio.ReceiveAudio(SharedTrack(true));
            Radio.SelectOutput(true);
            Assert.That(Settings.S.radioOutput, Is.EqualTo("server"));
            Assert.That(_player.Playing, Is.False);
            Assert.That(Radio.NowPlaying, Is.EqualTo("terry fail - dive"));

            Radio.ResetSharedSnapshot();
            Assert.That(Radio.SharedReady, Is.False);
            Radio.Update();
            Radio.ReceiveAudio(
                new Wire.AudioState
                {
                    Selection = new Wire.AudioSelection { Station = "radio", Stream = "low" },
                    Volume = .75f,
                }
            );
            Assert.That(Radio.PickedRate, Is.EqualTo(64));
            Assert.That(Radio.SharedSelectionLabel, Is.EqualTo("Station"));
            Radio.SelectOutput(false);
            Radio.Update();
            Assert.That(Radio.Muted, Is.True);
            Assert.That(Radio.PickedRate, Is.EqualTo(128));
            Assert.That(Radio.OutputVolume, Is.EqualTo(.31f));
            Assert.That(_player.Volume, Is.EqualTo(.31f));
            Assert.That(_player.Starts, Is.EqualTo(1));
            Assert.That(_socket.Sent, Is.Empty);
            Radio.ToggleMute();
            Assert.That(_player.Stream, Is.EqualTo("high"));
            Assert.That(_socket.Sent, Is.Empty);
        }

        [Test]
        public void FreshLocalListenersKeepTheirOwnStationAndGain()
        {
            foreach (var (stream, volume) in new[] { ("low", .25f), ("high", .74f) })
            {
                StartClient();
                Settings.S.radio = "radio:" + stream;
                Settings.S.radioVolume = volume;
                Settings.S.radioMute = false;
                Announce();
                Catalog();
                Radio.ReceiveAudio(SharedTrack(true));
                Radio.Update();
                Assert.That(_player.Stream, Is.EqualTo(stream));
                Assert.That(_player.Volume, Is.EqualTo(volume));
                Radio.Quit();
                Assert.That(_socket.Sent, Is.Empty);
            }
        }

        [Test]
        public void DecoderErrorRetainsLocalSelectionAndAllowsRetry()
        {
            Announce();
            Catalog();
            Radio.Pick(Radio.Stations[0], 128);
            _player.Playing = false;
            _player.Error = "This browser cannot decode any stream for Station.";
            Radio.Update();
            Assert.That(Radio.PlaybackMessage, Does.Contain("decode"));
            Assert.That(Radio.Muted, Is.True);
            Assert.That(Radio.ServerOutput, Is.False);
            Assert.That(Settings.Radio, Is.EqualTo("radio:high"));
            Radio.ToggleMute();
            Assert.That(_player.Starts, Is.EqualTo(2));
            Assert.That(_player.Stream, Is.EqualTo("high"));
            Assert.That(_socket.Sent, Is.Empty);
        }

        [Test]
        public void SharedStationIdentityDoesNotDependOnAnAvailableCatalogEntry()
        {
            Settings.S.radioOutput = "server";
            Announce();
            Radio.ReceiveAudio(
                new Wire.AudioState
                {
                    Playing = true,
                    Title = "Live title",
                    Selection = new Wire.AudioSelection { Station = "missing", Stream = "stream" },
                }
            );
            Radio.Update();
            Assert.That(Radio.OstSelected, Is.False);
            Assert.That(Radio.Source, Is.EqualTo("missing"));
            Assert.That(Radio.NowPlaying, Is.EqualTo("Live title"));
            Assert.That(Radio.Artist, Is.Empty);
            Assert.That(_socket.Sent, Is.Empty);
        }

        [Test]
        public void RemovedStationCanOnlyBeReplacedByAnExplicitSourceSelection()
        {
            Announce();
            Catalog();
            Radio.Pick(Radio.Stations[0], 128);
            Radio.SetStations(new Wire.JukeboxCatalog());
            Assert.That(Radio.PlaybackMessage, Does.Contain("catalog"));
            Assert.That(Radio.Muted, Is.True);
            Radio.ToggleMute();
            Assert.That(_player.Playing, Is.False);
            Assert.That(Settings.Radio, Is.EqualTo("radio:high"));
            Radio.PickOst();
            Assert.That(Settings.Radio, Is.EqualTo("ost"));
            Assert.That(_player.Playing, Is.True);
            Assert.That(_socket.Sent, Is.Empty);
        }

        [TestCase(false, true, "host audio")]
        [TestCase(true, false, "update the daemon")]
        public void RestoredServerRemainsSelectedWhenUnavailable(
            bool output,
            bool sharedTransport,
            string reason
        )
        {
            Settings.S.radioOutput = "server";
            Settings.S.radioStopOnExit = true;
            Announce(output: output, sharedTransport: sharedTransport);
            Radio.ReceiveAudio(new Wire.AudioState { Playing = true, Volume = .7f });
            Catalog();
            Radio.Update();
            Assert.That(Radio.TransportAvailable, Is.False);
            Assert.That(Radio.ServerAvailable, Is.False);
            Assert.That(Radio.PlaybackMessage, Does.Contain(reason));
            Radio.ToggleMute();
            Radio.PickOst();
            Radio.Pick(Radio.Stations[0], 128);
            string error = null;
            Radio.OpenSpotify(null, value => error = value);
            Assert.That(error, Does.Contain(reason));
            Radio.Next();
            Radio.SetOutputVolume(.1f);
            Radio.Quit();
            Assert.That(Settings.S.radioOutput, Is.EqualTo("server"));
            Assert.That(_player.Starts, Is.Zero);
            Assert.That(_socket.Sent, Is.Empty);
        }

        [Test]
        public void LocalSourceChangeRejectsALateSpotifyAcknowledgement()
        {
            Announce();
            Radio.Update();
            Radio.OpenSpotify(_ => Assert.Fail("retired open"), Assert.Fail);
            Radio.PickOst();
            Radio.OpenSpotify(_ => Assert.Fail("new open received old reply"), Assert.Fail);
            Radio.ReceiveAudio(
                new Wire.AudioState
                {
                    Source = "ncspot",
                    Session = "player",
                    RequestId = _socket.Sent[0].RequestId,
                    Selection = new Wire.AudioSelection { Ncspot = true },
                }
            );
            Assert.That(_socket.Sent[0].RequestId, Is.Not.EqualTo(_socket.Sent[1].RequestId));
            Assert.That(Radio.ServerOutput, Is.False);
            Assert.That(_player.Playing, Is.True);
            Assert.That(SessionHub.Instance.SessionStore.Refreshed, Is.Null);
        }

        [Test]
        public void RejectedSpotifySocketWriteLeavesLocalMusicPlaying()
        {
            Announce();
            Radio.Update();
            _transport.Disconnect();
            string error = null;
            Radio.OpenSpotify(null, value => error = value);
            Assert.That(error, Does.Contain("send"));
            Assert.That(Settings.S.radioOutput, Is.EqualTo("local"));
            Assert.That(_player.Playing, Is.True);
        }

        [TestCase(false, true, true)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public void ServerExitStopOnlyUsesExplicitPreferenceAndAnOnlineConnection(
            bool connected,
            bool stop,
            bool muted
        )
        {
            Settings.S.radioOutput = "server";
            Settings.S.radioStopOnExit = stop;
            Settings.S.radioMute = muted;
            Announce();
            Radio.ReceiveAudio(SharedTrack(true));
            Radio.Update();
            SessionHub.Instance.Online = connected;
            Radio.Quit();
            Radio.Quit();
            Assert.That(_socket.Sent.Count, Is.EqualTo(connected && stop ? 1 : 0));
            if (connected && stop)
            {
                Assert.That(_socket.Sent[0].Stop, Is.Not.Null);
                Assert.That(_socket.Sent[0].HasVolume, Is.False);
                Assert.That(_socket.FlushedCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void SpotifyValidatesCapabilitiesAndOnlyExplicitSelectionChangesOutput()
        {
            string error = null;
            string opened = null;
            Radio.Update();
            Radio.OpenSpotify(null, value => error = value);
            Assert.That(error, Does.Contain("capabilities"));
            Announce(output: false);
            Radio.OpenSpotify(null, value => error = value);
            Assert.That(error, Does.Contain("audio output"));
            Announce(spotify: false);
            Radio.OpenSpotify(null, value => error = value);
            Assert.That(error, Does.Contain("ncspot"));
            Assert.That(Radio.ServerOutput, Is.False);
            Assert.That(_player.Playing, Is.True);
            Assert.That(_socket.Sent, Is.Empty);

            var spotify = new Wire.AudioState
            {
                Selection = new Wire.AudioSelection { Ncspot = true },
                Source = "ncspot",
                Playing = true,
                Session = "player",
            };
            Announce();
            Radio.ReceiveAudio(spotify);
            Assert.That(Radio.Spotify, Is.False);
            Radio.OpenSpotify(value => opened = value, value => error = value);
            Assert.That(Radio.ServerOutput, Is.False);
            Radio.ReceiveAudio(spotify);
            Assert.That(Radio.ServerOutput, Is.False);
            var rejection = new Wire.AudioState
            {
                Source = "ncspot",
                Error = "ncspot exited",
                RequestId = _socket.Sent[0].RequestId,
            };
            Radio.ReceiveAudio(rejection);
            Assert.That(error, Does.Contain("exited"));
            Assert.That(_player.Playing, Is.True);
            Radio.OpenSpotify(value => opened = value, Assert.Fail);
            spotify.RequestId = _socket.Sent[1].RequestId;
            Radio.ReceiveAudio(spotify);
            Assert.That(Radio.ServerOutput, Is.True);
            Assert.That(Settings.Radio, Is.EqualTo("ost"));
            Assert.That(_player.Playing, Is.False);
            Assert.That(_socket.Sent[1].Selection.Ncspot, Is.True);
            Assert.That(_socket.Sent[1].KeepVolume, Is.True);
            Assert.That(_socket.Sent[1].HasVolume, Is.False);
            SessionHub.Instance.SessionStore.Refreshed();
            Assert.That(opened, Is.EqualTo("player"));
            Radio.OpenSpotify(value => opened = value, Assert.Fail);
            spotify.RequestId = _socket.Sent[2].RequestId;
            Radio.ReceiveAudio(spotify);
            Radio.SelectOutput(false);
            opened = null;
            SessionHub.Instance.SessionStore.Refreshed();
            Assert.That(opened, Is.Null);
            Assert.That(_socket.Sent.Count, Is.EqualTo(3));
        }
    }
}
