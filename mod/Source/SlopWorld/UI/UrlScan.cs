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
                string scheme = text.Substring(start, sep - start).ToLowerInvariant();
                if (scheme != "http" && scheme != "https") { at = sep + 3; continue; }

                int end = sep + 3;
                while (end < text.Length && IsUrl(text[end])) end++;
                end = TrimTail(text, sep + 3, end);

                // "https://" and nothing after it is not a link, it is the word.
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
                    char open = c == ')' ? '(' : '[';
                    int depth = 0;
                    for (int i = from; i < end; i++)
                    {
                        if (text[i] == open) depth++;
                        else if (text[i] == c) depth--;
                    }
                    if (depth < 0) { end--; continue; }
                }
                break;
            }
            return end;
        }
    }
}
