using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public sealed partial class BoardView
    {
        string _search = "",
            _assigneeFilter;
        Wire.ProjectBoard _filterSource,
            _visible;

        static string AssigneeLabel(string name) =>
            name == "" ? "Unassigned"
            : name == WireProtocol.HostIdentity ? "You"
            : name;

        static List<FloatMenuOption> AssigneeOptions(Wire.ProjectBoard board, Action<string> choose)
        {
            var options = new List<FloatMenuOption>();
            var seen = new HashSet<string>();
            void Add(string name, string label)
            {
                if (seen.Add(name))
                    options.Add(new FloatMenuOption(label, () => choose(name)));
            }
            Add("", "Unassigned");
            Add(WireProtocol.HostIdentity, "You");
            foreach (
                var session in SessionHub
                    .Instance.Sessions.OrderBy(s => s.Worker)
                    .ThenBy(s => s.Name)
            )
                Add(session.Name, (session.Worker ? "Worker · " : "Agent · ") + session.Name);
            foreach (
                var name in board
                    .Columns.SelectMany(c => c.Cards)
                    .Select(c => c.Assignee)
                    .Distinct()
                    .OrderBy(n => n)
            )
                Add(name, name);
            return options;
        }

        void DrawFilters(Rect rect, Wire.ProjectBoard board)
        {
            string search = UiText.FieldWithPlaceholder(
                TextBox(rect),
                "board.search",
                _search,
                "Search cards…"
            );
            if (search != _search)
                SetFilters(search, _assigneeFilter);
            rect.x += rect.width + 4;
            UiSelector.DrawInline(
                rect,
                _assigneeFilter == null ? "All assignees" : AssigneeLabel(_assigneeFilter),
                () =>
                {
                    var options = AssigneeOptions(board, value => SetFilters(_search, value));
                    options.Insert(
                        0,
                        new FloatMenuOption("All assignees", () => SetFilters(_search, null))
                    );
                    return options;
                }
            );
        }

        void SetFilters(string search, string assignee)
        {
            _search = search;
            _assigneeFilter = assignee;
            _filterSource = null;
            _adding = null;
            _gesture.Cancel();
            _viewport.Reset();
        }

        Wire.ProjectBoard VisibleBoard(Wire.ProjectBoard source)
        {
            if (source == null || (string.IsNullOrWhiteSpace(_search) && _assigneeFilter == null))
                return source;
            if (ReferenceEquals(source, _filterSource))
                return _visible;
            _filterSource = source;
            _visible = new Wire.ProjectBoard
            {
                Id = source.Id,
                Name = source.Name,
                Archived = source.Archived,
            };
            string query = _search.Trim();
            foreach (var column in source.Columns)
            {
                var visible = new Wire.BoardColumn { Id = column.Id, Name = column.Name };
                visible.Cards.AddRange(
                    column.Cards.Where(card =>
                        (_assigneeFilter == null || card.Assignee == _assigneeFilter)
                        && (
                            card.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                            || card.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                        )
                    )
                );
                _visible.Columns.Add(visible);
            }
            return _visible;
        }
    }
}
