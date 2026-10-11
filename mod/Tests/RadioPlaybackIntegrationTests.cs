using System.Collections.Generic;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class RadioPlaybackIntegrationTests
    {
        sealed class Socket : IHubSocket
        {
            public bool Connected { get; private set; }
            public string LastError => null;
            public IncomingMessageQueue Incoming { get; } = new IncomingMessageQueue();
            public readonly List<Wire.AudioRequest> Sent = new List<Wire.AudioRequest>();
            public bool Connect(string host, int port, string path, string token, int timeoutMs = 3000) => Connected = true;
            public bool SendBinary(byte[] payload)
            {
                var message = Wire.ClientMessage.Parser.ParseFrom(payload);
                if (message.Audio != null) Sent.Add(message.Audio);
                return true;
            }
            public void Dispose() { Connected = false; Incoming.Close(); }
        }

        public static void SourceChangesDispatchSelectionsImmediatelyAndReconnectRetainsMute()
        {
            string saved = Settings.S.radio;
            var socket = new Socket();
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
                Assert.That(socket.Sent[0].Selection.File, Is.EqualTo("/test/ost"));
                Assert.That(socket.Sent[0].Volume, Is.EqualTo(0.5f));
                var station = new Radio.Station("radio", "Radio", "", "", new[] { 64, 128 }, new[] { "low", "high" }, 64);
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
                Assert.That(socket.Sent[6].Selection.File, Is.EqualTo("/test/ost"));
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
