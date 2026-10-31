using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Endpoint-scoped task drafts survive view changes. Persistence and worker dispatch
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
            _output = "",
            _error,
            _project = "",
            _template = "";
        string _quickAdd = "",
            _search = "",
            _projectFilter = "";
        TodoFilter _filter = TodoFilter.Active;
        TodoSort _sort = TodoSort.Due;
        bool _busy,
            _open,
            _source,
            _assistant,
            _detailOpen,
            _refreshAfterExecute,
            _needsRefresh;
        int _generation,
            _presentation;
        readonly SmoothScroll _listScroll = new SmoothScroll();
        readonly ScrollableListing _navigation = new ScrollableListing();
        readonly ScrollableListing _details = new ScrollableListing();
        bool DraftDirty => _selected != null && _draft != null && _draft.Changed(_selected);
        bool Dirty => _document != null && (_text != _document.Text || DraftDirty);
        bool Ready => !_busy && _document != null && !_needsRefresh;

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

        public static void Open()
        {
            string endpoint = DaemonClient.BaseUrl;
            if (!Views.TryGetValue(endpoint, out var view))
                Views[endpoint] = view = new PlannerView(endpoint);
            WorkspaceWindow.OpenContent(view);
        }

        public override void Opened()
        {
            _open = true;
            _pollingDemand ??= SessionHub.Instance.TaskStore.OpenReader();
            _fields = new FieldLifetime();
            SessionHub.Instance.Catalog.RefreshTemplates();
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
            if (_project.Length == 0)
                _project =
                    SessionHub
                        .Instance.Projects.FirstOrDefault(project =>
                            TodoText
                                .Words(entry.Projects)
                                .Any(name => name == project.Name || name == project.Id)
                        )
                        ?.Id
                    ?? "";
            _error = null;
            _assistant = false;
            _detailOpen = true;
        }

        void Accept(Wire.PlannerDocument document)
        {
            _needsRefresh = false;
            _document = document;
            _text = document.Text;
            Rebuild();
            _busy = false;
        }

        void Load(bool preserveError = false)
        {
            string previousError = preserveError ? _error : null;
            int generation = Begin();
            DaemonClient.Get<Wire.PlannerDocument>(
                WireProtocol.Routes.Planner,
                value =>
                {
                    if (Current(generation))
                    {
                        Accept(value);
                        _error = previousError;
                    }
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
            if (!ApplyDraft())
                return;
            int generation = Begin();
            DaemonClient.Put<Wire.PlannerDocument>(
                WireProtocol.Routes.Planner,
                new Wire.PlannerSaveReq { Text = _text, Revision = _document.Revision },
                value =>
                {
                    if (Current(generation))
                        Accept(value);
                },
                error => Failed(generation, error)
            );
        }

        void Add()
        {
            try
            {
                _text = TodoText.Add(_text, _quickAdd, _projectFilter, DateTime.Today);
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

        void Run(string action)
        {
            int generation = Begin();
            DaemonClient.Send<Wire.PlannerActionReply>(
                "POST",
                WireProtocol.Routes.PlannerAction,
                new Wire.PlannerActionReq
                {
                    Action = action,
                    Revision = _document.Revision,
                    Line = _selected?.Line ?? 0,
                    Project = _project,
                    Template = _template,
                },
                value =>
                {
                    if (!Current(generation))
                        return;
                    _output = value.Output;
                    _assistant = true;
                    _detailOpen = true;
                    _busy = false;
                    _source = false;
                    _needsRefresh = _refreshAfterExecute = action == "execute";
                },
                error =>
                {
                    if (!Current(generation))
                        return;
                    Failed(generation, error);
                    // Startup can fail after reserving the line or creating a Task.
                    // Reconcile its durable identity before offering Execute again.
                    _needsRefresh = _refreshAfterExecute = action == "execute";
                },
                timeoutMs: 120000
            );
        }

        Wire.PlannerItem Linked(TodoEntry entry)
        {
            var linked = _document?.Items.FirstOrDefault(item =>
                item.Id.Length > 0 ? item.Id == entry.Id : item.Line == entry.Line
            );
            if (linked != null)
            {
                var task = SessionHub.Instance.Tasks.FirstOrDefault(value =>
                    value.Id == linked.TaskId
                );
                if (task != null)
                    linked.Status = TaskInfo.StatusText(task.Status);
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
                        TaskDetailView.Open(TaskInfo.FromWire(value.Task));
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
                if (_refreshAfterExecute && !_busy && !Dirty)
                {
                    _refreshAfterExecute = false;
                    Load(preserveError: true);
                }
                DrawBoard(body.ContractedBy(UiTheme.GapM));
            }
        }
    }
}
