using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // Item/template identities survive filtering. Action rows are selected only while visible.
    // Mouse policy stays here; the view owns hit testing and scroll-to-selection geometry.
    internal sealed class LibraryRowSelection
    {
        internal string Key { get; private set; }
        internal void Focus(string key) { Key = key; }

        internal bool Click(LibraryRowTarget target, int button, LibraryRowTarget.IEffects effects)
        {
            if (button != 0 && button != 1) return false;
            bool select = button == 0 || target.HasMenu;
            if (select) Key = target.Key;
            target.Remember(effects);
            if (button == 1) target.OpenMenu(effects);
            return select;
        }

        internal bool Reconcile(IEnumerable<string> visibleKeys)
        {
            if (string.IsNullOrEmpty(Key)) return false;
            if (visibleKeys.Contains(Key)) return true;
            ClearAction();
            return false;
        }

        internal void ClearAction()
        {
            if (Key != null && Key.StartsWith("action:", StringComparison.Ordinal)) Key = null;
        }
    }
}
