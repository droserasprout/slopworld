using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // Game-free catalog owner. Inject transport, clock, and project visibility.
    // A late reply must match the project identity and its request generation.
    public sealed class BrowseScopeCatalog
    {
        sealed class Catalog
        {
            public string Name, Path, Error;
            public bool Loading;
            public float Next;
            public int Token;
            public List<BrowseScope> Scopes = new List<BrowseScope>();
        }
        readonly Dictionary<string, Catalog> _catalogs = new Dictionary<string, Catalog>();
        readonly Dictionary<string, BrowseScope> _scopes = new Dictionary<string, BrowseScope>();
        readonly Func<string, bool> _passes;
        readonly Func<float> _now;
        readonly Action<string, Action<List<BrowseScope>, string>> _load;
        int _inFlight, _projects = -1;
        string _filter, _saved;
        bool _updating;
        BrowseScopeChoices _choices = new BrowseScopeChoices("");
        public int Revision { get; private set; }
        public event Action Changed;
        public BrowseScopeCatalog(Func<string, bool> passes, Func<float> now,
            Action<string, Action<List<BrowseScope>, string>> load)
        { _passes = passes; _now = now; _load = load; }
        void Bump()
        {
            _scopes.Clear();
            foreach (var scope in _catalogs.Values.SelectMany(c => c.Scopes)) _scopes[scope.Key] = scope;
            Revision++;
            Changed?.Invoke();
        }
        public BrowseScope Find(string key) => key != null && _scopes.TryGetValue(key, out var scope) ? scope.Copy() : null;
        public bool Enabled(string key)
        {
            var s = Find(key);
            return s != null && s.Ready && _choices.Chosen(s) && _passes(s.Project);
        }
        public bool Chosen(BrowseScope scope) => _choices.Chosen(scope);
        // Never expose retained descriptors or collection storage to callers.
        public IReadOnlyList<BrowseScope> All(string projectId) => _catalogs.TryGetValue(projectId, out var c)
            ? c.Scopes.Select(s => s.Copy()).ToList().AsReadOnly() : new List<BrowseScope>().AsReadOnly();
        public bool HasWorktrees(string projectId) => All(projectId).Any(s => s.Worktree != "main");
        public string Error(string projectId) => _catalogs.TryGetValue(projectId, out var c) ? c.Error : null;
        public List<BrowseScope> EnabledScopes() => _catalogs.Values.SelectMany(c => c.Scopes)
            .Where(s => s.Ready && _choices.Chosen(s) && _passes(s.Project))
            .OrderBy(s => s.Project, StringComparer.Ordinal).ThenBy(s => s.Worktree == "main" ? 0 : 1)
            .ThenBy(s => s.Name, StringComparer.Ordinal).Select(s => s.Copy()).ToList();
        public string EmptyReason()
        {
            if (_catalogs.Count == 0) return "No projects are available.";
            var visible = _catalogs.Values.Where(c => _passes(c.Name)).ToList();
            if (visible.Count == 0) return "No projects match the sidebar filter.";
            if (visible.Any(c => c.Error != null)) return "Unable to load checkouts. Check the project filter for details.";
            if (visible.Any(c => c.Loading || c.Scopes.Any(s => s.Phase == "loading")))
                return "Loading checkouts…";
            var scopes = visible.SelectMany(c => c.Scopes).ToList();
            if (scopes.Any(s => s.Ready) && !scopes.Any(s => s.Ready && _choices.Chosen(s)))
                return "No checkouts are selected in the sidebar filter.";
            if (visible.All(c => string.IsNullOrEmpty(c.Path))) return "No project has a directory yet.";
            return "No ready checkouts are available.";
        }

        public IEnumerable<string> GroupKeys => _catalogs.Values.SelectMany(c => c.Scopes)
            .SelectMany(s => new[] { s.ProjectKey, s.Key }).Distinct();
        public string Toggle(BrowseScope scope, Action<string> persist = null)
        {
            scope = Find(scope.Key);
            if (scope == null || !scope.Ready) return _saved;
            _choices.Toggle(scope);
            _saved = _choices.Save();
            persist?.Invoke(_saved);
            Bump();
            return _saved;
        }
        public void Invalidate()
        {
            foreach (var c in _catalogs.Values) { c.Next = 0; c.Token++; }
        }
        public void Update(IEnumerable<ProjectInfo> projects, int projectRevision, string filter, string saved, bool online, bool includeHidden = false)
        {
            if (_updating) return;
            _updating = true;
            try
            {
                bool changed = false;
                if (_saved != saved) { _saved = saved; _choices = new BrowseScopeChoices(saved); changed = true; }
                if (_filter != filter) { _filter = filter; changed = true; }
                if (_projects != projectRevision)
                {
                    ReconcileProjects(projects, projectRevision);
                    changed = true;
                }
                if (changed) Bump();
                if (online) ScheduleLoads(includeHidden);
            }
            finally { _updating = false; }
        }
        void ReconcileProjects(IEnumerable<ProjectInfo> projects, int projectRevision)
        {
            _projects = projectRevision;
            var list = projects.ToList();
            var ids = new HashSet<string>(list.Select(BrowseScope.ProjectIdOf));
            foreach (var id in _catalogs.Keys.Where(k => !ids.Contains(k)).ToList()) _catalogs.Remove(id);
            foreach (var p in list)
            {
                string id = BrowseScope.ProjectIdOf(p);
                if (!_catalogs.TryGetValue(id, out var c)) _catalogs[id] = c = new Catalog();
                if (c.Name == p.Name && c.Path == p.ExpandedDir) continue;
                c.Name = p.Name; c.Path = p.ExpandedDir; c.Error = null; c.Token++; c.Next = 0;
                // Replace descriptors, never mutate a scope captured by a Search request.
                c.Scopes = c.Scopes.Select(s => new BrowseScope { ProjectId = id, Project = p.Name,
                    Worktree = s.Worktree, Name = s.Name, Path = s.Path, Phase = s.Phase, Branch = s.Branch, Error = s.Error }).ToList();
                var main = c.Scopes.FirstOrDefault(s => s.Worktree == "main");
                if (main == null) c.Scopes.Insert(0, main = new BrowseScope { ProjectId = id, Worktree = "main", Name = "Main checkout", Phase = "loading" });
                if (main.Path != p.ExpandedDir) main.Phase = "loading";
                main.Project = p.Name; main.Path = p.ExpandedDir;
            }
        }

        void ScheduleLoads(bool includeHidden)
        {
            foreach (var entry in _catalogs.OrderBy(e => e.Value.Next).ToList())
            {
                var c = entry.Value;
                if (_inFlight >= 2) break;
                // The open filter menu needs catalog metadata for hidden projects too,
                // so it can distinguish a plain checkbox from a worktree submenu.
                if (c.Loading || (!includeHidden && !_passes(c.Name)) || _now() < c.Next) continue;
                c.Loading = true; _inFlight++;
                string id = entry.Key;
                int token = c.Token;
                _load(c.Name, (reply, error) => Complete(id, c, token, reply, error));
            }
        }

        static string Stamp(IEnumerable<BrowseScope> scopes) => string.Join("\n", scopes.Select(s =>
            string.Join("\t", s.Key, s.Name, s.Path, s.Phase, s.Branch, s.Error)));
        void Complete(string id, Catalog c, int token, List<BrowseScope> reply, string error)
        {
            _inFlight--; c.Loading = false;
            if (token != c.Token || !_catalogs.TryGetValue(id, out var current) || !ReferenceEquals(current, c)) return;
            c.Next = _now() + 5f;
            string nextError = reply == null ? error ?? "Unable to load worktrees." : null;
            bool errorChanged = c.Error != nextError;
            c.Error = nextError;
            if (reply == null)
            {
                // Preserve known checkouts and choices, but surface the failure to the menu.
                if (errorChanged) Bump();
                return;
            }
            reply = reply.Select(s => s.Copy()).ToList();
            foreach (var scope in reply) { scope.ProjectId = id; scope.Project = c.Name; }
            if (!errorChanged && Stamp(reply) == Stamp(c.Scopes)) return;
            c.Scopes = reply;
            Bump();
        }
    }
}
