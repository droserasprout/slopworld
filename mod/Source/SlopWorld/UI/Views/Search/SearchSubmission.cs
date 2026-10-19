namespace SlopWorld
{
    // Immutable submission: changing filters reruns this query, not partially edited fields.
    public sealed class SearchSubmission
    {
        public readonly string Query;
        public readonly string Include,
            Exclude;
        public readonly bool Regex,
            CaseSensitive,
            WholeWord,
            IncludeIgnored;

        public SearchSubmission(
            string query,
            bool regex,
            bool sensitive,
            bool word,
            bool includeIgnored,
            string include = "",
            string exclude = ""
        )
        {
            Query = query ?? "";
            Include = (include ?? "").Trim();
            Exclude = (exclude ?? "").Trim();
            Regex = regex;
            CaseSensitive = sensitive;
            WholeWord = word;
            IncludeIgnored = includeIgnored;
        }
    }
}
