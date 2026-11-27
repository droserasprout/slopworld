using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public sealed partial class BoardView
    {
        Wire.BoardCatalog _linkedCatalog;
        List<(Wire.ProjectBoard Board, Wire.BoardCard Card)> _linkedCards;

        // Each fullscreen reader owns a board editor so polling and writes follow the
        // same permission, revision and draft rules as the embedded card feed.
        internal void OpenTaskFeed(BoardView origin)
        {
            _open = true;
            _nextRead = 0;
            if (origin != null)
            {
                _board = origin._board;
                _editor.Select(origin._editor.Card);
                _catalog = origin._catalog;
                _filter = origin._filter;
            }
        }

        internal void CloseTaskFeed() => _open = false;

        internal void DrawTaskFeed(Rect area, TaskInfo task)
        {
            if (!Active)
                return;
            Refresh(task.Id);
            if (!ReferenceEquals(_catalog, _linkedCatalog))
            {
                _linkedCatalog = _catalog;
                _linkedCards = _catalog
                    ?.Boards.SelectMany(b =>
                        b.Columns.SelectMany(c => c.Cards)
                            .Where(c =>
                                c.Links.Any(link => link.Kind == "task" && link.Target == task.Id)
                            )
                            .Select(c => (Board: b, Card: c))
                    )
                    .ToList();
            }
            var cards = _linkedCards;
            var selected = cards?.FirstOrDefault(p => p.Card.Id == _editor.Card?.Id) ?? default;
            if (selected.Card == null && cards?.Count > 0)
                selected = cards[0];
            if (selected.Card != null)
            {
                _board = selected.Board.Id;
                _editor.Select(selected.Card);
                var row = new Rect(area.x, area.y, area.width, 28);
                var picker = row;
                picker.xMax -= 90;
                string Label(Wire.BoardCard c) => c.Key + " · " + c.Title;
                if (cards.Count > 1)
                    UiSelector.DrawInline(
                        picker,
                        Label(selected.Card),
                        () =>
                            cards
                                .Select(p => new FloatMenuOption(
                                    Label(p.Card),
                                    () => SelectCard(p.Card)
                                ))
                                .ToList()
                    );
                else
                    UiText.RowLabel(picker, Label(selected.Card));
                if (UiControls.Button(new Rect(picker.xMax + 4, row.y, 86, 28), "Open card"))
                    OpenLinkedCard(_board, selected.Card.Id, task.Id);
                area.yMin += 32;
            }
            if (_error != null)
            {
                UiText.RowLabel(new Rect(area.x, area.y, area.width, 26), _error);
                area.yMin += 28;
            }
            DrawDiscussion(
                area,
                selected.Board,
                selected.Card,
                selected.Board != null && Writable(selected.Board) && !_saving,
                selected.Card == null ? task : null
            );
        }
    }
}
