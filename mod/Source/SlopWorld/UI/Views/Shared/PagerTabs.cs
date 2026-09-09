using System.Collections.Generic;

namespace SlopWorld
{
    // One replaceable preview plus any previews the user pinned by double-clicking their
    // routed header. Files and Git each own one collection, so changing views does not make a
    // pinned diff release a pinned file preview (or vice versa).
    sealed class PagerTabs
    {
        Pager _preview = new Pager();
        readonly List<Pager> _locked = new List<Pager>();

        public Pager Preview
        {
            get
            {
                RetireDead();
                return _preview;
            }
        }

        public Pager ForPreview()
        {
            RetireDead();
            if (_preview.Locked)
            {
                _locked.Add(_preview);
                _preview = new Pager();
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

        public bool Lock(string session)
        {
            RetireDead();
            if (_preview.Session == session) return _preview.Lock();
            foreach (var pager in _locked)
                if (pager.Session == session) return pager.Lock();
            return false;
        }

        public bool LockPreview(string project, string key)
        {
            RetireDead();
            if (_preview.LockPreview(project, key)) return true;
            foreach (var pager in _locked)
                if (pager.Matches(project, key)) return pager.Lock();
            return false;
        }

        public string FilePath(string session)
        {
            if (_preview.Session == session) return _preview.FilePath;
            foreach (var pager in _locked)
                if (pager.Session == session) return pager.FilePath;
            return null;
        }

        public bool IsSession(string session)
        {
            RetireDead();
            if (session == null) return false;
            if (_preview.Session == session && _preview.Alive) return true;
            foreach (var pager in _locked)
                if (pager.Session == session && pager.Alive) return true;
            return false;
        }

        public bool IsLocked(string session)
        {
            RetireDead();
            if (session == null) return false;
            foreach (var pager in _locked)
                if (pager.Session == session && pager.Alive) return true;
            return _preview.Session == session && _preview.Alive && _preview.Locked;
        }

        // Leaving Files/Git, selecting a directory, or refreshing releases only the preview
        // slot. Pinned sessions follow the same lifetime as an edit session and stay available
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
            foreach (var pager in _locked) pager.CloseIf(session);
            RetireDead();
        }

        public bool CloseTab(string session)
        {
            RetireDead();
            bool closed = _preview.CloseTab(session);
            foreach (var pager in _locked) closed |= pager.CloseTab(session);
            RetireDead();
            return closed;
        }

        void RetireDead()
        {
            for (int i = _locked.Count - 1; i >= 0; i--)
                if (!_locked[i].Alive) _locked.RemoveAt(i);
        }
    }
}
