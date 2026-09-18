namespace SlopWorld
{
    class AudioBus
    {
        readonly HubTransport _transport;
        public AudioBus(HubTransport transport) { _transport = transport; }
        public void SendAudio(string station, string stream, string file, float volume)
        {
            var request = new Wire.AudioRequest { Volume = volume };
            if (file != null) request.Selection = new Wire.AudioSelection { File = file };
            else if (station != null && stream != null) request.Selection = new Wire.AudioSelection { Station = station, Stream = stream };
            else request.Stop = new Wire.Empty();
            _transport.Send(new Wire.ClientMessage { Audio = request });
        }
        public void SendVolume(float volume) => _transport.Send(new Wire.ClientMessage { Audio = new Wire.AudioRequest { Volume = volume } });
    }
}
