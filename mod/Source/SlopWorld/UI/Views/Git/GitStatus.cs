namespace SlopWorld
{
    // One parsed porcelain pair supplies actions, presence, labels and conflict styling.
    internal readonly struct GitStatus
    {
        public readonly string Pair;
        public readonly char Staged, Worktree;
        public GitStatus(string pair)
        {
            Pair = pair ?? "";
            Staged = Pair.Length > 0 ? Pair[0] : ' ';
            Worktree = Pair.Length > 1 ? Pair[1] : ' ';
        }
        public bool Untracked => Pair == "??";
        public bool Unknown => string.IsNullOrEmpty(Pair);
        public bool NeedsStage => Unknown || Untracked || Worktree != ' ';
        public bool IsStaged => !Unknown && !Untracked && Staged != ' ';
        public bool Present => Unknown || Untracked || (Worktree != 'D' && !(Staged == 'D' && Worktree == ' '));
        public bool Unmerged => Pair == "AA" || Pair == "DD" || Pair == "AU" || Pair == "UD" ||
            Pair == "UA" || Pair == "DU" || Pair == "UU";
        public string Mark => Unknown || Untracked ? "?" :
            (Staged != ' ' && Staged != '?' ? Staged : Worktree).ToString();
    }
}
