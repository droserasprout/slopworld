using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Endpoint-scoped task drafts survive view changes. Persistence and worker status
    // stay in the daemon; TodoText owns edits to the user's fields only.
    public sealed partial class PlannerView : ContentView
    {
        static readonly Dictionary<string, PlannerView> Views =
            new Dictionary<string, PlannerView>();
        readonly string _endpoint;
        FieldLifetime _fields = new FieldLifetime();
        Wire.PlannerDocument _document;
        IDisposable _pollingDemand;
        List<TodoEntry> _entries = new List<TodoEntry>();
        TodoEntry _selected;
        TodoDraft _draft;
        string _text = "",
            _error;
        string _quickAdd = "",
            _search = "",
            _projectFilter = "",
            _contextFilter = "";
        TodoFilter _filter = TodoFilter.Active;
        TodoSort _sort = TodoSort.Due;
        bool _busy,
            _open,
            _source,
            _detailOpen;
        int _generation,
            _presentation;
        readonly SmoothScroll _listScroll = new SmoothScroll();
        readonly ScrollableListing _navigation = new ScrollableListing();
        readonly ScrollableListing _details = new ScrollableListing();
        bool DraftDirty => _selected != null && _draft != null && _draft.Changed(_selected);
        bool Dirty =>
            _document != null
            && (
                _text != _document.Text
                || DraftDirty
                || _descriptions.Values.Any(draft => draft.Dirty)
            );
        bool Ready => !_busy && _document != null;

        // Requests belong to the cached endpoint draft, not the visible window. Closing
        // must not discard a committed save or permit a second action while one is pending.
        bool Current(int generation) => _generation == generation;

        bool IsOpenEndpoint => _open && _endpoint == DaemonClient.BaseUrl;

        public override string Title => "Planner";
        public override PanelSize MinimumSize => new PanelSize(580f, 420f);

        PlannerView(string endpoint)
        {
            _endpoint = endpoint;
        }

        public static void Toggle() => WorkspaceWindow.ToggleContent(CurrentView);

        public static void Open() => WorkspaceWindow.OpenContent(CurrentView());

        static PlannerView CurrentView()
        {
            string endpoint = DaemonClient.BaseUrl;
            if (!Views.TryGetValue(endpoint, out var view))
                Views[endpoint] = view = new PlannerView(endpoint);
            return view;
        }

        public override void Opened()
        {
            _open = true;
            _pollingDemand ??= SessionHub.Instance.TaskStore.OpenReader();
            _fields = new FieldLifetime();
            EnsureDescription();
            if (_document == null && !_busy)
                Load();
        }

        public override void Closed()
        {
            _open = false;
            _pollingDemand?.Dispose();
            _pollingDemand = null;
            _presentation++;
            _fields.Cancel();
        }

        int Begin()
        {
            _busy = true;
            _error = null;
            return ++_generation;
        }

        void Failed(int generation, string error)
        {
            if (!Current(generation))
                return;
            _busy = false;
            _error = error;
        }

        void Rebuild(string key = null)
        {
            key = key ?? _selected?.Key;
            uint? line = _selected?.Line;
            _entries = TodoText.Parse(_text);
            var selected =
                _entries.FirstOrDefault(entry => entry.Key == key)
                ?? _entries.FirstOrDefault(entry => entry.Line == line);
            SetSelection(selected);
        }

        void SetSelection(TodoEntry entry)
        {
            _fields.Cancel();
            _fields = new FieldLifetime();
            _selected = entry;
            _draft = entry == null ? null : new TodoDraft(entry);
            EnsureDescription();
        }

        Action EditSelected(Action edit)
        {
            var draft = _draft;
            int generation = _generation,
                presentation = _presentation;
            return () =>
            {
                if (
                    IsOpenEndpoint
                    && Ready
                    && Current(generation)
                    && presentation == _presentation
                    && ReferenceEquals(_draft, draft)
                )
                    edit();
            };
        }

        void Select(TodoEntry entry)
        {
            if (_selected?.Key == entry.Key)
            {
                _detailOpen = true;
                return;
            }
            if (DraftDirty)
            {
                _error = "Save or discard the task you're editing first.";
                return;
            }
            SetSelection(entry);
            _error = null;
            _detailOpen = true;
        }

        void Accept(Wire.PlannerDocument document, bool discard = false)
        {
            // A refresh can change link metadata without changing the todo text.
            // Keep the existing rows, field lifetime and selection until text changes.
            bool rebuild = _document == null || _text != document.Text || discard;
            _document = document;
            _text = document.Text;
            if (rebuild)
                Rebuild();
            _busy = false;
        }

        void Load()
        {
            if (_busy)
                return;
            // Only the initial load needs a loading presentation. Refreshing an existing
            // document must not disable/repaint its fields or replace its status text.
            bool initial = _document == null;
            int generation = initial ? Begin() : ++_generation;
            DaemonClient.Get<Wire.PlannerDocument>(
                WireProtocol.Routes.Planner,
                value =>
                {
                    if (!Current(generation))
                        return;
                    // Editing remains available during refresh. Keep local drafts and their
                    // original revision so a subsequent save still detects external changes.
                    if (!initial && Dirty)
                        return;
                    _error = null;
                    Accept(value);
                },
                error => Failed(generation, error)
            );
        }

        bool ApplyDraft()
        {
            if (!DraftDirty)
                return true;
            try
            {
                _text = TodoText.Update(_text, _selected, _draft, DateTime.Today);
                Rebuild();
                return true;
            }
            catch (Exception error)
            {
                _error = error.Message;
                return false;
            }
        }

        void Save()
        {
            if (!IsOpenEndpoint || !Ready || !ApplyDraft())
                return;
            int generation = Begin();
            if (_text == _document.Text)
            {
                SaveDescriptions(generation);
                return;
            }
            DaemonClient.Put<Wire.PlannerDocument>(
                WireProtocol.Routes.Planner,
                new Wire.PlannerSaveReq { Text = _text, Revision = _document.Revision },
                value =>
                {
                    if (Current(generation))
                    {
                        Accept(value);
                        _busy = true;
                        SaveDescriptions(generation);
                    }
                },
                error => Failed(generation, error)
            );
        }

        void Discard()
        {
            if (!Ready || !IsOpenEndpoint)
                return;
            foreach (var draft in _descriptions.Values)
            {
                draft.Text = draft.Saved;
                draft.Error = null;
            }
            _error = null;
            Accept(_document, discard: true);
        }

        void Add()
        {
            try
            {
                _text = TodoText.Add(
                    _text,
                    _quickAdd,
                    _projectFilter,
                    DateTime.Today,
                    _contextFilter
                );
                Rebuild();
                Select(_entries.Last());
                _quickAdd = "";
                Save();
            }
            catch (Exception error)
            {
                _error = error.Message;
            }
        }

        void Toggle(TodoEntry entry)
        {
            if (DraftDirty)
            {
                _error = "Save or discard the task you're editing first.";
                return;
            }
            try
            {
                var draft = new TodoDraft(entry) { Completed = !entry.Completed };
                _text = TodoText.Update(_text, entry, draft, DateTime.Today);
                Rebuild();
                Save();
            }
            catch (Exception error)
            {
                _error = error.Message;
            }
        }

        void DeleteSelected()
        {
            try
            {
                _text = TodoText.Replace(_text, _selected, "");
                SetSelection(null);
                Rebuild();
                _detailOpen = false;
                Save();
            }
            catch (Exception error)
            {
                _error = error.Message;
            }
        }

        Wire.PlannerItem Linked(TodoEntry entry)
        {
            if (entry == null)
                return null;
            var linked = _document?.Items.FirstOrDefault(item =>
                item.Id.Length > 0 ? item.Id == entry.Id : item.Line == entry.Line
            );
            if (linked != null)
            {
                foreach (var link in linked.Links.Where(value => value.Kind == "task"))
                {
                    var task = SessionHub.Instance.Tasks.FirstOrDefault(value =>
                        value.Id == link.Id
                    );
                    if (task != null)
                        link.Status = TaskInfo.StatusText(task.Status);
                }
            }
            return linked;
        }

        void OpenTask(string id)
        {
            int presentation = _presentation;
            int generation = Begin();
            DaemonClient.Get<Wire.TaskResult>(
                WireProtocol.Routes.Tasks + "/" + Uri.EscapeDataString(id),
                value =>
                {
                    if (!Current(generation))
                        return;
                    _busy = false;
                    if (IsOpenEndpoint && presentation == _presentation && value.Task != null)
                    {
                        var task = TaskInfo.FromWire(value.Task);
                        // The reader follows the shared board snapshot. Seed it before
                        // opening when this link arrived ahead of the next task poll.
                        SessionHub.Instance.TaskStore.Add(task);
                        TaskDetailView.Open(task, this);
                    }
                },
                error => Failed(generation, error),
                session: TaskInfo.Host
            );
        }

        void ToggleSource()
        {
            if (!ApplyDraft())
                return;
            if (_source)
                Rebuild();
            _source = !_source;
        }

        public override void Draw(Rect body)
        {
            using (WidgetState.Save())
            using (FieldLifetimeScope.Push(_fields))
            {
                if (_endpoint != DaemonClient.BaseUrl)
                {
                    UiText.RowLabel(
                        body,
                        "Endpoint changed. Reopen Planner for the current daemon."
                    );
                    return;
                }
                DrawBoard(body.ContractedBy(UiTheme.GapM));
            }
        }
    }
}
