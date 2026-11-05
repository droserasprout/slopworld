using System;

namespace SlopWorld
{
    // Main-thread owner of shared config publication. Reads from Settings and the hub
    // share one ordering; writes invalidate reads both before and during the mutation.
    internal sealed class DaemonConfigStore
    {
        string _endpoint;
        long _revision;
        long _generation;
        DaemonConfig _value = new DaemonConfig();

        string Endpoint
        {
            get
            {
                string endpoint = DaemonClient.BaseUrl;
                if (_endpoint != endpoint)
                {
                    _endpoint = endpoint;
                    _revision++;
                    _generation++;
                    _value = new DaemonConfig();
                }
                return endpoint;
            }
        }

        public DaemonConfig Value
        {
            get
            {
                _ = Endpoint;
                return _value;
            }
        }

        public string BeginSave()
        {
            string endpoint = Endpoint;
            _revision++;
            return endpoint;
        }

        public void Saved(string endpoint)
        {
            if (Endpoint == endpoint)
                Refresh();
        }

        public void Refresh(
            Action<Wire.ConfigResult, DaemonConfig> loaded = null,
            Action<string> fail = null
        )
        {
            string endpoint = Endpoint;
            long revision = ++_revision;
            long generation = _generation;
            DaemonClient.Get<Wire.ConfigResult>(
                WireProtocol.Routes.Config,
                reply =>
                {
                    if (Endpoint != endpoint || _generation != generation)
                        return;
                    var server = DaemonConfig.FromWire(reply.Values, reply.Metadata);
                    if (_revision == revision)
                        _value = server;
                    // Page drafts have their own operation ordering. A newer hub read
                    // must not strand an independently requested page load.
                    // Draft edits must not mutate the accepted shared snapshot.
                    loaded?.Invoke(reply, DaemonConfig.FromWire(reply.Values, reply.Metadata));
                },
                error =>
                {
                    if (Endpoint == endpoint && _generation == generation)
                        fail?.Invoke(error);
                }
            );
        }
    }
}
