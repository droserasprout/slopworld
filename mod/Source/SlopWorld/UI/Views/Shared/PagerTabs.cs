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
        void CloseIf(string session);
        bool CloseTab(string session);
    }

    class PreviewTabs<T> where T : class, IPreviewTab
    {
        readonly System.Func<T> _create;
        readonly System.Action _beforePreview;

        public PreviewTabs(System.Func<T> create, System.Action beforePreview = null)
        {
            _create = create;
            _beforePreview = beforePreview;
        }

        T _preview;
        readonly List<T> _locked = new List<T>();

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
                _preview = _create();
            }
            return _preview;
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

        void RetireDead()
        {
            if (_preview == null) _preview = _create();
            for (int i = _locked.Count - 1; i >= 0; i--)
                if (!_locked[i].Alive) _locked.RemoveAt(i);
        }
    }

    // One replaceable preview plus any previews the user pinned by double-clicking their
    // routed header. Files and Git share a collection; Search retains its own readers.
    sealed class PagerTabs : PreviewTabs<Pager>
    {
        public PagerTabs(System.Action beforePreview = null)
            : base(() => new Pager(), beforePreview) { }
    }
}
