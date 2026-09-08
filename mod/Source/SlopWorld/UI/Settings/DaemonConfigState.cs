using System;

namespace SlopWorld
{
    // Config request state owns the daemon mirror, path, errors, and save lifecycle. Pages
    // retain their field parsing and post-load/save behavior; this object never draws UI.
    public sealed class DaemonConfigState
    {
        public DaemonConfig Config { get; private set; }
        public string Path { get; private set; } = "";
        public string Error { get; set; }
        public bool Loaded { get; private set; }

        public void Load(bool refreshHealth, Action loaded)
        {
            if (refreshHealth) SessionHub.Instance.RefreshHealth();
            DaemonClient.Get(WireContract.Routes.Config,
                j =>
                {
                    Config = DaemonConfig.FromJson(j["values"]);
                    SessionHub.Instance.Config = Config;
                    Path = j["path"].AsString();
                    Loaded = true;
                    Error = null;
                    loaded?.Invoke();
                },
                msg => { Error = msg; Loaded = false; });
        }

        public void Save(Action beforeSave, Action afterSave)
        {
            if (!Loaded || Config == null) return;
            beforeSave?.Invoke();
            DaemonClient.Put(WireContract.Routes.ConfigPatch, Config.ToPatchJson(),
                _ =>
                {
                    Error = null;
                    SessionHub.Instance.Config = Config;
                    ModOptions.Reread();
                    afterSave?.Invoke();
                },
                msg => Error = msg);
        }
    }
}
