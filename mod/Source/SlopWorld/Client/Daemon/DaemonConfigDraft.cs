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
        JVal Baseline { get; set; }
        JVal RemoteBaseline { get; set; }
        public string BaselineJson => JVal.ToJson(Baseline);
        public string RemoteBaselineJson => JVal.ToJson(RemoteBaseline);
        public JVal BaselineValue() => JVal.Clone(Baseline);
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
                if (Config.ToPatch(Baseline).ObjectItems.Any()) return true;
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
                Baseline = server.ToPatch();
                RemoteBaseline = JVal.Clone(Baseline);
                Loaded = true;
                Conflicts.Clear();
                return;
            }

            var oldBaseline = Baseline ?? Config.ToPatch();
            var oldRemote = RemoteBaseline ?? oldBaseline;
            var serverValue = server.ToPatch();
            var dirty = Config.ToPatch(oldBaseline);

            Conflicts.Clear();
            CollectConflicts(dirty, oldBaseline, serverValue, "", Conflicts);

            var merged = JVal.Merge(serverValue, dirty);
            // Untouched fields should become clean against the new server snapshot. Keep the
            // old baseline only at paths that the player is still editing.
            var mergedBaseline = JVal.Merge(serverValue,
                JVal.OverlayByShape(dirty, oldBaseline));
            Config = DaemonConfig.FromJson(merged);
            Config.CopyMetadataFrom(server);
            Baseline = mergedBaseline;
            RemoteBaseline = JVal.Clone(serverValue);
            MergeTexts(serverValue, oldRemote);
            Error = null;
        }

        // Page-specific defaults (for example Usage's visible provider rows) are not server
        // changes. Call this after the page has materialized those defaults on its first load.
        public void MarkCurrentClean()
        {
            if (!Loaded || Config == null) return;
            Baseline = Config.ToPatch();
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
            Config = DaemonConfig.FromJson(RemoteBaseline);
            Config.CopyMetadataFrom(metadata);
            foreach (var state in _fields.Values)
            {
                state.Text = ValueText(ValueAt(RemoteBaseline, state.Path),
                    state.RemoteBaseline, state.ZeroMeansBlank);
                state.LocalBaseline = state.Text;
                state.RemoteBaseline = state.Text;
            }
            Baseline = JVal.Clone(RemoteBaseline);
            Error = null;
            Conflicts.Clear();
        }

        public Dictionary<string, string> TextSnapshot() =>
            _fields.Values.ToDictionary(state => state.Key, state => state.Text);

        public void Acknowledge(string submittedJson, Dictionary<string, string> submittedTexts)
        {
            Acknowledge(JVal.Parse(submittedJson), submittedTexts);
        }

        public void Acknowledge(JVal submitted, Dictionary<string, string> submittedTexts)
        {
            Baseline = JVal.Clone(submitted);
            RemoteBaseline = JVal.Clone(submitted);
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

        static void MergeTexts(JVal server, JVal remoteBaseline,
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

        void MergeTexts(JVal server, JVal remoteBaseline) =>
            MergeTexts(server, remoteBaseline, _fields, Conflicts);

        static void CollectConflicts(JVal patch, JVal oldBaseline, JVal server,
                                     string path, HashSet<string> conflicts)
        {
            if (patch == null || patch.IsNull) return;
            if (patch.IsObject)
            {
                foreach (var pair in patch.ObjectItems)
                    CollectConflicts(pair.Value, oldBaseline?[pair.Key], server?[pair.Key],
                        path.Length == 0 ? pair.Key : path + "." + pair.Key, conflicts);
                return;
            }

            if (!JVal.Equivalent(oldBaseline, server)) conflicts.Add(path);
        }

        static JVal ValueAt(JVal value, string path)
        {
            if (value == null || value.IsNull || string.IsNullOrEmpty(path)) return value ?? JVal.Null;
            var current = value;
            foreach (string part in path.Split('.')) current = current[part];
            return current;
        }

        static string ValueText(JVal value, string fallback, bool zeroMeansBlank = false)
        {
            if (value == null || value.IsNull) return fallback ?? "";
            // Usage interval zero means inheritance and is edited as an empty field.
            if (zeroMeansBlank && value.IsNumber && value.Num == 0) return "";
            if (value.Str != null) return value.Str;
            if (value.IsBool) return value.Bool ? "true" : "false";
            return value.Num.ToString("0", CultureInfo.InvariantCulture);
        }

        static bool StringEquals(string left, string right) =>
            string.Equals(left ?? "", right ?? "", StringComparison.Ordinal);
    }
}
