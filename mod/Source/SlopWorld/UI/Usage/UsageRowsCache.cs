using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Usage arrives as replacement snapshots, but the settings editor mutates poll flags.
    internal sealed class UsageRowsCache
    {
        public readonly List<string> Rows = new List<string>();
        public int Revision { get; private set; }
        UsageInfo _usage;
        bool _hasConfig, _ready;
        readonly Dictionary<string, bool?> _polls = new Dictionary<string, bool?>();

        public bool Prepare(UsageInfo usage, DaemonConfig config)
        {
            if (_ready && ReferenceEquals(_usage, usage) && Matches(config)) return false;
            _ready = true;
            _usage = usage;
            _hasConfig = config != null;
            _polls.Clear();
            if (config?.UsageItems != null)
                foreach (var pair in config.UsageItems) _polls[pair.Key] = pair.Value?.Poll;
            Rows.Clear();
            bool weekly = false;
            if (usage != null)
            {
                foreach (var window in usage.Windows)
                {
                    if (window.Key == SharedUsage.OpenaiWeek) weekly = true;
                    Add(window.Key);
                }
                foreach (var source in usage.Sources)
                {
                    if (source == "anthropic")
                    {
                        Add(SharedUsage.ClaudeSession);
                        Add(SharedUsage.ClaudeWeek);
                    }
                    else if (source == "openai" && !weekly) Add(SharedUsage.OpenaiSession);
                    else if (source == "openrouter") Add(SharedUsage.OpenrouterBalance);
                }
            }
            Rows.Sort(Compare);
            Revision++;
            return true;
        }

        bool Matches(DaemonConfig config)
        {
            if (_hasConfig != (config != null) || _polls.Count != (config?.UsageItems?.Count ?? 0)) return false;
            if (config?.UsageItems != null)
                foreach (var pair in config.UsageItems)
                    if (!_polls.TryGetValue(pair.Key, out var poll) || poll != pair.Value?.Poll) return false;
            return true;
        }

        void Add(string key)
        {
            if (Polled(key) && !Rows.Contains(key)) Rows.Add(key);
        }

        bool Polled(string key)
        {
            if (!_hasConfig) return true;
            if (_polls.TryGetValue(key, out var poll) && poll.HasValue) return poll.Value;
            return key.StartsWith("claude_", StringComparison.Ordinal) ||
                key.StartsWith("openai_", StringComparison.Ordinal);
        }

        static int Compare(string a, string b) => Rank(a).CompareTo(Rank(b));

        static int Rank(string key)
        {
            if (key == SharedUsage.ClaudeSession) return 0;
            if (key == SharedUsage.ClaudeWeek) return 1;
            if (key == SharedUsage.OpenaiSession) return 2;
            if (key == SharedUsage.OpenaiWeek) return 3;
            if (key == SharedUsage.OpenrouterBalance) return 4;
            if (key == SharedUsage.ClaudeSpend) return 5;
            return 6;
        }
    }
}
