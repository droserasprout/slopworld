using System;

namespace SlopWorld
{
    // Click-time recognition only. Paths do not join URL autolinking because repaint and
    // pointer motion are hot paths; one row scan after Ctrl+MouseDown is both cheaper and
    // less eager about ordinary terminal text that happens to contain a slash.
    public static class RelativePathScan
    {
        public static string At(string text, int column)
        {
            if (string.IsNullOrEmpty(text) || column < 0 || column >= text.Length)
                return null;

            int start = column, end = column + 1;
            while (start > 0 && !Boundary(text[start - 1])) start--;
            while (end < text.Length && !Boundary(text[end])) end++;
            while (start < end && IsWrapper(text[start])) start++;
            while (end > start && IsTail(text[end - 1])) end--;

            // Compiler locations commonly append :line or :line:column.
            int location = end;
            while (location > start)
            {
                int colon = text.LastIndexOf(':', location - 1, location - start);
                if (colon < start || !Digits(text, colon + 1, location)) break;
                location = colon;
            }
            if (location < end) end = location;

            if (end <= start) return null;
            string path = text.Substring(start, end - start);
            if (path.StartsWith("/", StringComparison.Ordinal) ||
                path.IndexOf("://", StringComparison.Ordinal) >= 0)
                return null;
            if (!path.StartsWith("./", StringComparison.Ordinal) &&
                !path.StartsWith("../", StringComparison.Ordinal) &&
                path.IndexOf('/') < 0)
                return null;
            return path;
        }

        static bool Boundary(char c) => char.IsWhiteSpace(c) || c == '"' || c == '`' ||
            c == '<' || c == '>' || c == '|' || c == '=';

        static bool IsWrapper(char c) => c == '(' || c == '[' || c == '{' || c == '\'';

        static bool IsTail(char c) => c == ')' || c == ']' || c == '}' || c == '\'' ||
            c == ',' || c == ';' || c == '!' || c == '?';

        static bool Digits(string text, int start, int end)
        {
            if (start >= end) return false;
            for (int i = start; i < end; i++)
                if (text[i] < '0' || text[i] > '9') return false;
            return true;
        }
    }
}
