namespace SlopWorld
{
    // Immutable submission: changing filters reruns this query, not partially edited fields.
    public sealed class SearchSubmission
    {
        public readonly string Query;
        public readonly bool Regex, Case, Word, IncludeIgnored;
        public SearchSubmission(string query, bool regex, bool sensitive, bool word, bool includeIgnored)
        {
            Query = (query ?? "").Trim();
            Regex = regex; Case = sensitive; Word = word; IncludeIgnored = includeIgnored;
        }
    }
}
