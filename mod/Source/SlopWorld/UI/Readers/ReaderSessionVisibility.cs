using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SlopWorld
{
    // Pager owns process handoffs. This projection keeps their pending and retiring
    // sessions out of sidebar rows and restoration until ownership has settled.
    internal sealed class ReaderSessionVisibility
    {
        static readonly ConditionalWeakTable<SessionHub, ReaderSessionVisibility> Owners =
            new ConditionalWeakTable<SessionHub, ReaderSessionVisibility>();

        internal static ReaderSessionVisibility For(SessionHub owner) =>
            Owners.GetValue(owner, _ => new ReaderSessionVisibility());

        internal sealed class Launch
        {
            internal string Scope,
                Key,
                Intent;
        }

        readonly List<Launch> _pending = new List<Launch>();
        readonly HashSet<string> _claimed = new HashSet<string>();
        readonly HashSet<string> _retiring = new HashSet<string>();

        internal Launch Begin(string scope, string key, string intent)
        {
            var launch = new Launch
            {
                Scope = scope ?? "",
                Key = key ?? "",
                Intent = intent,
            };
            _pending.Add(launch);
            RoutedSessionRows.Invalidate();
            return launch;
        }

        internal void End(Launch launch)
        {
            _pending.Remove(launch);
            RoutedSessionRows.Invalidate();
        }

        internal void Claim(string session)
        {
            if (session != null && _claimed.Add(session))
                RoutedSessionRows.Invalidate();
        }

        internal void Retire(string session)
        {
            if (session == null)
                return;
            _claimed.Remove(session);
            if (_retiring.Add(session))
                RoutedSessionRows.Invalidate();
        }

        internal bool Includes(SessionInfo info)
        {
            // Stopped sessions can remain in the snapshot with reader intent/commands.
            // They must not keep routed rows or the Open files pane alive.
            if (info == null || !info.Alive || _retiring.Contains(info.Name))
                return false;
            // A newer request can claim its session while an older matching launch
            // still awaits its callback. Its committed row must remain visible.
            if (_claimed.Contains(info.Name))
                return true;
            string scope = string.IsNullOrEmpty(info.ReaderScope) ? info.Project : info.ReaderScope;
            foreach (var launch in _pending)
                if (
                    info.Ephemeral
                    && info.Intent == launch.Intent
                    && (scope ?? "") == launch.Scope
                    && (info.ReaderKey ?? "") == launch.Key
                )
                    return false;
            return true;
        }

        // Called once per authoritative session revision. Names are disposable reader
        // identities; retirement guards can go once their removal reaches the snapshot.
        internal void Reconcile(IEnumerable<SessionInfo> sessions)
        {
            var names = new HashSet<string>();
            foreach (var info in sessions)
                if (info != null)
                    names.Add(info.Name);
            _retiring.RemoveWhere(name => !names.Contains(name));
            _claimed.RemoveWhere(name => !names.Contains(name));
        }
    }
}
