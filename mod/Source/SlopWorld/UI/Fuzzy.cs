using System;
using System.Collections.Generic;
using System.Text;

namespace SlopWorld
{
    // Palette matching splits terms, accepts order-independent subsequences, and scores head,
    // word-boundary, and consecutive matches above mid-word hits. Match positions drive markup.
    static class Fuzzy
    {
        // Per matched character.
        const int Base = 16;        // anywhere
        const int Head = 34;        // first character of the text
        const int Boundary = 28;    // after a separator, or a camelCase hump
        const int Consecutive = 20; // directly after the previous match

        // Per term.
        const int Verbatim = 60;    // the term is in there whole
        const int AtWord = 40;      // ...and starts a word
        const int LeadMax = 12;     // most a late first character can cost

        // The color matched characters are marked up in. Bright enough to read against
        // both the selected row's fill and the plain one.
        const string Mark = "#7FC8FF";

        /// <summary>
        /// Returns false if any query term is missing; otherwise fills sorted, deduplicated matched positions.
        /// </summary>
        public static bool Match(string text, string query, out int score, out List<int> hits)
        {
            score = 0;
            hits = null;
            if (string.IsNullOrEmpty(text)) return false;

            var terms = query.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0) return true;

            string lower = text.ToLowerInvariant();
            hits = new List<int>();

            foreach (var raw in terms)
            {
                string term = raw.ToLowerInvariant();
                int s = Term(text, lower, term, hits);
                if (s == int.MinValue) { hits = null; return false; }
                score += s;
            }

            // A shorter name matching the same query is the more likely answer.
            score -= text.Length / 8;

            hits.Sort();
            for (int i = hits.Count - 1; i > 0; i--)
                if (hits[i] == hits[i - 1]) hits.RemoveAt(i);

            return true;
        }

        /// <summary>Score with the positions thrown away.</summary>
        public static bool Match(string text, string query, out int score)
        {
            List<int> _;
            return Match(text, query, out score, out _);
        }

        /// <summary>
        /// Wraps hit positions in color markup; null or empty hits return the text unchanged.
        /// </summary>
        public static string Highlight(string text, List<int> hits)
        {
            if (hits == null || hits.Count == 0) return text;

            var sb = new StringBuilder(text.Length + hits.Count * 8);
            int h = 0;
            for (int i = 0; i < text.Length; i++)
            {
                bool on = h < hits.Count && hits[h] == i;
                if (on)
                {
                    // Run the tag over neighbours rather than per character.
                    int end = i;
                    while (h < hits.Count && hits[h] == end) { h++; end++; }
                    sb.Append("<color=").Append(Mark).Append('>');
                    sb.Append(text, i, end - i);
                    sb.Append("</color>");
                    i = end - 1;
                }
                else
                {
                    sb.Append(text[i]);
                }
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- one term

        // Best subsequence placement of `term` in `text`, by dynamic programming over
        // (term index, text index): exact under this scoring, and the strings here are
        // short enough that being exact is free. Matched positions are appended to
        // `hits`. int.MinValue means the term is not in there at all.
        static int Term(string text, string lower, string term, List<int> hits)
        {
            int n = lower.Length, m = term.Length;
            if (m == 0) return 0;
            if (m > n) return int.MinValue;

            var best = new int[m, n];   // score with term[i] placed at text[j]
            var from = new int[m, n];   // where term[i-1] sat, for the walk back
            var prevMax = new int[n];   // running max of row i-1, up to and including j
            var prevArg = new int[n];
            var curMax = new int[n];
            var curArg = new int[n];

            for (int i = 0; i < m; i++)
            {
                int runMax = int.MinValue, runArg = -1;

                for (int j = 0; j < n; j++)
                {
                    best[i, j] = int.MinValue;
                    from[i, j] = -1;

                    if (lower[j] == term[i])
                    {
                        int bonus = Weight(text, j);

                        if (i == 0)
                        {
                            best[i, j] = bonus - Math.Min(j, LeadMax);
                        }
                        else if (j > 0)
                        {
                            int cand = prevMax[j - 1], arg = prevArg[j - 1];
                            int run = best[i - 1, j - 1];
                            if (run != int.MinValue && run + Consecutive > cand)
                            {
                                cand = run + Consecutive;
                                arg = j - 1;
                            }
                            if (cand != int.MinValue)
                            {
                                best[i, j] = cand + bonus;
                                from[i, j] = arg;
                            }
                        }
                    }

                    if (best[i, j] > runMax) { runMax = best[i, j]; runArg = j; }
                    curMax[j] = runMax;
                    curArg[j] = runArg;
                }

                if (runMax == int.MinValue) return int.MinValue;

                Array.Copy(curMax, prevMax, n);
                Array.Copy(curArg, prevArg, n);
            }

            int endCol = prevArg[n - 1];
            int score = prevMax[n - 1];

            for (int i = m - 1, j = endCol; i >= 0 && j >= 0; i--)
            {
                hits.Add(j);
                j = from[i, j];
            }

            int at = lower.IndexOf(term, StringComparison.Ordinal);
            if (at >= 0)
            {
                score += Verbatim;
                if (Weight(text, at) != Base) score += AtWord;
            }

            return score;
        }

        static int Weight(string text, int j)
        {
            if (j == 0) return Head;
            char p = text[j - 1], c = text[j];
            if (!char.IsLetterOrDigit(p)) return Boundary;
            if (char.IsLower(p) && char.IsUpper(c)) return Boundary;
            return Base;
        }
    }
}
