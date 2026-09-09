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
        public bool Saving { get; private set; }
        string _baseline;
        int _generation;

        public void Load(bool refreshHealth, Action loaded)
        {
            if (Saving) return;
            int generation = ++_generation;
            if (refreshHealth) SessionHub.Instance.RefreshHealth();
            DaemonClient.Get(WireContract.Routes.Config,
                j =>
                {
                    if (generation != _generation) return;
                    Config = DaemonConfig.FromJson(j["values"]);
                    _baseline = Config.ToPatchJson();
                    // The editable draft must never become the live daemon mirror.
                    SessionHub.Instance.Config = DaemonConfig.FromJson(j["values"]);
                    Path = j["path"].AsString();
                    Loaded = true;
                    Error = null;
                    loaded?.Invoke();
                },
                msg =>
                {
                    if (generation != _generation) return;
                    Error = msg;
                    Loaded = false;
                });
        }

        public void Save(Action beforeSave, Action afterSave)
        {
            if (!Loaded || Config == null || Saving) return;
            beforeSave?.Invoke();
            ++_generation;
            Saving = true;
            string submitted = Config.ToPatchJson();
            DaemonClient.Put(WireContract.Routes.ConfigPatch, Config.ToPatchJson(_baseline),
                _ =>
                {
                    Saving = false;
                    // Advance only to the submitted snapshot: edits made while saving
                    // remain a draft, and other pages keep their own baseline and fields.
                    _baseline = submitted;
                    Error = null;
                    SessionHub.Instance.RefreshConfig();
                    afterSave?.Invoke();
                },
                msg => { Saving = false; Error = msg; });
        }
    }
}
