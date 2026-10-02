using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Project folding and filtering are sidebar preferences, not geometry. This object owns
    // their lazy settings mirrors so a row layout can consume stable sets without knowing how
    // they are persisted.
    sealed class SidebarProjectState
    {
        public const string NoProject = "[none]";

        HashSet<string> _folded;
        HashSet<string> _filter;
        string _foldedSource;
        string _filterSource;
        int _revision;

        public int Revision
        {
            get
            {
                _ = Folded;
                _ = Filter;
                return _revision;
            }
        }

        public HashSet<string> Folded
        {
            get
            {
                string source = Settings.FoldedProjects;
                if (_folded == null || _foldedSource != source)
                {
                    _folded = ParseNames(source);
                    _foldedSource = source;
                    _revision++;
                }
                return _folded;
            }
        }

        public HashSet<string> Filter
        {
            get
            {
                string source = Settings.SidebarFilter;
                if (_filter == null || _filterSource != source)
                {
                    _filter = ParseNames(source);
                    _filterSource = source;
                    _revision++;
                }
                return _filter;
            }
        }

        public void SetFolded(string key, bool on)
        {
            var folded = Folded;
            bool changed = on ? folded.Add(key) : folded.Remove(key);
            if (changed) Save(folded, value => Settings.S.foldedProjects = value);
        }

        public void SetFolded(IEnumerable<string> keys, bool on)
        {
            var folded = Folded;
            bool changed = false;
            foreach (string key in keys)
                changed |= on ? folded.Add(key) : folded.Remove(key);
            if (changed) Save(folded, value => Settings.S.foldedProjects = value);
        }

        static HashSet<string> ParseNames(string source) =>
            new HashSet<string>((source ?? "").Split(new[] { '\n' },
                StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

        public void ToggleFilter(string key)
        {
            if (key.Length == 0)
            {
                if (!Filtering) return;
                Filter.Clear();
            }
            else if (!Filter.Remove(key)) Filter.Add(key);

            Save(Filter, value => Settings.S.sidebarFilter = value);
        }

        public bool Filtering => Filter.Count > 0;
        public bool Ticked(string key) => Filter.Contains(key);

        public bool Passes(string project)
        {
            string key = string.IsNullOrEmpty(project) ? NoProject : project;
            return !Filtering || Filter.Contains(key);
        }

        public string FilterLabel
        {
            get
            {
                if (Filter.Count != 1) return Filter.Count + " projects";
                foreach (var key in Filter) return key;
                return "";
            }
        }

        static void Save(HashSet<string> values, Action<string> assign)
        {
            var names = new List<string>(values);
            names.Sort(StringComparer.Ordinal);
            assign(string.Join("\n", names.ToArray()));
            Settings.S.Write();
        }
    }
}
