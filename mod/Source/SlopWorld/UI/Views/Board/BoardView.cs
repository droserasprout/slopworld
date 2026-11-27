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
        string _board = "root",
            _error;
        bool _reading,
            _saving,
            _open;
        int _revision;
        float _nextRead;
        Vector2 _last;
        string _prompt,
            _input;
        Action<string> _submit;
        public override string Title => "Board";
        public override PanelSize MinimumSize => new PanelSize(240f, 180f);

        static BoardView Current() =>
            _current != null && _current._endpoint == DaemonClient.BaseUrl
                ? _current
                : (_current = new BoardView());

        public static void Toggle() => WorkspaceWindow.ToggleContent(Current);

        public static void Open() => WorkspaceWindow.OpenContent(Current());

        public override void Opened()
        {
            _open = true;
            WorkspaceLayout.CompactBoardOpen = true;
            _nextRead = 0;
        }

        public override void Closed()
        {
            _open = false;
            WorkspaceLayout.CompactBoardOpen = false;
            _gesture.Cancel();
            _submit = null;
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
            if (_submit != null)
            {
                _submit = null;
                return true;
            }
            if (_editor.Card != null)
            {
                _editor.Select(null);
                return true;
            }
            return false;
        }

        public bool TouchTextRequested { get; private set; }

        Rect TextBox(Rect rect)
        {
            if (
                Focused
                && Event.current.rawType == EventType.MouseDown
                && rect.Contains(Event.current.mousePosition)
            )
                TouchTextRequested = true;
            return rect;
        }

        bool Active => _open && _endpoint == DaemonClient.BaseUrl;

        void Refresh()
        {
            if (_reading || _saving || Time.unscaledTime < _nextRead)
                return;
            _reading = true;
            int revision = _revision;
            DaemonClient.Get<Wire.BoardCatalog>(
                WireProtocol.Routes.Boards,
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
                        _error = error;
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
                    _error = error;
                    _nextRead = 0;
                }
            );
        }

        void Accept(Wire.BoardCatalog value)
        {
            _catalog = value;
            if (!_gesture.Valid(Board) || (_gesture.Dragging && Board.Archived))
            {
                _gesture.Cancel();
                _error = "Move canceled. The card, column, or insertion point changed.";
            }
            if (_editor.Card != null)
            {
                var owner = _editor.Refresh(value);
                if (owner == null)
                    _error = "The open card was removed.";
                else
                    _board = owner.Id;
            }
            if (Board == null)
                PickBoard("root");
        }

        string BoardLabel(Wire.ProjectBoard board)
        {
            string name = board.Name + (board.Archived ? " (archived)" : "");
            return _catalog.Boards.Count(b => b.Name == board.Name) > 1
                ? name + " · " + board.Id
                : name;
        }

        void PickBoard(string id)
        {
            _board = id;
            _viewport.Reset();
            _editor.Select(null);
            _submit = null;
            _gesture.Cancel();
        }

        void Prompt(string label, string initial, Action<string> submit)
        {
            _prompt = label;
            _input = initial;
            _submit = submit;
        }

        public override void Draw(Rect body)
        {
            if (!Active)
            {
                UiText.RowLabel(body, "Reopen Board for the current daemon.");
                return;
            }
            if (Event.current.rawType == EventType.MouseDown)
                TouchTextRequested = false;
            Refresh();
            using (WidgetState.Save())
            {
                body = body.ContractedBy(4);
                if (Board == null)
                {
                    UiText.RowLabel(body, _error ?? "Loading boards…");
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
                var picker = new Rect(body.x, body.y, Mathf.Max(85, body.width - 120), 30);
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
                var action = new Rect(picker.xMax + 4, body.y, body.xMax - picker.xMax - 4, 30);
                if (board.Archived)
                {
                    if (UiControls.Button(action, "Delete archive…", on: !_saving))
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
                else if (UiControls.Button(action, "+ Column", on: !_saving))
                    Prompt(
                        "Column name",
                        "",
                        name =>
                            Save(
                                new Wire.BoardEdit
                                {
                                    Action = "add-column",
                                    Board = board.Id,
                                    Text = name,
                                }
                            )
                    );
                body.yMin += 34;
                if (_submit != null)
                {
                    _input = UiText.FieldWithPlaceholder(
                        TextBox(new Rect(body.x, body.y, body.width - 112, 30)),
                        "board.prompt",
                        _input,
                        _prompt
                    );
                    if (
                        UiControls.Button(
                            new Rect(body.xMax - 108, body.y, 66, 30),
                            "Apply",
                            on: !_saving && _input.Trim().Length > 0
                        )
                    )
                    {
                        var submit = _submit;
                        _submit = null;
                        submit(_input);
                    }
                    if (UiControls.Button(new Rect(body.xMax - 38, body.y, 38, 30), "×"))
                        _submit = null;
                    body.yMin += 34;
                }
                var canvas = body;
                bool narrow = body.width < 650;
                bool detailOpen = _editor.Card != null;
                if (detailOpen && !narrow)
                    canvas.width = body.width - Mathf.Min(380, body.width * .45f) - 6;
                if (!detailOpen || !narrow)
                    DrawColumns(canvas, board, narrow);
                if (detailOpen && _editor.Card != null)
                {
                    var panel = body;
                    if (!narrow)
                        panel.xMin = canvas.xMax + 6;
                    DrawDetails(panel, board);
                }
            }
        }
    }
}
