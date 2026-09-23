using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public partial class TerminalWindow
    {
        public static TerminalWindow Open(string name)
        {
            // The current session follows the pane.
            SessionSelectable.Current = name;

            // Re-opening the same session should focus it, not stack a second copy. Asking for a
            // pane always puts the pane back, though, even the one already behind the content. A
            // portrait clicked while the options menu is up is a request to see that agent.
            var existing = Find.WindowStack.WindowOfType<TerminalWindow>();
            if (existing != null)
            {
                existing.SwitchTo(name);
                return existing;
            }

            var w = new TerminalWindow(name);
            Find.WindowStack.Add(w);
            TerminalRecall.Remember(name);
            return w;
        }

        // Open the requested content view.
        // If a pane is open, keep it behind the content so Leave can restore it.
        // If no pane is open, show only the content view, such as the map options menu.
        public static void OpenContent(IContentView view)
        {
            if (view == null || Find.WindowStack == null) return;

            var existing = Find.WindowStack.WindowOfType<TerminalWindow>();
            if (existing != null) { existing.SetContent(view); return; }

            var w = new TerminalWindow(null);
            Find.WindowStack.Add(w);
            w.SetContent(view);
        }

        // Return the pane session only while the window shows the pane.
        // A content view has no current agent. Its agent rows remain unselected and can open their panes.
        public static string CurrentName
        {
            get
            {
                var w = Find.WindowStack?.WindowOfType<TerminalWindow>();
                return w == null || !w.TerminalVisible ? null : w._name;
            }
        }

        // Content views hide the active session from CurrentName. However, F12 still needs to know
        // whether leaving the view will reveal a pane or remove a content-only host.
        internal static bool HasBackingPane =>
            Find.WindowStack?.WindowOfType<TerminalWindow>()?._name != null;

        // A successful rename must not go through Open. That would reset the pane and can briefly
        // bind it to the old name while the sessions snapshot catches up. Keep the existing window,
        // scrollback and selection, changing only the session handle.
        internal static void RenameActive(string oldName, string newName)
        {
            if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName) ||
                oldName == newName) return;

            var window = Find.WindowStack?.WindowOfType<TerminalWindow>();
            var renamed = window?._terminals.Find(oldName);
            bool active = renamed != null && renamed == window._terminal;
            if (renamed != null)
            {
                renamed.SessionName = newName;
                TerminalRecall.Remember(newName);
            }

            if (!active && SessionSelectable.Current != oldName) return;
            SessionSelectable.Current = newName;
            if (active && window._content == null) SelectAgent(newName);
        }

        // What the chrome is showing, for anything that has to know which it is. Null is the
        // pane, and null window is neither.
        public static IContentView Showing =>
            Find.WindowStack?.WindowOfType<TerminalWindow>()?._content;

        // The one of a kind already up, so a door that opens a view can hand the same one back
        // rather than build a second. Pressing `config` twice is a toggle, not a reset.
        public static T ShowingAs<T>() where T : class, IContentView => Showing as T;

        // If this view is already open, close it. Otherwise, create and open it.
        // Accept a factory so a close action does not create a view or query the daemon.
        // Route every view entry point here to keep toggles consistent.
        public static void ToggleContent<T>(System.Func<T> make) where T : class, IContentView
        {
            if (ShowingAs<T>() != null)
            {
                Find.WindowStack?.WindowOfType<TerminalWindow>()?.Leave();
                return;
            }
            OpenContent(make());
        }

        void SetContent(IContentView view)
        {
            if (_content == view) return;
            _fieldLifetime.Cancel();
            _fieldLifetime = new FieldLifetime();
            _panels.SetContent(view);
        }

        // Out of the content and back to what is behind it: the pane it was opened over, or
        // the map when there was none. The chrome exists to show something.
        public void Leave()
        {
            if (_content == null) return;
            SetContent(null);
            if (_name == null) Close();
        }

        // The pane is on the Super layer, so an ordinary dialog opened from inside it would be
        // added underneath and never seen.
        public static void OpenOverPane(Window w)
        {
            if (Find.WindowStack == null) return;
            if (Find.WindowStack.WindowOfType<TerminalWindow>() != null)
                w.layer = WindowLayer.Super;
            Find.WindowStack.Add(w);
        }

        // Content views remain read-only, but a view opened over a pane can still offer the
        // terminal's paste action to the agent behind it. A view opened from the map has no
        // destination, so its Paste menu item is disabled.
        public static bool CanPasteClipboardToAgent =>
            Find.WindowStack?.WindowOfType<TerminalWindow>()?._name != null;

        public static void PasteClipboardToAgent()
        {
            var window = Find.WindowStack?.WindowOfType<TerminalWindow>();
            if (window == null || window._name == null) return;
            window._terminal.JumpToLive();
            window._terminal.PasteClipboard();
        }

        // PaneOverDraw reads this several times a frame, so the closed case costs one static
        // read. Open, it is checked against the stack: a flag left standing wrongly is a map
        // never drawn again.
        static bool _covering;

        public static bool Covering =>
            _covering && Find.WindowStack?.WindowOfType<TerminalWindow>() != null;

        // Rebinds the pane but keeps the window's place in the stack. Whatever was in the body
        // goes: being pointed at an agent is a request to see it. History rows belong to the
        // old session, while its scroll position is saved for the next visit.
        internal void SwitchTo(string name)
        {
            SetContent(null);
            var existing = _terminals.Find(name);
            if (existing != null)
            {
                _terminals.Select(existing);
                return;
            }
            if (_name == name) return;
            ArrangeTerminal(WorkspaceLayout.Current.Content);
            _terminal.BindSession(name);
            _panels.SetBacking(name == null ? null : _terminals);
            if (name != null) SelectAgent(name);
        }

        // Clearing first: the brackets' jump-out is an animation off SelectionDrawer's select
        // time, so a pawn already selected would never replay it.
        static void SelectAgent(string session)
        {
            var pawn = AgentColony.Current?.PawnOf(session);
            if (pawn == null) return;
            Find.Selector.ClearSelection();
            Find.Selector.Select(pawn);
        }

        public override void PreOpen()
        {
            base.PreOpen();
            ArrangeTerminal(WorkspaceLayout.Current.Content);
            if (_name != null) _panels.SetBacking(_terminals);
            _covering = true;
            if (_name != null) SelectAgent(_name);
        }

        public override void PostClose()
        {
            _fieldLifetime.Cancel();
            base.PostClose();
            _covering = false;
            _panels.Close();
        }

        bool EnsureSession(SessionHub hub)
        {
            // A disappearing ephemeral session removes only its pane. Durable stopped
            // sessions stay visible, including when Settings covers the split.
            if (_terminals.Split)
            {
                var first = _terminals.First;
                var second = _terminals.Second;
                if (!first.EnsureSession(hub, false)) _terminals.Remove(first);
                if (!second.EnsureSession(hub, false))
                {
                    if (_terminals.Split) _terminals.Remove(second);
                    else if (_content == null) { Close(); return false; }
                }
                if (_terminals.Split) return true;
            }
            if (!_terminal.EnsureSession(hub, _content != null))
            {
                Close();
                return false;
            }
            if (_name == null) _panels.SetBacking(null);
            return true;
        }

        internal static void OpenSplit(string name)
        {
            var window = Find.WindowStack?.WindowOfType<TerminalWindow>();
            if (window == null || window._name == null) { Open(name); return; }
            window.SetContent(null);
            window.ArrangeTerminal(WorkspaceLayout.Current.Content);
            window._terminals.OpenSplit(name);
        }

        public static Color StateColor(AgentState s)
        {
            switch (s)
            {
                case AgentState.Working: return UiTheme.StateWorking;
                case AgentState.Waiting: return UiTheme.StateWaiting;
                case AgentState.Idle: return UiTheme.StateIdle;
                default: return UiTheme.StateDown;
            }
        }
    }
}
