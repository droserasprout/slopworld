namespace SlopWorld
{
    class AudioBus
    {
        readonly HubTransport _transport;

        public AudioBus(HubTransport transport)
        {
            _transport = transport;
        }

        public void PlayStation(string station, string stream, float? volume = null) =>
            Send(
                new Wire.AudioRequest
                {
                    Selection = new Wire.AudioSelection { Station = station, Stream = stream },
                },
                volume
            );

        public bool Stop(float? volume = null, string token = null) =>
            Send(
                new Wire.AudioRequest { Stop = new Wire.Empty(), StopToken = token ?? "" },
                volume
            );

        internal bool StopForShutdown(float? volume = null) =>
            Stop(volume) && _transport.FlushOutgoing(1000);

        public bool SendSpotify(float? volume = null, string requestId = null)
        {
            var request = new Wire.AudioRequest
            {
                Selection = new Wire.AudioSelection { Ncspot = true },
            };
            if (requestId != null)
                request.RequestId = requestId;
            return Send(request, volume);
        }

        public void SendTransport(
            string action,
            System.Collections.Generic.List<string> hidden = null
        )
        {
            var request = new Wire.AudioRequest { Transport = action };
            if (hidden != null)
                request.HiddenSources.Add(hidden);
            Send(request);
        }

        public void SendVolume(float volume) => Send(new Wire.AudioRequest(), volume);

        bool Send(Wire.AudioRequest request, float? volume = null)
        {
            if (volume.HasValue)
                request.Volume = volume.Value;
            return _transport.TrySend(new Wire.ClientMessage { Audio = request });
        }
    }
}
