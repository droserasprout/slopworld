using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SlopWorld
{
    // One editable text field owns the value the user sees and both snapshots used to merge
    // reloads. Keeping the path and formatting policy beside the value prevents pages from
    // maintaining parallel dictionaries that can drift during an in-flight request.
    public sealed class DaemonConfigFieldState
    {
        public readonly string Key;
        public string Path { get; private set; }
        public string Text { get; set; }
        public string LocalBaseline { get; set; }
        public string RemoteBaseline { get; set; }
        public bool ZeroMeansBlank { get; private set; }
        public bool HasPendingNormalization { get; private set; }
        public string PendingNormalization { get; private set; }

        internal DaemonConfigFieldState(string key, string path, string text, bool zeroMeansBlank)
        {
            Key = key;
            Path = path;
            Text = text ?? "";
            LocalBaseline = Text;
            RemoteBaseline = Text;
            ZeroMeansBlank = zeroMeansBlank;
        }

        internal void Configure(string path, bool zeroMeansBlank)
        {
            Path = path;
            ZeroMeansBlank |= zeroMeansBlank;
        }

        internal void QueueNormalization(string value)
        {
            PendingNormalization = value ?? "";
            HasPendingNormalization = true;
        }

        internal void ClearNormalization()
        {
            PendingNormalization = null;
            HasPendingNormalization = false;
        }

        internal bool IsDirty => !StringEquals(Text, LocalBaseline);

        internal bool NormalizeIfUnchanged(string normalized)
        {
            if (IsDirty) return false;
            Text = normalized ?? "";
            LocalBaseline = Text;
            RemoteBaseline = Text;
            return true;
        }

        static bool StringEquals(string left, string right) =>
            string.Equals(left ?? "", right ?? "", StringComparison.Ordinal);
    }

    // Game-free ownership for one daemon settings page. The page object may be discarded when
    // Settings closes, but this record keeps the editable values, raw field text, and the
    // server snapshot needed to merge a later reload.
    public sealed class DaemonConfigDraft
    {
        readonly Dictionary<string, DaemonConfigFieldState> _fields =
            new Dictionary<string, DaemonConfigFieldState>();

        public DaemonConfig Config { get; private set; }
        Wire.EditableConfig Baseline { get; set; }
        Wire.EditableConfig RemoteBaseline { get; set; }
        public Wire.EditableConfig BaselineValue() => Baseline.Clone();
        public bool Loaded { get; private set; }
        public bool Saving { get; set; }
        public string Error { get; set; }
        public readonly HashSet<string> Conflicts = new HashSet<string>();
        int _operation;

        public bool HasConflicts => Conflicts.Count > 0;

        public string ConflictMessage => !HasConflicts ? null
            : "External changes on " + string.Join(", ", Conflicts.ToArray()) +
              ". Save keeps this draft; Discard uses the server values.";

        public bool HasDraft => Loaded && Config != null;

        public bool IsDirty
        {
            get
            {
                if (!Loaded || Config == null) return false;
                if (Config.ToPatch(Baseline).Paths.Count > 0) return true;
                foreach (var state in _fields.Values)
                    if (state.IsDirty) return true;
                return false;
            }
        }

        public bool IsTextDirty(string key) =>
            _fields.TryGetValue(key, out var state) && state.IsDirty;

        public IEnumerable<string> FieldKeys(string prefix = null)
        {
            return _fields.Keys.Where(key => prefix == null || key.StartsWith(prefix,
                StringComparison.Ordinal)).OrderBy(key => key, StringComparer.Ordinal).ToList();
        }

        public bool NormalizeTextIfUnchanged(string key, string normalized)
        {
            return _fields.TryGetValue(key, out var state) && state.NormalizeIfUnchanged(normalized);
        }

        public void QueueNormalization(string key, string path, string normalized)
        {
            Field(key, path, "", false).QueueNormalization(normalized);
        }

        public void ClearQueuedNormalizations()
        {
            foreach (var state in _fields.Values) state.ClearNormalization();
        }

        public void ApplyQueuedNormalizations()
        {
            foreach (var state in _fields.Values)
            {
                if (state.HasPendingNormalization)
                    state.NormalizeIfUnchanged(state.PendingNormalization);
                state.ClearNormalization();
            }
        }

        public int BeginOperation()
        {
            unchecked { return ++_operation; }
        }

        public bool IsCurrent(int operation) => operation != 0 && operation == _operation;

        public void LoadServer(DaemonConfig server)
        {
            if (!Loaded || Config == null)
            {
                Config = server;
                Baseline = server.Snapshot();
                RemoteBaseline = Baseline.Clone();
                Loaded = true;
                Conflicts.Clear();
                return;
            }

            var oldBaseline = Baseline ?? Config.Snapshot();
            var oldRemote = RemoteBaseline ?? oldBaseline;
            var serverValue = server.Snapshot();
            var dirty = ProtoFields.Changes(Config.Snapshot(), oldBaseline);

            Conflicts.Clear();
            foreach (var pair in dirty)
                if (!ProtoFields.Equal(ProtoFields.ValueAt(oldBaseline, pair.Key), ProtoFields.ValueAt(serverValue, pair.Key))) Conflicts.Add(pair.Key);

            var merged = ProtoFields.Apply(serverValue, dirty);
            // Untouched fields should become clean against the new server snapshot. Keep the
            // old baseline only at paths that the player is still editing.
            var mergedBaseline = ProtoFields.Apply(serverValue, dirty.Keys.ToDictionary(k => k, k => ProtoFields.ValueAt(oldBaseline, k)));
            Config = DaemonConfig.FromSnapshot(merged);
            Config.CopyMetadataFrom(server);
            Baseline = mergedBaseline;
            RemoteBaseline = serverValue.Clone();
            MergeTexts(serverValue, oldRemote);
            Error = null;
        }

        // Page-specific defaults (for example Usage's visible provider rows) are not server
        // changes. Call this after the page has materialized those defaults on its first load.
        public void MarkCurrentClean()
        {
            if (!Loaded || Config == null) return;
            Baseline = Config.Snapshot();
            foreach (var state in _fields.Values) state.LocalBaseline = state.Text;
            Conflicts.Clear();
        }

        public string Text(string key, string path, string serverValue, bool zeroMeansBlank = false)
        {
            return Field(key, path, serverValue ?? "", zeroMeansBlank).Text;
        }

        public void SetText(string key, string path, string value)
        {
            Field(key, path, value ?? "", false).Text = value ?? "";
        }

        public void ResetToBaseline()
        {
            if (!Loaded || RemoteBaseline == null) return;
            var metadata = Config;
            Config = DaemonConfig.FromSnapshot(RemoteBaseline);
            Config.CopyMetadataFrom(metadata);
            foreach (var state in _fields.Values)
            {
                state.Text = ValueText(ValueAt(RemoteBaseline, state.Path),
                    state.RemoteBaseline, state.ZeroMeansBlank);
                state.LocalBaseline = state.Text;
                state.RemoteBaseline = state.Text;
            }
            Baseline = RemoteBaseline.Clone();
            Error = null;
            Conflicts.Clear();
        }

        public Dictionary<string, string> TextSnapshot() =>
            _fields.Values.ToDictionary(state => state.Key, state => state.Text);

        public void Acknowledge(Wire.EditableConfig submitted, Dictionary<string, string> submittedTexts)
        {
            Baseline = submitted.Clone();
            RemoteBaseline = submitted.Clone();
            foreach (var pair in submittedTexts)
            {
                if (_fields.TryGetValue(pair.Key, out var state))
                {
                    state.LocalBaseline = pair.Value;
                    state.RemoteBaseline = pair.Value;
                }
            }
            Error = null;
            Conflicts.Clear();
        }

        DaemonConfigFieldState Field(string key, string path, string serverValue,
                                     bool zeroMeansBlank)
        {
            if (!_fields.TryGetValue(key, out var state))
            {
                state = new DaemonConfigFieldState(key, path, serverValue, zeroMeansBlank);
                _fields[key] = state;
            }
            else state.Configure(path, zeroMeansBlank);
            return state;
        }

        static void MergeTexts(Wire.EditableConfig server, Wire.EditableConfig remoteBaseline,
                               Dictionary<string, DaemonConfigFieldState> fields,
                               HashSet<string> conflicts)
        {
            foreach (var state in fields.Values)
            {
                bool dirty = state.IsDirty;
                string serverText = ValueText(ValueAt(server, state.Path), "", state.ZeroMeansBlank);
                string remoteText = remoteBaseline == null
                    ? state.RemoteBaseline
                    : ValueText(ValueAt(remoteBaseline, state.Path), state.RemoteBaseline,
                        state.ZeroMeansBlank);
                if (!dirty)
                {
                    state.Text = serverText;
                    state.LocalBaseline = serverText;
                }
                else if (!StringEquals(serverText, remoteText))
                {
                    conflicts.Add(state.Path);
                }
                state.RemoteBaseline = serverText;
            }
        }

        void MergeTexts(Wire.EditableConfig server, Wire.EditableConfig remoteBaseline) =>
            MergeTexts(server, remoteBaseline, _fields, Conflicts);

        static object ValueAt(Wire.EditableConfig value, string path) => ProtoFields.ValueAt(value, path);
        static string ValueText(object value, string fallback, bool zeroMeansBlank = false)
        {
            if (value == null) return fallback ?? "";
            if (value is string text) return text;
            if (value is bool flag) return flag ? "true" : "false";
            if (value is object[] array) return string.Join("\n", array.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)));
            if (zeroMeansBlank && Convert.ToDouble(value, CultureInfo.InvariantCulture) == 0) return "";
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        static bool StringEquals(string left, string right) =>
            string.Equals(left ?? "", right ?? "", StringComparison.Ordinal);
    }
}
