using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Owns Planner presentation and the selected row editor. Endpoint documents,
    // persistent drafts, and request settlement belong to PlannerDocumentState.
    public sealed partial class PlannerView : ContentView
    {
        static readonly Dictionary<string, PlannerView> Views =
            new Dictionary<string, PlannerView>();
        readonly PlannerDocumentState _state;
        FieldLifetime _fields = new FieldLifetime();
        IDisposable _pollingDemand;
        TodoEntry _selected;
        TodoDraft _draft;
        string _quickAdd = "",
            _search = "",
            _projectFilter = "",
            _contextFilter = "";
        TodoFilter _filter = TodoFilter.Active;
        TodoSort _sort = TodoSort.Due;
        bool _open,
            _source,
            _detailOpen;
        int _presentation;
        readonly SmoothScroll _listScroll = new SmoothScroll();
        readonly ScrollableListing _navigation = new ScrollableListing();
        readonly ScrollableListing _details = new ScrollableListing();
        bool DraftDirty => _selected != null && _draft != null && _draft.Changed(_selected);
        bool Dirty => _state.Dirty || (_state.Document != null && DraftDirty);
        bool CanSave => Dirty || _state.NeedsIds;
        bool Ready => _state.Ready;

        bool IsOpenEndpoint => _open && _state.Endpoint == DaemonClient.BaseUrl;

        public override string Title => "Planner";
        public override PanelSize MinimumSize => new PanelSize(580f, 420f);

        PlannerView(string endpoint)
        {
            _state = new PlannerDocumentState(endpoint);
            _state.Rebuilt += () => RestoreSelection();
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
            if (_state.Document == null && !_state.Busy)
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

        void Rebuild(string key = null)
        {
            _state.Parse();
            RestoreSelection(key);
        }

        void RestoreSelection(string key = null)
        {
            key = key ?? _selected?.Key;
            uint? line = _selected?.Line;
            var selected =
                _state.Entries.FirstOrDefault(entry => entry.Key == key)
                ?? _state.Entries.FirstOrDefault(entry => entry.Line == line);
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
            int generation = _state.Generation,
                presentation = _presentation;
            return () =>
            {
                if (
                    IsOpenEndpoint
                    && Ready
                    && _state.Current(generation)
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
                _state.Error = "Save or discard the task you're editing first.";
                return;
            }
            SetSelection(entry);
            _state.Error = null;
            _detailOpen = true;
        }

        bool ApplyDraft()
        {
            if (!DraftDirty)
                return true;
            try
            {
                _state.Text = TodoText.Update(_state.Text, _selected, _draft, Settings.Today);
                Rebuild();
                return true;
            }
            catch (Exception error)
            {
                _state.Error = error.Message;
                return false;
            }
        }

        void Load() => _state.Load(() => DraftDirty);

        void Save()
        {
            if (IsOpenEndpoint && Ready && ApplyDraft())
                _state.Save();
        }

        void Discard()
        {
            if (!Ready || !IsOpenEndpoint)
                return;
            _state.Discard();
        }

        void Add()
        {
            try
            {
                _state.Text = TodoText.Add(
                    _state.Text,
                    _quickAdd,
                    _projectFilter,
                    Settings.Today,
                    _contextFilter
                );
                Rebuild();
                Select(_state.Entries.Last());
                _quickAdd = "";
                Save();
            }
            catch (Exception error)
            {
                _state.Error = error.Message;
            }
        }

        void Toggle(TodoEntry entry)
        {
            if (DraftDirty)
            {
                _state.Error = "Save or discard the task you're editing first.";
                return;
            }
            try
            {
                var draft = new TodoDraft(entry) { Completed = !entry.Completed };
                _state.Text = TodoText.Update(_state.Text, entry, draft, Settings.Today);
                Rebuild();
                Save();
            }
            catch (Exception error)
            {
                _state.Error = error.Message;
            }
        }

        void DeleteSelected()
        {
            try
            {
                _state.Text = TodoText.Replace(_state.Text, _selected, "");
                SetSelection(null);
                Rebuild();
                _detailOpen = false;
                Save();
            }
            catch (Exception error)
            {
                _state.Error = error.Message;
            }
        }

        Wire.PlannerItem Linked(TodoEntry entry)
        {
            if (entry == null)
                return null;
            var linked = _state.Document?.Items.FirstOrDefault(item =>
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
            _state.LoadTask(
                id,
                task =>
                {
                    if (IsOpenEndpoint && presentation == _presentation)
                    {
                        // Seed the shared board before opening a link that arrived
                        // ahead of its next poll.
                        var info = TaskInfo.FromWire(task);
                        SessionHub.Instance.TaskStore.Add(info);
                        TaskDetailView.Open(info, this);
                    }
                }
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
                if (_state.Endpoint != DaemonClient.BaseUrl)
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
