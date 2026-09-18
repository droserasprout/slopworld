using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // The link-detection half of Sgr, carved out because it is pure string work with no
    // Unity or game types: scheme detection, trailing-punctuation trimming, and OSC 8
    // parsing. Kept here so it can be unit-tested without a live terminal.
    public static class UrlScan
    {
        public struct Span
        {
            public int Start, End;
            public string Url;

            public Span(int start, int end, string url)
            {
                Start = start;
                End = end;
                Url = url;
            }
        }

        // The empty URI is how OSC 8 closes a link, so "no link from here" and "this OSC
        // was about something else" have to be different answers: the first is an empty
        // string, the second null, which leaves the caller's link standing.
        public static string Osc(string body)
        {
            if (body == null || !body.StartsWith("8;")) return null;
            var parts = body.Split(new[] { ';' }, 3);
            return parts.Length < 3 ? "" : parts[2];
        }

        public static List<Span> FindUrls(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf("://", StringComparison.Ordinal) < 0)
                return null;

            List<Span> spans = null;
            int at = 0;
            while (at < text.Length)
            {
                int sep = text.IndexOf("://", at, StringComparison.Ordinal);
                if (sep < 0) break;

                int start = sep;
                while (start > 0 && IsScheme(text[start - 1])) start--;
                int schemeLength = sep - start;
                bool web = (schemeLength == 4 && string.Compare(text, start, "http", 0, 4,
                                StringComparison.OrdinalIgnoreCase) == 0) ||
                           (schemeLength == 5 && string.Compare(text, start, "https", 0, 5,
                                StringComparison.OrdinalIgnoreCase) == 0);
                if (!web) { at = sep + 3; continue; }

                int end = sep + 3;
                while (end < text.Length && IsUrl(text[end])) end++;
                end = TrimTail(text, sep + 3, end);

                // A bare "https://" token is text because a URL requires content after the scheme.
                if (end > sep + 3)
                {
                    if (spans == null) spans = new List<Span>();
                    spans.Add(new Span(start, end, text.Substring(start, end - start)));
                }
                at = Math.Max(end, sep + 3);
            }

            return spans;
        }

        static bool IsScheme(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        // The printable ASCII a URL is allowed to be made of, less the brackets and
        // quotes text wraps them in.
        static bool IsUrl(char c)
        {
            if (c <= ' ' || c > '~') return false;
            switch (c)
            {
                case '<':
                case '>':
                case '"':
                case '`':
                case '{':
                case '}':
                case '|':
                case '\\':
                case '^':
                    return false;
                default:
                    return true;
            }
        }

        // A URL at the end of a sentence takes the full stop with it, and one in
        // parentheses takes the closing bracket. Both are the prose's, not the link's -
        // unless the link opened a bracket of its own, which is how a wiki URL reads.
        static int TrimTail(string text, int from, int end)
        {
            bool counted = false;
            int parens = 0, brackets = 0;
            while (end > from)
            {
                char c = text[end - 1];
                if (c == '.' || c == ',' || c == ';' || c == ':' || c == '!' ||
                    c == '?' || c == '\'' || c == '*' || c == '_')
                {
                    end--;
                    continue;
                }
                if (c == ')' || c == ']')
                {
                    // Count once, then adjust for removed closers. Recounting each suffix
                    // makes long runs of unmatched brackets quadratic in screen length.
                    if (!counted)
                    {
                        for (int i = from; i < end; i++)
                        {
                            switch (text[i])
                            {
                                case '(': parens++; break;
                                case ')': parens--; break;
                                case '[': brackets++; break;
                                case ']': brackets--; break;
                            }
                        }
                        counted = true;
                    }
                    if (c == ')' && parens < 0) { parens++; end--; continue; }
                    if (c == ']' && brackets < 0) { brackets++; end--; continue; }
                }
                break;
            }
            return end;
        }
    }
}
