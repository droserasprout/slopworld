using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SlopWorld
{
    // Game-free ownership for one daemon settings page. The page object may be discarded when
    // Settings closes, but this record keeps the editable values, raw field text, and the
    // server snapshot needed to merge a later reload.
    public sealed class DaemonConfigDraft
    {
        readonly HashSet<string> _blankZeroTexts = new HashSet<string>();
        readonly Dictionary<string, string> _texts = new Dictionary<string, string>();
        readonly Dictionary<string, string> _textBaselines = new Dictionary<string, string>();
        readonly Dictionary<string, string> _textRemoteBaselines =
            new Dictionary<string, string>();
        readonly Dictionary<string, string> _textPaths = new Dictionary<string, string>();
        readonly Dictionary<string, string> _queuedNormalizations =
            new Dictionary<string, string>();

        public DaemonConfig Config { get; private set; }
        public string BaselineJson { get; private set; }
        public string RemoteBaselineJson { get; private set; }
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
                if (Config.ToPatchJson(BaselineJson) != "{}") return true;
                foreach (var pair in _texts)
                    if (!StringEquals(pair.Value, _textBaselines[pair.Key])) return true;
                return false;
            }
        }

        public bool IsTextDirty(string key) =>
            _texts.ContainsKey(key) && !StringEquals(_texts[key], _textBaselines[key]);

        public bool NormalizeTextIfUnchanged(string key, string normalized)
        {
            if (!_texts.ContainsKey(key) || IsTextDirty(key)) return false;
            string value = normalized ?? "";
            _texts[key] = value;
            _textBaselines[key] = value;
            _textRemoteBaselines[key] = value;
            return true;
        }

        public void QueueNormalization(string key, string path, string normalized)
        {
            _textPaths[key] = path;
            _queuedNormalizations[key] = normalized ?? "";
        }

        public void ClearQueuedNormalizations() => _queuedNormalizations.Clear();

        public void ApplyQueuedNormalizations()
        {
            foreach (var pair in _queuedNormalizations)
                NormalizeTextIfUnchanged(pair.Key, pair.Value);
            _queuedNormalizations.Clear();
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
                BaselineJson = server.ToPatchJson();
                RemoteBaselineJson = BaselineJson;
                Loaded = true;
                Conflicts.Clear();
                return;
            }

            string oldBaseline = BaselineJson ?? Config.ToPatchJson();
            string oldRemote = RemoteBaselineJson ?? oldBaseline;
            string serverJson = server.ToPatchJson();
            var dirty = JVal.Parse(Config.ToPatchJson(oldBaseline));
            var oldBaselineValue = JVal.Parse(oldBaseline);
            var serverValue = JVal.Parse(serverJson);

            Conflicts.Clear();
            CollectConflicts(dirty, oldBaselineValue, serverValue, "", Conflicts);

            var merged = JVal.Merge(serverValue, dirty);
            // Untouched fields should become clean against the new server snapshot. Keep the
            // old baseline only at paths that the player is still editing.
            var mergedBaseline = JVal.Merge(serverValue,
                JVal.OverlayByShape(dirty, oldBaselineValue));
            Config = DaemonConfig.FromJson(merged);
            BaselineJson = JVal.ToJson(mergedBaseline);
            RemoteBaselineJson = serverJson;
            MergeTexts(serverValue, JVal.Parse(oldRemote));
            Error = null;
        }

        // Page-specific defaults (for example Usage's visible provider rows) are not server
        // changes. Call this after the page has materialized those defaults on its first load.
        public void MarkCurrentClean()
        {
            if (!Loaded || Config == null) return;
            BaselineJson = Config.ToPatchJson();
            foreach (var key in new List<string>(_texts.Keys))
                _textBaselines[key] = _texts[key];
            Conflicts.Clear();
        }

        public string Text(string key, string path, string serverValue, bool zeroMeansBlank = false)
        {
            _textPaths[key] = path;
            if (zeroMeansBlank) _blankZeroTexts.Add(key);
            if (!_texts.ContainsKey(key))
            {
                _texts[key] = serverValue ?? "";
                _textBaselines[key] = _texts[key];
                _textRemoteBaselines[key] = _texts[key];
            }
            return _texts[key];
        }

        public void SetText(string key, string path, string value)
        {
            _textPaths[key] = path;
            _texts[key] = value ?? "";
            if (!_textBaselines.ContainsKey(key)) _textBaselines[key] = _texts[key];
            if (!_textRemoteBaselines.ContainsKey(key))
                _textRemoteBaselines[key] = _textBaselines[key];
        }

        public void ResetToBaseline()
        {
            if (!Loaded || string.IsNullOrEmpty(RemoteBaselineJson)) return;
            Config = DaemonConfig.FromJson(JVal.Parse(RemoteBaselineJson));
            foreach (var key in new List<string>(_texts.Keys))
            {
                string fallback = _textRemoteBaselines.ContainsKey(key)
                    ? _textRemoteBaselines[key] : _textBaselines[key];
                _texts[key] = ValueText(ValueAt(JVal.Parse(RemoteBaselineJson), _textPaths[key]),
                    fallback, _blankZeroTexts.Contains(key));
                _textBaselines[key] = _texts[key];
                _textRemoteBaselines[key] = _texts[key];
            }
            BaselineJson = RemoteBaselineJson;
            Error = null;
            Conflicts.Clear();
        }

        public Dictionary<string, string> TextSnapshot() =>
            new Dictionary<string, string>(_texts);

        public void Acknowledge(string submittedJson, Dictionary<string, string> submittedTexts)
        {
            BaselineJson = submittedJson;
            RemoteBaselineJson = submittedJson;
            foreach (var pair in submittedTexts)
            {
                _textBaselines[pair.Key] = pair.Value;
                _textRemoteBaselines[pair.Key] = pair.Value;
            }
            Error = null;
            Conflicts.Clear();
        }

        static void MergeTexts(JVal server, JVal remoteBaseline,
                               Dictionary<string, string> texts,
                               Dictionary<string, string> baselines,
                               Dictionary<string, string> remoteBaselines,
                               Dictionary<string, string> paths,
                               HashSet<string> conflicts, HashSet<string> blankZeroTexts)
        {
            foreach (var key in new List<string>(texts.Keys))
            {
                bool dirty = !StringEquals(texts[key], baselines[key]);
                string serverText = ValueText(ValueAt(server, paths[key]), "", blankZeroTexts.Contains(key));
                string remoteText = remoteBaseline == null
                    ? remoteBaselines[key]
                    : ValueText(ValueAt(remoteBaseline, paths[key]), remoteBaselines[key],
                        blankZeroTexts.Contains(key));
                if (!dirty)
                {
                    texts[key] = serverText;
                    baselines[key] = serverText;
                }
                else if (!StringEquals(serverText, remoteText))
                {
                    conflicts.Add(paths[key]);
                }
                remoteBaselines[key] = serverText;
            }
        }

        void MergeTexts(JVal server, JVal remoteBaseline) =>
            MergeTexts(server, remoteBaseline, _texts, _textBaselines,
                _textRemoteBaselines, _textPaths, Conflicts, _blankZeroTexts);

        static void CollectConflicts(JVal patch, JVal oldBaseline, JVal server,
                                     string path, HashSet<string> conflicts)
        {
            if (patch == null || patch.IsNull) return;
            if (patch.Obj != null)
            {
                foreach (var pair in patch.Obj)
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
