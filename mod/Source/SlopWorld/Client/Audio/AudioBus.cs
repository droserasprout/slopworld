namespace SlopWorld
{
    class AudioBus
    {
        readonly HubTransport _transport;

        public AudioBus(HubTransport transport)
        {
            _transport = transport;
        }

        public void PlayStation(string station, string stream, float volume) =>
            SendSelection(new Wire.AudioSelection { Station = station, Stream = stream }, volume);

        public void Stop(float volume, string token = null) =>
            _transport.Send(
                new Wire.ClientMessage
                {
                    Audio = new Wire.AudioRequest
                    {
                        Volume = volume,
                        Stop = new Wire.Empty(),
                        StopToken = token ?? "",
                    },
                }
            );

        internal bool StopForShutdown(float volume)
        {
            Stop(volume);
            return _transport.FlushOutgoing(1000);
        }

        void SendSelection(Wire.AudioSelection selection, float volume) =>
            _transport.Send(
                new Wire.ClientMessage
                {
                    Audio = new Wire.AudioRequest { Volume = volume, Selection = selection },
                }
            );

        public void SendSpotify(float volume) =>
            SendSelection(new Wire.AudioSelection { Ncspot = true }, volume);

        public void SendVolume(float volume) =>
            _transport.Send(
                new Wire.ClientMessage { Audio = new Wire.AudioRequest { Volume = volume } }
            );
    }
}
