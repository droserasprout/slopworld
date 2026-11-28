using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public sealed partial class BoardView : ContentView
    {
        static BoardView _current;

        // SlopWeb supplies device identity for its mouse-style native GUI events.
        public static bool TouchPointer { get; set; }

        public static void CancelPointer() => _current?._gesture.Cancel();

        readonly string _endpoint = DaemonClient.BaseUrl;
        readonly BoardGesture _gesture = new BoardGesture();
        readonly ScrollableListing _details = new ScrollableListing();
        Wire.BoardCatalog _catalog;
        Wire.ProjectBoard Board => _catalog?.Boards.FirstOrDefault(b => b.Id == _board);
        readonly BoardSelection _editor = new BoardSelection();
        string _board,
            _error;
        bool _reading,
            _saving,
            _open;
        int _revision;
        float _nextRead;
        Vector2 _last;
        Wire.BoardEdit _inlineEdit;
        bool _focusInline;
        public override string Title => "Board";
        public override PanelSize MinimumSize => new PanelSize(240f, 180f);

        static BoardView Current() =>
            _current != null && _current._endpoint == DaemonClient.BaseUrl
                ? _current
                : (_current = new BoardView());

        public static void Toggle() => WorkspaceWindow.ToggleContent(Current);

        public static void Open() => WorkspaceWindow.OpenContent(Current());

        IDisposable _taskDemand;

        public override void Opened()
        {
            _taskDemand ??= SessionHub.Instance.TaskStore.OpenReader();
            _open = true;
            WorkspaceLayout.CompactBoardOpen = true;
            _nextRead = 0;
        }

        public override void Closed()
        {
            _taskDemand?.Dispose();
            _taskDemand = null;
            _open = false;
            _cardWindow?.Close(false);
            WorkspaceLayout.CompactBoardOpen = false;
            _gesture.Cancel();
            _inlineEdit = null;
        }

        public override void FocusChanged(bool focused)
        {
            base.FocusChanged(focused);
            if (!focused)
                _gesture.Cancel();
        }

        internal bool Back()
        {
            if (_gesture.Held)
            {
                _gesture.Cancel();
                return true;
            }
            if (_inlineEdit != null)
            {
                _inlineEdit = null;
                return true;
            }
            if (_editor.Card != null)
            {
                _cardWindow?.Close(false);
                _editor.Select(null);
                return true;
            }
            return false;
        }

        public bool TouchTextRequested { get; private set; }

        Rect TextBox(Rect rect)
        {
            if (
                (_cardWindow != null ? Find.WindowStack.GetsInput(_cardWindow) : Focused)
                && Event.current.rawType == EventType.MouseDown
                && rect.Contains(Event.current.mousePosition)
            )
                TouchTextRequested = true;
            return rect;
        }

        static bool SubmitField(string name)
        {
            var e = Event.current;
            if (
                e.type != EventType.KeyDown
                || (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter)
                || GUI.GetNameOfFocusedControl() != name
            )
                return false;
            e.Use();
            return true;
        }

        bool CanWrite => _catalog?.CanWrite == true;
        bool CanAdmin => _catalog?.CanAdmin == true;

        static string ProjectName(Wire.ProjectBoard board) =>
            SessionHub.Instance.Projects.FirstOrDefault(p => p.Id == board.Id)?.Name;

        bool Writable(Wire.ProjectBoard board) => CanWrite && !board.Archived;

        bool Active => _open && _endpoint == DaemonClient.BaseUrl;

        void Refresh(string taskId = null)
        {
            if (_reading || _saving || Time.unscaledTime < _nextRead)
                return;
            _reading = true;
            int revision = _revision;
            DaemonClient.Get<Wire.BoardCatalog>(
                WireProtocol.Routes.Boards
                    + (taskId == null ? "" : "?task=" + Uri.EscapeDataString(taskId)),
                value =>
                {
                    _reading = false;
                    _nextRead = Time.unscaledTime + 1.5f;
                    if (revision == _revision)
                        Accept(value);
                },
                error =>
                {
                    _reading = false;
                    _nextRead = Time.unscaledTime + 3f;
                    if (revision == _revision)
                    {
                        _catalog = null;
                        _gesture.Cancel();
                        _inlineEdit = null;
                        _error = error;
                    }
                }
            );
        }

        void Save(Wire.BoardEdit edit, Action done = null)
        {
            if (!Active || _saving)
                return;
            _saving = true;
            _revision++;
            _error = null;
            DaemonClient.Post<Wire.BoardCatalog>(
                WireProtocol.Routes.Boards,
                edit,
                value =>
                {
                    _saving = false;
                    Accept(value);
                    done?.Invoke();
                },
                error =>
                {
                    _saving = false;
                    _error =
                        edit.Action == "link" && edit.Kind == "task"
                            ? $"Could not attach task {edit.Text}. {error}"
                            : error;
                    _nextRead = 0;
                }
            );
        }

        void Accept(Wire.BoardCatalog value)
        {
            if (!value.CanWrite || (_catalog?.CanAdmin == true && !value.CanAdmin))
                _inlineEdit = null;
            if (!value.CanAdmin)
                _editor.EditingComment = 0;
            _catalog = value;
            if (!_gesture.Valid(VisibleBoard(Board)) || (_gesture.Dragging && !Writable(Board)))
            {
                _gesture.Cancel();
                _error = "Move canceled. The card, column, or insertion point changed.";
            }
            if (_pendingTaskCard.HasValue)
            {
                var link = _pendingTaskCard.Value;
                _pendingTaskCard = null;
                if (!_editor.SelectLinkedTaskCard(Board, link.Card, link.Task))
                    _error = "The linked card is no longer available.";
            }
            if (_editor.Card != null)
            {
                var owner = _editor.Refresh(value);
                if (owner == null)
                    _error = "The open card was removed.";
                else if (_board != owner.Id)
                {
                    _board = owner.Id;
                    SetFilters("", null);
                }
            }
            if (Board == null)
                PickBoard(value.Boards.FirstOrDefault()?.Id);
        }

        string BoardLabel(Wire.ProjectBoard board)
        {
            string name = board.Name + (board.Archived ? " (archived)" : "");
            return _catalog.Boards.Count(b => b.Name == board.Name) > 1
                ? name + " · " + board.Id
                : name;
        }

        (string Card, string Task)? _pendingTaskCard;

        internal static void OpenLinkedCard(string boardId, string cardId, string taskId)
        {
            var view = Current();
            if (view._board != boardId)
                view.PickBoard(boardId);
            view._pendingTaskCard = (cardId, taskId);
            Open();
        }

        void PickBoard(string id)
        {
            _board = id;
            SetFilters("", null);
            _editor.Select(null);
            _inlineEdit = null;
        }

        void BeginInline(string action, string text = "", string column = "")
        {
            _inlineEdit = new Wire.BoardEdit
            {
                Board = _board,
                Action = action,
                Column = column,
                Text = text,
            };
            _focusInline = true;
        }

        bool DrawInlineField(Rect rect, string action, string placeholder, string column = "")
        {
            var edit = _inlineEdit;
            if (
                edit == null
                || edit.Action != action
                || edit.Column != column
                || edit.Board != _board
            )
                return false;
            string name = "board.inline." + action + "." + column;
            var e = Event.current;
            if (
                e.type == EventType.KeyDown
                && GUI.GetNameOfFocusedControl() == name
                && e.keyCode == KeyCode.Escape
            )
            {
                e.Use();
                _inlineEdit = null;
                return true;
            }
            if (SubmitField(name))
            {
                _inlineEdit = null;
                if (edit.Text.Trim().Length > 0)
                    Save(edit);
                return true;
            }
            edit.Text = UiText.FieldWithPlaceholder(TextBox(rect), name, edit.Text, placeholder);
            if (_focusInline && e.type == EventType.Repaint)
            {
                GUI.FocusControl(name);
                _focusInline = false;
            }
            return true;
        }

        public override void Draw(Rect body)
        {
            if (!Active || Board == null || _editor.Card == null)
                _cardWindow?.Close(false);
            else if (_cardWindow == null)
                Find.WindowStack.Add(_cardWindow = new CardWindow(this));
            if (!Active)
            {
                UiText.RowLabel(body, "Reopen Board for the current daemon.");
                return;
            }
            if (_cardWindow == null && Event.current.rawType == EventType.MouseDown)
                TouchTextRequested = false;
            Refresh();
            using (WidgetState.Save())
            {
                body = body.ContractedBy(4);
                if (Board == null)
                {
                    UiText.RowLabel(
                        body,
                        _error
                            ?? (
                                _catalog == null
                                    ? "Loading boards…"
                                    : "No visible boards. Enable a board in Project Edit → Boards."
                            )
                    );
                    return;
                }
                if (_error != null)
                {
                    if (
                        UiControls.Button(new Rect(body.x, body.y, body.width, 28), _error + "   ×")
                    )
                        _error = null;
                    body.yMin += 30;
                }
                var board = Board;
                bool stacked = body.width < 560;
                const float gap = 4;
                float actionWidth = board.Archived ? 116 : 85;
                bool showPrefix = CanAdmin && !board.Archived;
                const float prefixWidth = 96;
                bool separateActions = stacked && showPrefix;
                float actionsWidth = actionWidth + (showPrefix ? gap + prefixWidth : 0);
                float pickerWidth =
                    separateActions ? body.width
                    : stacked ? body.width - actionWidth - gap
                    : (body.width - actionsWidth - 3 * gap) / 3;
                var picker = new Rect(body.x, body.y, pickerWidth, 30);
                float toolbarHeight =
                    separateActions ? 102
                    : stacked ? 68
                    : 34;
                UiSelector.DrawInline(
                    picker,
                    BoardLabel(board),
                    () =>
                        _catalog
                            .Boards.Select(b => new FloatMenuOption(
                                BoardLabel(b),
                                () => PickBoard(b.Id)
                            ))
                            .ToList()
                );
                float filterWidth = stacked ? (body.width - gap) / 2 : pickerWidth;
                var filter = new Rect(
                    stacked ? body.x : picker.xMax + gap,
                    stacked ? body.y + 34 : body.y,
                    filterWidth,
                    30
                );
                DrawFilters(filter, board);
                float actionY = separateActions ? body.y + 68 : body.y;
                var action = new Rect(body.xMax - actionWidth, actionY, actionWidth, 30);
                if (showPrefix)
                {
                    var prefix = new Rect(action.x - gap - prefixWidth, actionY, prefixWidth, 30);
                    if (!DrawInlineField(prefix, "set-prefix", "Key prefix"))
                    {
                        UiText.RowLabel(prefix, board.Prefix, TextAnchor.MiddleCenter);
                        TooltipHandler.TipRegion(prefix, "Edit card key prefix");
                        if (!_saving && Widgets.ButtonInvisible(prefix))
                            BeginInline("set-prefix", board.Prefix);
                    }
                }
                if (board.Archived)
                {
                    if (CanAdmin && UiControls.Button(action, "Delete archive…", on: !_saving))
                        Find.WindowStack.Add(
                            ConfirmDialog.Create(
                                $"Permanently delete {BoardLabel(board)} and its {board.Columns.Sum(c => c.Cards.Count)} remaining cards?",
                                () =>
                                    Save(
                                        new Wire.BoardEdit
                                        {
                                            Action = "delete-archive",
                                            Board = board.Id,
                                            Confirm = true,
                                        }
                                    ),
                                destructive: true
                            )
                        );
                }
                else if (
                    CanWrite
                    && !DrawInlineField(action, "add-column", "Column name")
                    && UiControls.Button(action, "+ Column", on: !_saving)
                )
                    BeginInline("add-column");
                body.yMin += toolbarHeight;
                DrawColumns(body, VisibleBoard(board), body.width < 650);
            }
        }
    }
}
