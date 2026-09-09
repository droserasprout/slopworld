using System;
using System.Globalization;

namespace SlopWorld
{
    // A text range keeps the clicked anchor separate from the active end. Start/End are the
    // ordered, half-open form used by TextEditor and by rendered text selection.
    public struct TextSelectionRange
    {
        public int Anchor { get; }
        public int Focus { get; }
        public int Start => Math.Min(Anchor, Focus);
        public int End => Math.Max(Anchor, Focus);
        public bool HasSelection => Anchor != Focus;

        public TextSelectionRange(int anchor, int focus)
        {
            Anchor = anchor;
            Focus = focus;
        }
    }

    // Shared string-index rules for editable fields and laid-out text. Terminal cells keep a
    // separate classifier because a terminal index is a fixed screen column, not a UTF-16
    // string offset.
    public static class TextSelectionRules
    {
        public static TextSelectionRange WordRange(string text, int index)
        {
            if (string.IsNullOrEmpty(text)) return new TextSelectionRange(0, 0);

            int at = CodePointIndex(text, index);
            int anchor = CodePointAt(text, at);
            bool word = IsWordCodePoint(text, at);
            int start = at;
            int end = NextCodePoint(text, at);

            while (start > 0)
            {
                int previous = PreviousCodePoint(text, start);
                if (!SameClass(text, previous, anchor, word)) break;
                start = previous;
            }
            while (end < text.Length && SameClass(text, end, anchor, word))
                end = NextCodePoint(text, end);

            return new TextSelectionRange(start, end);
        }

        public static TextSelectionRange LineRange(string text, int index)
        {
            if (string.IsNullOrEmpty(text)) return new TextSelectionRange(0, 0);

            int at = Math.Max(0, Math.Min(index, text.Length - 1));
            int start = at == 0 ? 0 : text.LastIndexOf('\n', at - 1) + 1;
            int newline = text.IndexOf('\n', at);
            int end = newline < 0 ? text.Length : newline + 1;
            return new TextSelectionRange(start, end);
        }

        // A word selection starts with the clicked word already selected. Extending to the
        // right must keep that whole word even when the pointer is still in a shorter adjacent
        // run; extending left keeps the original word as the opposite endpoint.
        public static TextSelectionRange ExpandWordSelection(TextSelectionRange original,
                                                              TextSelectionRange destination)
        {
            if (destination.Start < original.Start)
                return new TextSelectionRange(original.End, destination.Start);
            if (destination.Start > original.Start)
            {
                int end = Math.Max(original.End, destination.End);
                return new TextSelectionRange(original.Start, end);
            }
            return original;
        }

        public static int CodePointIndex(string text, int index)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            int at = Math.Max(0, Math.Min(index, text.Length - 1));
            if (at > 0 && char.IsLowSurrogate(text[at]) &&
                char.IsHighSurrogate(text[at - 1]))
                at--;
            return at;
        }

        static int CodePointAt(string text, int index) => char.ConvertToUtf32(text, index);

        static int NextCodePoint(string text, int index)
        {
            int next = index + 1;
            if (next < text.Length && char.IsHighSurrogate(text[index]) &&
                char.IsLowSurrogate(text[next]))
                return next + 1;
            return next;
        }

        static int PreviousCodePoint(string text, int index)
        {
            int previous = index - 1;
            if (previous > 0 && char.IsLowSurrogate(text[previous]) &&
                char.IsHighSurrogate(text[previous - 1]))
                previous--;
            return previous;
        }

        static bool SameClass(string text, int index, int anchor, bool word) =>
            word ? IsWordCodePoint(text, index) : CodePointAt(text, index) == anchor;

        static bool IsWordCodePoint(string text, int index)
        {
            if (text[index] == '_') return true;
            switch (CharUnicodeInfo.GetUnicodeCategory(text, index))
            {
                case UnicodeCategory.UppercaseLetter:
                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.TitlecaseLetter:
                case UnicodeCategory.ModifierLetter:
                case UnicodeCategory.OtherLetter:
                case UnicodeCategory.DecimalDigitNumber:
                case UnicodeCategory.LetterNumber:
                case UnicodeCategory.OtherNumber:
                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.SpacingCombiningMark:
                case UnicodeCategory.EnclosingMark:
                    return true;
                default:
                    return false;
            }
        }
    }
}
