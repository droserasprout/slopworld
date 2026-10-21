using System.Collections.Generic;

namespace SlopWorld
{
    // Owns independent session and reader cursors. Inventories decide eligibility;
    // returning to a group restores its cursor before subsequent presses advance it.
    internal sealed class WorkspaceNavigation
    {
        internal static readonly WorkspaceNavigation Shared = new WorkspaceNavigation();

        string _session;
        string _reader;

        public void Remember(string name, bool reader)
        {
            if (string.IsNullOrEmpty(name))
                return;
            if (reader)
                _reader = name;
            else
                _session = name;
        }

        public string ReplaceReader(string oldName, string newName, string current = null)
        {
            // Refresh changes the process handle without creating a new reader visit.
            if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName))
                return current;
            if (_reader == oldName)
                _reader = newName;
            return current == oldName ? newName : current;
        }

        public string Select(List<string> order, string current, int direction, bool reader)
        {
            if (order.Count == 0)
                return null;
            int index = order.IndexOf(current);
            if (index >= 0)
                return order[(index + direction + order.Count) % order.Count];
            string remembered = reader ? _reader : _session;
            return order.Contains(remembered)
                ? remembered
                : order[direction > 0 ? 0 : order.Count - 1];
        }
    }
}
