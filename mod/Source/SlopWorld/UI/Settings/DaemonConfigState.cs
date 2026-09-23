using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // The page is disposable UI. This state is the session-owned draft behind it. A record is
    // selected by stable page identity and daemon endpoint. Therefore, closing Settings cannot
    // discard an edit and an endpoint switch cannot leak one daemon's draft into another.
    public sealed class DaemonConfigState
    {
        static readonly Dictionary<string, DaemonConfigDraft> Drafts =
            new Dictionary<string, DaemonConfigDraft>();

        readonly string _pageId;
        string _key;
        DaemonConfigDraft _draft;

        public DaemonConfigState(string pageId)
        {
            _pageId = pageId ?? "daemon-config";
        }

        string Endpoint => DaemonClient.BaseUrl ?? "";

        string Key => _pageId + "\n" + Endpoint;

        DaemonConfigDraft Draft
        {
            get
            {
                string key = Key;
                if (_draft == null || _key != key)
                {
                    _key = key;
                    if (!Drafts.TryGetValue(key, out _draft))
                        Drafts[key] = _draft = new DaemonConfigDraft();
                }
                return _draft;
            }
        }

        public DaemonConfig Config => Draft.Config;
        public string Path { get; private set; } = "";
        public string Error { get => Draft.Error; set => Draft.Error = value; }
        public bool Loaded => Draft.Loaded;
        public bool Saving => Draft.Saving;
        public bool Dirty => Draft.IsDirty;
        public bool HasConflicts => Draft.Conflicts.Count > 0;

        public string ConflictMessage
        {
            get
            {
                if (!HasConflicts) return null;
                return "External changes on " + string.Join(", ", Draft.Conflicts.ToArray()) +
                       ". Save keeps this draft; Discard uses the server values.";
            }
        }

        public void Load(bool refreshHealth, Action loaded)
        {
            var draft = Draft;
            if (draft.Saving) return;
            draft.ClearQueuedNormalizations();

            string key = _key;
            int operation = draft.BeginOperation();
            if (refreshHealth) SessionHub.Instance.RefreshHealth();
            DaemonClient.Get<Wire.ConfigResult>(WireProtocol.Routes.Config,
                j =>
                {
                    if (!IsCurrent(key, draft, operation)) return;
                    var server = DaemonConfig.FromWire(j.Values, j.Metadata);
                    draft.LoadServer(server);
                    SessionHub.Instance.Config = server;
                    Path = j.Path;
                    draft.Error = null;
                    loaded?.Invoke();
                },
                msg =>
                {
                    if (!IsCurrent(key, draft, operation)) return;
                    draft.Error = msg;
                    // An already loaded draft remains drawable and recoverable after a failed
                    // refresh. Only the initial empty page is considered unavailable.
                });
        }

        public void Save(Action afterSave)
        {
            var draft = Draft;
            if (!draft.Loaded || draft.Config == null || draft.Saving || !draft.IsDirty) return;

            string key = _key;
            int operation = draft.BeginOperation();
            draft.Saving = true;
            var submitted = draft.Config.Snapshot();
            var patch = draft.Config.ToPatch(draft.BaselineValue());
            var submittedTexts = draft.TextSnapshot();

            DaemonClient.Put(WireProtocol.Routes.ConfigPatch, patch,
                _ =>
                {
                    if (!IsCurrent(key, draft, operation)) return;
                    draft.Saving = false;
                    // Acknowledge only what was sent. Fields changed while the request was in
                    // flight remain different from this submitted baseline and stay dirty.
                    draft.Acknowledge(submitted, submittedTexts);
                    draft.ApplyQueuedNormalizations();
                    SessionHub.Instance.RefreshConfig();
                    afterSave?.Invoke();
                },
                msg =>
                {
                    if (!IsCurrent(key, draft, operation)) return;
                    draft.Saving = false;
                    draft.Error = msg;
                });
        }

        public void Discard()
        {
            var draft = Draft;
            if (!draft.Loaded || draft.Saving) return;
            draft.ClearQueuedNormalizations();
            draft.ResetToBaseline();
        }

        public string DraftText(string key, string path, string serverValue,
                                bool zeroMeansBlank = false) =>
            Draft.Text(key, path, serverValue, zeroMeansBlank);

        public void SetDraftText(string key, string path, string value) =>
            Draft.SetText(key, path, value);

        public void MarkCurrentClean() => Draft.MarkCurrentClean();

        public bool IsTextDirty(string key) => Draft.IsTextDirty(key);

        public IEnumerable<string> DraftFieldKeys(string prefix = null) => Draft.FieldKeys(prefix);

        public void ClearQueuedNormalizations() => Draft.ClearQueuedNormalizations();

        public void QueueDraftTextNormalization(string key, string path, string normalized) =>
            Draft.QueueNormalization(key, path, normalized);

        public bool NormalizeDraftTextIfUnchanged(string key, string path, string normalized)
        {
            string current = DraftText(key, path, normalized);
            Draft.SetText(key, path, current);
            return Draft.NormalizeTextIfUnchanged(key, normalized);
        }

        bool IsCurrent(string key, DaemonConfigDraft draft, int operation) =>
            _key == key && Key == key && draft.IsCurrent(operation);
    }
}
