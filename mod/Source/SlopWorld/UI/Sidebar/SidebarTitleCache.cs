using System;
using System.Runtime.CompilerServices;

namespace SlopWorld
{
    // Session snapshots mutate in place. Key title cleanup on its actual inputs, not activity.
    internal sealed class SidebarTitleCache
    {
        sealed class Entry
        {
            public string Label, Title, Dir, Text;
            public bool Host, Valid;
            public object Font;
            public Func<SessionInfo, string> Builder;
            public int FontRevision;
        }

        readonly ConditionalWeakTable<SessionInfo, Entry> _entries =
            new ConditionalWeakTable<SessionInfo, Entry>();
        static readonly ConditionalWeakTable<SessionInfo, Entry>.CreateValueCallback Create = _ => new Entry();

        public string Get(SessionInfo info, object font, int fontRevision, Func<SessionInfo, string> build)
        {
            if (info == null) return "";
            var entry = _entries.GetValue(info, Create);
            if (entry.Valid && entry.Label == info.Label && entry.Title == info.Title &&
                entry.Dir == info.Dir && entry.Host == info.Host &&
                ReferenceEquals(entry.Font, font) && entry.FontRevision == fontRevision &&
                entry.Builder == build)
                return entry.Text;

            entry.Valid = false;
            entry.Label = info.Label;
            entry.Title = info.Title;
            entry.Dir = info.Dir;
            entry.Host = info.Host;
            entry.Font = font;
            entry.FontRevision = fontRevision;
            entry.Builder = build;
            entry.Text = build(info);
            entry.Valid = true;
            return entry.Text;
        }
    }
}
