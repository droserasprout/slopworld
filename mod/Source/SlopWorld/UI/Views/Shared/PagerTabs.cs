using System.Collections.Generic;

namespace SlopWorld
{
    // A preview tab supplies the identity and the small lifetime operations that differ between
    // daemon pagers and native content. The slot/pinned collection below owns replacement,
    // pinning, reuse and cleanup for both adapters.
    interface IPreviewTab
    {
        string Session { get; }
        string FilePath { get; }
        bool Locked { get; }
        bool Alive { get; }
        bool Matches(string project, string key);
        bool Reopen();
        bool Lock();
        bool LockPreview(string project, string key);
        void Release();
        void Invalidate();
        void CloseIf(string session);
        bool CloseTab(string session);
    }

    // A metadata reply belongs to the reader that requested it, not its reusable slot.
    sealed class ReaderProbe
    {
        readonly IPreviewTab _tab;
        readonly string _session;
        readonly int _operation;
        public readonly string Path;

        public ReaderProbe(IPreviewTab tab)
        {
            _tab = tab;
            _session = tab.Session;
            _operation = (tab as Pager)?.Operation ?? 0;
            Path = tab.FilePath;
        }

        public void Apply(bool isFile, string stamp = null)
        {
            if (_tab.FilePath != Path || _tab.Session != _session) return;
            var pager = _tab as Pager;
            if (pager != null && pager.Operation != _operation) return;
            if (!isFile) _tab.Invalidate();
            else pager?.RefreshIfChanged(stamp);
        }
    }

    class PreviewTabs<T> where T : class, IPreviewTab
    {
        protected readonly System.Func<T> _create;
        readonly System.Action _beforePreview;

        public PreviewTabs(System.Func<T> create, System.Action beforePreview = null)
        {
            _create = create;
            _beforePreview = beforePreview;
        }

        protected T _preview;
        protected readonly List<T> _locked = new List<T>();

        public T Preview
        {
            get
            {
                RetireDead();
                if (_preview == null) _preview = _create();
                return _preview;
            }
        }

        public T ForPreview()
        {
            _beforePreview?.Invoke();
            RetireDead();
            if (_preview == null) _preview = _create();
            if (_preview.Locked)
            {
                _locked.Add(_preview);
                RoutedSessionRows.Invalidate();
                _preview = _create();
            }
            return _preview;
        }

        // Includes a pending preview, so deletion can cancel its eventual handoff too.
        public T Find(System.Predicate<T> matches)
        {
            RetireDead();
            if (matches(_preview)) return _preview;
            return _locked.Find(matches);
        }

        public void Invalidate(System.Predicate<T> matches)
        {
            RetireDead();
            if (matches(_preview)) _preview.Invalidate();
            foreach (var tab in _locked)
                if (matches(tab)) tab.Invalidate();
            RetireDead();
        }

        public bool Reopen(string project, string key)
        {
            RetireDead();
            if (_preview.Matches(project, key))
                return _preview.Reopen();

            foreach (var pager in _locked)
                if (pager.Matches(project, key))
                    return pager.Reopen();
            return false;
        }

        public bool ReopenSession(string session)
        {
            RetireDead();
            if (_preview.Session == session) return _preview.Reopen();
            foreach (var tab in _locked)
                if (tab.Session == session) return tab.Reopen();
            return false;
        }

        public bool ContainsSession(string session)
        {
            RetireDead();
            if (_preview.Session == session && _preview.Alive) return true;
            foreach (var tab in _locked)
                if (tab.Session == session && tab.Alive) return true;
            return false;
        }

        public IEnumerable<T> All
        {
            get
            {
                RetireDead();
                if (_preview.Alive) yield return _preview;
                foreach (var tab in _locked) yield return tab;
            }
        }

        public bool Lock(string session)
        {
            RetireDead();
            if (_preview.Session == session) return _preview.Lock();
            foreach (var tab in _locked)
                if (tab.Session == session) return tab.Lock();
            return false;
        }

        public bool LockPreview(string project, string key)
        {
            RetireDead();
            if (_preview.LockPreview(project, key)) return true;
            foreach (var tab in _locked)
                if (tab.Matches(project, key)) return tab.Lock();
            return false;
        }

        public string FilePath(string session)
        {
            if (_preview != null && _preview.Session == session) return _preview.FilePath;
            foreach (var tab in _locked)
                if (tab.Session == session) return tab.FilePath;
            return null;
        }

        public bool IsSession(string session)
        {
            RetireDead();
            if (session == null) return false;
            if (_preview.Session == session && _preview.Alive) return true;
            foreach (var tab in _locked)
                if (tab.Session == session && tab.Alive) return true;
            return false;
        }

        public bool IsLocked(string session)
        {
            RetireDead();
            if (session == null) return false;
            foreach (var tab in _locked)
                if (tab.Session == session && tab.Alive) return true;
            return _preview.Session == session && _preview.Alive && _preview.Locked;
        }

        // Selecting a directory or refreshing releases only the preview slot.
        // Pinned sessions follow the same lifetime as an edit session and stay available
        // through their routed header.
        public void ReleasePreview()
        {
            RetireDead();
            if (!_preview.Locked) _preview.Release();
            RetireDead();
        }

        public void CloseIf(string session)
        {
            RetireDead();
            _preview.CloseIf(session);
            foreach (var tab in _locked) tab.CloseIf(session);
            RetireDead();
        }

        public bool CloseTab(string session)
        {
            RetireDead();
            bool closed = _preview.CloseTab(session);
            foreach (var tab in _locked) closed |= tab.CloseTab(session);
            RetireDead();
            return closed;
        }

        protected void RetireDead()
        {
            if (_preview == null) _preview = _create();
            for (int i = _locked.Count - 1; i >= 0; i--)
                if (!_locked[i].Alive) { _locked.RemoveAt(i); RoutedSessionRows.Invalidate(); }
        }
    }

    // One replaceable preview plus any previews the user pinned by double-clicking their
    // routed header. Files and Git share a collection. Search retains its own readers.
    sealed class PagerTabs : PreviewTabs<Pager>
    {
        public bool ReuseFile(string project, string path)
        {
            var pager = Find(tab => tab != null && tab.OwnsSource(project, path));
            return pager != null && (pager.Pending(project, path) || pager.Reopen());
        }

        public void AttachRestored(SessionInfo info)
        {
            RetireDead();
            if (ContainsSession(info.Name)) return;
            string scope = string.IsNullOrEmpty(info.ReaderScope) ? info.Project : info.ReaderScope;
            if (Find(tab => tab != null && tab.Pending(scope, info.ReaderKey)) != null) return;
            var pager = _create();
            pager.AttachRestored(info);
            if (info.ReaderPinned || _preview.Alive)
            {
                if (!info.ReaderPinned) pager.Lock();
                _locked.Add(pager);
            }
            else _preview = pager;
            RoutedSessionRows.Invalidate();
        }
        public void OpenFresh(string project, string command, string label, string key)
        {
            // Replace the process inside the existing tab, retaining its pin and identity.
            var pager = Find(tab => tab.Owns(project, key)) ?? ForPreview();
            pager.Open(project, command, label, key, "", "diff");
        }

        public PagerTabs(System.Action beforePreview = null)
            : base(() => new Pager(), beforePreview) { }
    }
}
