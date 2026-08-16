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

        public HashSet<string> Folded
        {
            get
            {
                if (_folded == null)
                {
                    _folded = new HashSet<string>();
                    foreach (var name in Settings.FoldedProjects.Split('\n'))
                        if (name.Length > 0) _folded.Add(name);
                }
                return _folded;
            }
        }

        public HashSet<string> Filter
        {
            get
            {
                if (_filter == null)
                {
                    _filter = new HashSet<string>();
                    foreach (var name in Settings.SidebarFilter.Split('\n'))
                        if (name.Length > 0) _filter.Add(name);
                }
                return _filter;
            }
        }

        public void SetFolded(string key, bool on)
        {
            if (on) Folded.Add(key);
            else Folded.Remove(key);
            Save(Folded, value => Settings.S.foldedProjects = value);
        }

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
