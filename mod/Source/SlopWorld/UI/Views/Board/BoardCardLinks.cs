using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // Saved links use immutable IDs. Keys are display names and can overlap across boards.
    internal sealed class BoardCardLinks
    {
        Wire.BoardCatalog _source;
        List<(Wire.ProjectBoard Board, Wire.BoardCard Card)> _cards;

        internal IReadOnlyList<(Wire.ProjectBoard Board, Wire.BoardCard Card)> Visible(
            Wire.BoardCatalog catalog
        )
        {
            if (!ReferenceEquals(catalog, _source))
            {
                _source = catalog;
                _cards = catalog
                    .Boards.SelectMany(b => b.Columns.SelectMany(c => c.Cards).Select(c => (b, c)))
                    .ToList();
            }
            return _cards;
        }

        internal (Wire.ProjectBoard Board, Wire.BoardCard Card) Resolve(
            Wire.BoardCatalog catalog,
            Wire.ProjectBoard owner,
            string target
        )
        {
            var cards = Visible(catalog);
            var exact = cards.FirstOrDefault(p => p.Card.Id == target);
            if (exact.Card != null)
                return exact;
            var matches = cards
                .Where(p => string.Equals(p.Card.Key, target, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return matches.Count == 1
                ? matches[0]
                : matches.FirstOrDefault(p => p.Board.Id == owner.Id);
        }
    }
}
