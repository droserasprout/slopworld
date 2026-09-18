using System;
using System.Collections.Generic;

namespace SlopWorld
{
    public enum SidebarViewLocationKind
    {
        Tab,
        Agent,
        File,
        Search,
        Git,
        Task,
        Library,
    }

    // A view location stores semantic identity rather than a row or view instance. The
    // latter are rebuilt when daemon data arrives, while these values remain useful to the
    // back/forward stack and to a later Ctrl+F tab focus.
    public readonly struct SidebarViewLocation : IEquatable<SidebarViewLocation>
    {
        public readonly SidebarTab Tab;
        public readonly SidebarViewLocationKind Kind;
        public readonly string Primary;
        public readonly string Secondary;
        public readonly int Line;

        public bool HasTarget => Kind != SidebarViewLocationKind.Tab;

        SidebarViewLocation(SidebarTab tab, SidebarViewLocationKind kind,
                            string primary, string secondary, int line)
        {
            Tab = tab;
            Kind = kind;
            Primary = primary ?? "";
            Secondary = secondary ?? "";
            Line = line;
        }

        public static SidebarViewLocation TabOnly(SidebarTab tab) =>
            new SidebarViewLocation(tab, SidebarViewLocationKind.Tab, null, null, 0);

        public static SidebarViewLocation Agent(string session) =>
            new SidebarViewLocation(SidebarTab.Agents, SidebarViewLocationKind.Agent,
                session, null, 0);

        public static SidebarViewLocation File(string project, string path) =>
            new SidebarViewLocation(SidebarTab.Files, SidebarViewLocationKind.File,
                project, path, 0);

        public static SidebarViewLocation Search(string project, string path, int line) =>
            new SidebarViewLocation(SidebarTab.Search, SidebarViewLocationKind.Search,
                project, path, line);

        public static SidebarViewLocation Git(string project, string path) =>
            new SidebarViewLocation(SidebarTab.Git, SidebarViewLocationKind.Git,
                project, path, 0);

        public static SidebarViewLocation Task(string id) =>
            new SidebarViewLocation(SidebarTab.Tasks, SidebarViewLocationKind.Task,
                id, null, 0);

        public static SidebarViewLocation Library(string name, bool template = false) =>
            new SidebarViewLocation(SidebarTab.Library, SidebarViewLocationKind.Library,
                name, template ? "template" : "", 0);

        public bool Equals(SidebarViewLocation other) => Tab == other.Tab && Kind == other.Kind &&
            Line == other.Line && Primary == other.Primary && Secondary == other.Secondary;

        public override bool Equals(object obj) => obj is SidebarViewLocation other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ((int)Tab * 397) ^ (int)Kind;
                hash = (hash * 397) ^ (Primary?.GetHashCode() ?? 0);
                hash = (hash * 397) ^ (Secondary?.GetHashCode() ?? 0);
                return (hash * 397) ^ Line;
            }
        }

        public static bool operator ==(SidebarViewLocation left, SidebarViewLocation right) =>
            left.Equals(right);

        public static bool operator !=(SidebarViewLocation left, SidebarViewLocation right) =>
            !left.Equals(right);
    }

    // Process-local browser history for the shared sidebar. The latest target is retained per
    // tab even when the user only switches tabs, while Back and Forward move through the
    // actual locations visited in order.
    public sealed class SidebarViewHistory
    {
        const int MaxEntries = 64;
        readonly List<SidebarViewLocation> _back = new List<SidebarViewLocation>();
        readonly List<SidebarViewLocation> _forward = new List<SidebarViewLocation>();
        readonly Dictionary<SidebarTab, SidebarViewLocation> _last =
            new Dictionary<SidebarTab, SidebarViewLocation>();
        SidebarViewLocation _current;
        bool _hasCurrent;

        public bool CanBack => _back.Count > 0;
        public bool CanForward => _forward.Count > 0;
        public bool HasCurrent => _hasCurrent;
        public SidebarViewLocation Current => _current;

        public void VisitTab(SidebarTab tab)
        {
            // Reselecting the visible tab is a refresh, not a new browser location. This also
            // keeps Ctrl+F1 on the current agent from inserting a blank point in the stack.
            if (_hasCurrent && _current.Tab == tab) return;
            Visit(SidebarViewLocation.TabOnly(tab));
        }

        public void Visit(SidebarViewLocation location)
        {
            if (_hasCurrent && _current == location) return;

            if (_hasCurrent) Push(_back, _current);
            _current = location;
            _hasCurrent = true;
            if (location.HasTarget) _last[location.Tab] = location;
            _forward.Clear();
        }

        public bool TryLast(SidebarTab tab, out SidebarViewLocation location) =>
            _last.TryGetValue(tab, out location);

        public bool Back(out SidebarViewLocation location)
        {
            if (_back.Count == 0)
            {
                location = default(SidebarViewLocation);
                return false;
            }

            if (_hasCurrent) Push(_forward, _current);
            int last = _back.Count - 1;
            location = _back[last];
            _back.RemoveAt(last);
            _current = location;
            _hasCurrent = true;
            if (location.HasTarget) _last[location.Tab] = location;
            return true;
        }

        public bool Forward(out SidebarViewLocation location)
        {
            if (_forward.Count == 0)
            {
                location = default(SidebarViewLocation);
                return false;
            }

            if (_hasCurrent) Push(_back, _current);
            int last = _forward.Count - 1;
            location = _forward[last];
            _forward.RemoveAt(last);
            _current = location;
            _hasCurrent = true;
            if (location.HasTarget) _last[location.Tab] = location;
            return true;
        }

        static void Push(List<SidebarViewLocation> stack, SidebarViewLocation location)
        {
            stack.Add(location);
            if (stack.Count > MaxEntries) stack.RemoveAt(0);
        }
    }
}
