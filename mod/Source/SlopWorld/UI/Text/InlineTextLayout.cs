using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SlopWorld
{
    // Measurement, truncation and painting consume these same positions. Atlas availability
    // never changes geometry: missing artwork is painted as a replacement in its allotted box.
    public sealed class InlineTextLayout
    {
        public readonly struct Span
        {
            public readonly string Text;
            public readonly int Sprite;
            public readonly float X, Width;
            public Span(string text, int sprite, float x, float width)
            { Text = text; Sprite = sprite; X = x; Width = width; }
        }

        public readonly Span[] Spans;
        public readonly float Width;
        InlineTextLayout(List<Span> spans, float width) { Spans = spans.ToArray(); Width = width; }
        InlineTextLayout(Span[] spans, float width) { Spans = spans; Width = width; }

        readonly struct Token
        {
            public readonly string Text;
            public readonly int Sprite;
            public Token(string text, int sprite) { Text = text; Sprite = sprite; }
        }

        static List<Token> Tokens(string text, TextSpriteCatalog catalog)
        {
            var tokens = new List<Token>();
            for (int i = 0; i < text.Length;)
            {
                if (catalog.Match(text, i, out int length, out int slot))
                {
                    tokens.Add(new Token(text.Substring(i, length), slot));
                    i += length;
                }
                else
                {
                    string element = StringInfo.GetNextTextElement(text, i);
                    tokens.Add(new Token(element, -1));
                    i += element.Length;
                }
            }
            return tokens;
        }

        public static InlineTextLayout Proportional(string text, TextSpriteCatalog catalog,
            float spriteSize, Func<string, float> measure, float maxWidth = float.PositiveInfinity)
        {
            text = text ?? "";
            if (!catalog.Contains(text))
            {
                float width = measure(text);
                if (width <= maxWidth)
                    return new InlineTextLayout(new List<Span> { new Span(text, -1, 0f, width) }, width);
            }
            var tokens = Tokens(text, catalog);
            var full = Build(tokens, tokens.Count, spriteSize, measure, false);
            if (full.Width <= maxWidth) return full;
            if (measure("…") > maxWidth) return new InlineTextLayout(new List<Span>(), 0f);

            // Search only whole text elements / catalog keys, never UTF-16 offsets.
            int low = 0, high = tokens.Count;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (Build(tokens, mid, spriteSize, measure, true).Width <= maxWidth) low = mid;
                else high = mid - 1;
            }
            return Build(tokens, low, spriteSize, measure, true);
        }

        static InlineTextLayout Build(List<Token> tokens, int count, float spriteSize,
                                Func<string, float> measure, bool ellipsis)
        {
            var spans = new List<Span>();
            var plain = new StringBuilder();
            float x = 0f;
            Action flush = () =>
            {
                if (plain.Length == 0) return;
                string text = plain.ToString();
                float width = measure(text);
                spans.Add(new Span(text, -1, x, width));
                x += width;
                plain.Clear();
            };
            for (int i = 0; i < count; i++)
            {
                var token = tokens[i];
                if (token.Sprite < 0) { plain.Append(token.Text); continue; }
                flush();
                spans.Add(new Span(token.Text, token.Sprite, x, spriteSize));
                x += spriteSize;
            }
            if (ellipsis) plain.Append('…');
            flush();
            return new InlineTextLayout(spans, x);
        }

        // SgrRun guarantees only its final scalar can have a continuation cell.
        // A sprite sequence consumes the exact scalar cells supplied by the daemon.
        public static InlineTextLayout Cells(string text, int columns, float cellWidth,
                                      TextSpriteCatalog catalog, Func<char, bool> fitsCell,
                                      bool groupPlain = true)
        {
            text = text ?? "";
            // Full terminal paints revisit many ordinary rows. A printable ASCII
            // row cannot contain a keycap's combining mark. Keep it as one font
            // span without allocating tokens or probing the sprite trie.
            if (groupPlain && text.Length > 0 && columns == text.Length &&
                !catalog.HasPrintableAsciiOnlyKey)
            {
                int i = 0;
                while (i < text.Length && text[i] >= ' ' && text[i] <= '~' &&
                       fitsCell(text[i])) i++;
                if (i == text.Length)
                    return new InlineTextLayout(new[] {
                        new Span(text, -1, 0f, text.Length * cellWidth)
                    }, columns * cellWidth);
            }
            var spans = new List<Span>();
            int col = 0, plainStart = 0, plainCol = 0;
            for (int i = 0; i < text.Length;)
            {
                bool sprite = catalog.Match(text, i, out int length, out int slot);
                if (!sprite) length = TerminalColumns.ScalarUnits(text, i);
                int count = sprite ? TerminalColumns.ScalarCount(text.Substring(i, length)) : 1;
                // Only the final scalar may own a continuation cell. Give its token the
                // remaining daemon columns rather than guessing width from Unicode or artwork.
                int width = i + length == text.Length ? columns - col : count;
                if (groupPlain && !sprite && length == 1 && width == 1 && fitsCell(text[i]))
                { i++; col++; continue; }

                if (i > plainStart)
                    spans.Add(new Span(text.Substring(plainStart, i - plainStart), -1,
                        plainCol * cellWidth, (col - plainCol) * cellWidth));
                spans.Add(new Span(text.Substring(i, length), slot, col * cellWidth, width * cellWidth));
                i += length;
                col += width;
                plainStart = i;
                plainCol = col;
            }
            if (plainStart < text.Length)
                spans.Add(new Span(text.Substring(plainStart), -1, plainCol * cellWidth,
                    (col - plainCol) * cellWidth));
            return new InlineTextLayout(spans, columns * cellWidth);
        }

        // The daemon can supply several Unicode scalars for one terminal cell.
        // Its width is authoritative even when no sprite covers the complete text.
        public static InlineTextLayout CellCluster(string text, int columns, float cellWidth,
                                                   TextSpriteCatalog catalog)
        {
            text = text ?? "";
            int sprite = catalog.Match(text, 0, out int length, out int slot) &&
                         length == text.Length ? slot : -1;
            float width = columns * cellWidth;
            return new InlineTextLayout(new[] { new Span(text, sprite, 0f, width) }, width);
        }
    }
}
