namespace SlopWorld
{
    // The jukebox's socket channel. A station is an id plus its catalog stream key; a file is
    // the absolute path of one of the mod's OST files or its containing directory. Both are
    // sent over HubTransport.Send. See Sim/Radio.cs.
    class AudioBus
    {
        readonly HubTransport _transport;

        public AudioBus(HubTransport transport)
        {
            _transport = transport;
        }

        // All three nulls stop it, and leaving selection out altogether is the volume moving
        // on its own - which must not restart a stream. URLs never leave the selection message.
        public void SendAudio(string station, string stream, string file, float volume)
        {
            string selection;
            if (file != null)
                selection = $"{{\"file\":{JVal.Q(file)}}}";
            else if (station != null && stream != null)
                selection = $"{{\"station\":{JVal.Q(station)},\"stream\":{JVal.Q(stream)}}}";
            else
                selection = "null";
            _transport.Send($"{{\"t\":\"audio\",\"selection\":{selection}," +
                            $"\"volume\":{HubWire.Num(volume)}}}");
        }

        public void SendVolume(float volume)
        {
            _transport.Send($"{{\"t\":\"audio\",\"volume\":{HubWire.Num(volume)}}}");
        }
    }
}
