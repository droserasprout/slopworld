namespace SlopWorld
{
    // Owns revision-preview identity and settlement. The dialog owns transport and fields.
    // Identical revision text can resolve to different commits, so every attempt gets a token.
    internal sealed class WorkerBasePreviewState
    {
        readonly OperationGate _requests = new OperationGate();
        string _caller;
        string _revision;
        public string Commit { get; private set; } = "";
        public string Error { get; private set; }

        public bool Matches(string caller, string revision) =>
            _caller == caller && _revision == revision;

        public long Begin(string caller, string revision)
        {
            _caller = caller;
            _revision = revision;
            Commit = "";
            Error = null;
            return _requests.Begin();
        }

        public void Accept(long request, string caller, string revision, string commit)
        {
            if (!_requests.IsCurrent(request) || !Matches(caller, revision))
                return;
            _requests.Invalidate();
            Commit = commit;
            Error = null;
        }

        public void Fail(long request, string caller, string revision, string error)
        {
            if (!_requests.IsCurrent(request) || !Matches(caller, revision))
                return;
            _requests.Invalidate();
            Commit = "";
            Error = error;
        }

        public void Reset()
        {
            _requests.Invalidate();
            _caller = _revision = null;
            Commit = "";
            Error = null;
        }
    }
}
