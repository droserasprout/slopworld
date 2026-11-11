using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    static class RadioPlaybackIntegrationTests
    {
        [Test]
        public static void BrowserWithoutLocalAudioLeavesHostPlaybackUntilADaemonSourceIsSelected()
        {
            using var socket = new Socket();
            var transport = new HubTransport(() => socket, action => action());
            try
            {
                Radio.ResetSpotifyTest();
                Radio.LocalPlaybackSupported = false;
                SessionHub.Instance.Capabilities = DaemonCapabilities.FromWire(
                    new Wire.Capabilities { AudioPlayback = true }
                );
                SessionHub.Instance.Audio = new AudioBus(transport);
                transport.Connect();
                transport.Update();
                Radio.SelectPlaybackTest();
                Radio.UpdatePlaybackTest(0, 0.5f);
                Assert.That(socket.Sent.Count, Is.EqualTo(0));
                Assert.That(Radio.NativePlaybackAllowed, Is.False);
                Assert.That(Radio.TransportAvailable, Is.False);
                Radio.SelectPlaybackTest(spotify: true);
                Assert.That(Radio.TransportAvailable, Is.True);
                Radio.UpdatePlaybackTest(1, 0.5f);
                Assert.That(socket.Sent[0].Selection.Ncspot, Is.True);
                Radio.MutePlaybackTest();
                Radio.UpdatePlaybackTest(2, 0.5f);
                Assert.That(socket.Sent[1].Stop, Is.Not.Null);
            }
            finally
            {
                transport.Disconnect();
                Radio.LocalPlaybackSupported = true;
                Radio.ResetSpotifyTest();
            }
        }

        public static void OstAlwaysStaysLocalWhileOtherSourcesUseDaemon()
        {
            string saved = Environment.GetEnvironmentVariable("SLOPWORLD_INITIAL_AUDIO");
            string savedRadio = Settings.S.radio;
            using var socket = new Socket();
            var transport = new HubTransport(() => socket, action => action());
            try
            {
                Environment.SetEnvironmentVariable("SLOPWORLD_INITIAL_AUDIO", "1");
                Settings.S.radio = "ost";
                Radio.ResetSpotifyTest();
                SessionHub.Instance.Audio = new AudioBus(transport);
                transport.Connect();
                transport.Update();
                Radio.SelectPlaybackTest();
                Radio.UpdatePlaybackTest(0, 0.5f);
                Assert.That(socket.Sent[0].Stop, Is.Not.Null);
                Radio.SelectPlaybackTest(spotify: true);
                Radio.UpdatePlaybackTest(0.01, 0.5f);
                Assert.That(socket.Sent[1].Selection.Ncspot, Is.True);
                Radio.SelectPlaybackTest();
                Radio.UpdatePlaybackTest(0.02, 0.5f);
                Assert.That(socket.Sent[2].Stop, Is.Not.Null);
                Environment.SetEnvironmentVariable("SLOPWORLD_INITIAL_AUDIO", "0");
                Radio.SelectPlaybackTest();
                Radio.UpdatePlaybackTest(0.03, 0.5f);
                Assert.That(socket.Sent[3].Stop, Is.Not.Null);
            }
            finally
            {
                transport.Disconnect();
                Radio.ResetSpotifyTest();
                Settings.S.radio = savedRadio;
                Environment.SetEnvironmentVariable("SLOPWORLD_INITIAL_AUDIO", saved);
            }
        }

        public static void LocalPlaybackRequiresCurrentStopAcknowledgmentAndReconnectRevokesIt()
        {
            using var socket = new Socket();
            var transport = new HubTransport(() => socket, action => action());
            try
            {
                Radio.ResetSpotifyTest();
                SessionHub.Instance.Capabilities = DaemonCapabilities.FromWire(
                    new Wire.Capabilities { AudioPlayback = true }
                );
                SessionHub.Instance.Audio = new AudioBus(transport);
                transport.Connect();
                transport.Update();
                Radio.SelectPlaybackTest();
                Radio.UpdatePlaybackTest(0, 0.5f);
                string first = socket.Sent[0].StopToken;
                Assert.That(Radio.NativePlaybackAllowed, Is.False);
                Radio.AcknowledgeStop("stale");
                Assert.That(Radio.NativePlaybackAllowed, Is.False);
                Radio.AcknowledgeStop(first);
                Assert.That(Radio.NativePlaybackAllowed, Is.True);
                Radio.SelectPlaybackTest(spotify: true);
                Radio.AcknowledgeStop(first);
                Assert.That(Radio.NativePlaybackAllowed, Is.False);
                Radio.SelectPlaybackTest();
                Radio.UpdatePlaybackTest(1, 0.5f);
                Radio.AcknowledgeStop(first);
                Assert.That(Radio.NativePlaybackAllowed, Is.False);
                Radio.AcknowledgeStop(socket.Sent[1].StopToken);
                Assert.That(Radio.NativePlaybackAllowed, Is.True);
                SessionHub.Instance.Online = false;
                Radio.UpdatePlaybackTest(2, 0.5f);
                Assert.That(Radio.NativePlaybackAllowed, Is.False);
                SessionHub.Instance.Online = true;
                Radio.UpdatePlaybackTest(3, 0.5f);
                Radio.AcknowledgeStop(socket.Sent[1].StopToken);
                Assert.That(Radio.NativePlaybackAllowed, Is.False);
                Radio.AcknowledgeStop(socket.Sent[2].StopToken);
                Assert.That(Radio.NativePlaybackAllowed, Is.True);
                Radio.AcknowledgeStop(null, playing: true);
                Assert.That(Radio.NativePlaybackAllowed, Is.False);
                Radio.UpdatePlaybackTest(3.01, 0.5f);
                Radio.AcknowledgeStop(socket.Sent[3].StopToken);
                Assert.That(Radio.NativePlaybackAllowed, Is.True);
                Radio.MutePlaybackTest();
                Assert.That(Radio.NativePlaybackAllowed, Is.False);
            }
            finally
            {
                transport.Disconnect();
                Radio.ResetSpotifyTest();
            }
        }

        public static void SavedStationDoesNotStopDaemonWhileWaitingForInitialCatalog()
        {
            string saved = Settings.S.radio;
            using var socket = new Socket();
            var transport = new HubTransport(() => socket, action => action());
            try
            {
                Radio.ResetSpotifyTest();
                Settings.S.radio = "radio:high";
                // Read/Save are substituted in this harness; represent loaded preferences.
                typeof(Radio)
                    .GetField(
                        "_read",
                        System.Reflection.BindingFlags.Static
                            | System.Reflection.BindingFlags.NonPublic
                    )
                    .SetValue(null, true);
                SessionHub.Instance.Audio = new AudioBus(transport);
                transport.Connect();
                transport.Update();
                Radio.UpdatePlaybackTest(0, 0.5f);
                Radio.UpdatePlaybackTest(1, 0.5f);
                Assert.That(socket.Sent, Is.Empty, "startup must leave the existing stream alone");
                var station = new Wire.Station
                {
                    Id = "radio",
                    Metadata = new Wire.StationMetadata { Name = "Radio" },
                    DefaultRate = 64,
                };
                station.Streams.Add(new Wire.StationStream { Rate = 64, Key = "low" });
                station.Streams.Add(new Wire.StationStream { Rate = 128, Key = "high" });
                var catalog = new Wire.JukeboxCatalog();
                catalog.Stations.Add(station);
                Radio.SetStations(catalog);
                Radio.UpdatePlaybackTest(1.01, 0.5f);
                Assert.That(socket.Sent.Count, Is.EqualTo(1));
                Assert.That(socket.Sent[0].Selection.Station, Is.EqualTo("radio"));
                Assert.That(socket.Sent[0].Selection.Stream, Is.EqualTo("high"));
                Assert.That(socket.Sent[0].Stop, Is.Null);
            }
            finally
            {
                transport.Disconnect();
                Radio.ResetSpotifyTest();
                Settings.S.radio = saved;
            }
        }

        sealed class Socket : IHubSocket
        {
            public bool Connected { get; private set; }
            public string LastError => null;
            public IncomingMessageQueue Incoming { get; } = new IncomingMessageQueue();
            public readonly List<Wire.AudioRequest> Sent = new List<Wire.AudioRequest>();
            public int FlushedCount;
            public bool FlushSucceeds = true;

            public bool Connect(
                string host,
                int port,
                string path,
                string token,
                int timeoutMs = 3000,
                bool useTls = false
            ) => Connected = true;

            public bool SendBinary(byte[] payload)
            {
                var message = Wire.ClientMessage.Parser.ParseFrom(payload);
                if (message.Audio != null)
                    Sent.Add(message.Audio);
                return true;
            }

            public bool FlushOutgoing(int timeoutMs)
            {
                FlushedCount = Sent.Count;
                Assert.That(timeoutMs, Is.InRange(1, 1000));
                return Connected && FlushSucceeds;
            }

            public void Dispose()
            {
                Connected = false;
                Incoming.Close();
            }
        }

        public static void ShutdownStopFlushesAfterQueuedSelectionAndReportsFailure()
        {
            using var socket = new Socket();
            var transport = new HubTransport(() => socket, action => action());
            try
            {
                transport.Connect();
                transport.Update();
                var audio = new AudioBus(transport);
                audio.PlayStation("station", "stream", 0.5f);
                Assert.That(audio.StopForShutdown(0.5f), Is.True);
                Assert.That(socket.Sent[1].Stop, Is.Not.Null);
                Assert.That(socket.FlushedCount, Is.EqualTo(2));
                socket.FlushSucceeds = false;
                Assert.That(audio.StopForShutdown(0.5f), Is.False);
                Assert.That(socket.FlushedCount, Is.EqualTo(3));
            }
            finally
            {
                transport.Disconnect();
            }
        }

        public static void SourceChangesDispatchSelectionsImmediatelyAndReconnectRetainsMute()
        {
            string saved = Settings.S.radio;
            using var socket = new Socket();
            var transport = new HubTransport(() => socket, action => action());
            try
            {
                Settings.S.radio = "ost";
                Radio.ResetSpotifyTest();
                SessionHub.Instance.Audio = new AudioBus(transport);
                transport.Connect();
                transport.Update();
                Radio.SelectPlaybackTest();
                Radio.UpdatePlaybackTest(0, 0.5f);
                Assert.That(socket.Sent.Count, Is.EqualTo(1));
                Assert.That(socket.Sent[0].Stop, Is.Not.Null);
                Assert.That(socket.Sent[0].Volume, Is.EqualTo(0.5f));
                var station = new Radio.Station(
                    "radio",
                    "Radio",
                    "",
                    "",
                    new[] { 64, 128 },
                    new[] { "low", "high" },
                    64
                );
                Radio.SelectPlaybackTest(station, 128);
                Radio.UpdatePlaybackTest(0.01, 0.5f);
                Assert.That(socket.Sent.Count, Is.EqualTo(2));
                Assert.That(socket.Sent[1].Selection.Station, Is.EqualTo("radio"));
                Assert.That(socket.Sent[1].Selection.Stream, Is.EqualTo("high"));
                Radio.SelectPlaybackTest(spotify: true);
                Radio.UpdatePlaybackTest(0.02, 0.5f);
                Assert.That(socket.Sent[2].Selection.Ncspot, Is.True);
                Radio.UpdatePlaybackTest(0.03, 0.8f);
                Assert.That(socket.Sent.Count, Is.EqualTo(3));
                Radio.UpdatePlaybackTest(0.3, 0.8f);
                Assert.That(socket.Sent[3].Selection, Is.Null);
                Assert.That(socket.Sent[3].Stop, Is.Null);
                Assert.That(socket.Sent[3].Volume, Is.EqualTo(0.8f));
                Radio.MutePlaybackTest();
                Radio.UpdatePlaybackTest(0.31, 0.8f);
                Assert.That(socket.Sent[4].Stop, Is.Not.Null);
                SessionHub.Instance.Online = false;
                Radio.UpdatePlaybackTest(0.32, 0.8f);
                Assert.That(socket.Sent.Count, Is.EqualTo(5));
                SessionHub.Instance.Online = true;
                Radio.UpdatePlaybackTest(0.33, 0.8f);
                Assert.That(socket.Sent[5].Stop, Is.Not.Null);
                Radio.SelectPlaybackTest();
                Radio.UpdatePlaybackTest(0.34, 0.8f);
                Assert.That(socket.Sent[6].Stop, Is.Not.Null);
            }
            finally
            {
                transport.Disconnect();
                Radio.ResetSpotifyTest();
                Settings.S.radio = saved;
            }
        }
    }
}
