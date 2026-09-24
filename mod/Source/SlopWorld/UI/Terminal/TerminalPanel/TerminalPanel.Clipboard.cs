using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Clipboard and context-menu actions for TerminalPanel.
    sealed partial class TerminalPanel
    {
        internal void CopySelection()
        {
            var buf = DisplayedBuf();
            if (buf != null) CopyText(SelectionText(buf));
        }

        internal void CopyPrimarySelection()
        {
            var buf = DisplayedBuf();
            if (buf != null) CopyPrimaryText(SelectionText(buf));
        }

        // Trailing newlines go: a screen is padded to its row count, so an app half a screen
        // tall would copy the blank half with it.
        void SelectAll()
        {
            var buf = DisplayedBuf();
            if (buf == null || buf.Lines.Length == 0) return;

            EnsureRuns(buf);
            _state.Selection.A = Vector2Int.zero;
            _state.Selection.B = new Vector2Int(buf.Cols, buf.Runs.Length - 1);
            _state.Selection.HasSelection = true;
            _state.Selection.Dragging = false;
            _state.Selection.MultiClickSelection = false;
            _state.Selection.WordDragging = false;
            _state.Selection.LineDragging = false;
            ReleaseSelection();
            CopyText(SelectionText(buf).TrimEnd('\n'));
        }

        // The *host's* clipboard, through the daemon: on this Unity player
        // GUIUtility.systemCopyBuffer is as often the process's own buffer as the desktop's.
        // An agent copying on its own behalf goes via OSC 52 instead, never through here.
        void CopyText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            DaemonClipboard.Copy(text, null,
                msg => Log.Warning($"[SlopWorld] clipboard: {msg}"));
        }

        void CopyPrimaryText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            DaemonClipboard.CopyPrimary(text, null,
                msg => Log.Warning($"[SlopWorld] primary selection: {msg}"));
        }

        // Pane actions stay separate from the Ctrl+click file menu.
        internal void OpenMenu(string url)
        {
            var options = new List<FloatMenuOption>();

            // First when there is one: the pointer is already on it, and the right button is
            // the road for anyone who never learned Ctrl+click.
            if (url != null)
            {
                options.Add(new FloatMenuOption("Open " + url.Truncate(360f), () => OpenUrl(url)));
                options.Add(new FloatMenuOption("Copy link", () => CopyText(url)));
            }

            var info = SessionHub.Instance.Get(_state.Name);
            var selectionAvailability = new SelectionCommandAvailability(
                _state.Selection.HasSelection, true, true, false);
            SelectionCommands.AddCopy(options, selectionAvailability, CopySelection);
            if (info != null)
            {
                string name = _state.Name;
                options.Add(new FloatMenuOption("Label", () =>
                {
                    var current = SessionHub.Instance.Get(name);
                    if (current != null) LabelDialog.Open(name, current.Label);
                }));
            }
            SelectionCommands.AddPaste(options, selectionAvailability,
                () => { JumpToLive(); PasteClipboard(); });
            var breadcrumbs = AllBreadcrumbs();
            var breadcrumbMenu = new UiSubmenu("Breadcrumbs",
                () => BreadcrumbOptions(breadcrumbs));
            breadcrumbMenu.Disabled = info == null || !info.Alive || breadcrumbs.Count == 0;
            options.Add(breadcrumbMenu);
            SelectionCommands.AddSelectAll(options, selectionAvailability, SelectAll);
            options.Add(new UiSubmenu("Open beside", () =>
            {
                var sessions = new List<FloatMenuOption>();
                foreach (string session in TerminalWindow.OpenBesideOrder())
                {
                    if (session == _state.Name) continue;
                    string picked = session;
                    sessions.Add(new FloatMenuOption(picked, () => TerminalWindow.OpenSplit(picked)));
                }
                return sessions;
            }));
            options.Add(new FloatMenuOption("Close pane", Close));

            if (_scrollOff > 0)
                options.Add(new FloatMenuOption("Back to the live view", () =>
                {
                    JumpToLive();
                    ClearSelection();
                }));

            TerminalWindow.OpenOverPane(new UiMenu(options));
        }

        // Resolve only on activation, never during terminal repaint or hover. Copy remains
        // available without a project, including paths outside the project's file tree.
        internal void OpenPathMenu(string path, int line)
        {
            var info = SessionHub.Instance.Get(_state.Name);
            string project = info?.Project;
            if (string.IsNullOrEmpty(project))
            {
                ShowPathMenu(null, path, null, line);
                return;
            }
            var owner = SessionHub.Instance.Project(project);
            if (owner != null)
            {
                SidebarScopes.Update();
                project = BrowseScope.Identity(BrowseScope.ProjectIdOf(owner), string.IsNullOrEmpty(info.Worktree) ? "main" : info.Worktree);
            }
            SessionHub.Instance.SessionStore.CurrentPath(_state.Name, cwd =>
                LoadPathMenu(project, path, FilesView.ResolveProjectPath(project, path, cwd), line),
                error => ShowPathMenu(null, path, null, line));
        }

        void LoadPathMenu(string project, string path, string absolute, int line)
        {
            if (absolute == null)
            {
                ShowPathMenu(project, path, null, line);
                return;
            }

            // Ask the daemon host, not the game machine. Include hidden and ignored entries:
            // terminal links must not depend on the sidebar's current listing filters.
            absolute = absolute.TrimEnd('/');
            string parent = absolute.Substring(0, absolute.LastIndexOf('/'));
            if (parent.Length == 0) parent = "/";
            string name = Leaf(absolute);
            DaemonClient.Get<Wire.BrowseResult>(WireProtocol.Routes.Browse +
                "?files=1&hidden=1&gitignore=0&path=" + System.Uri.EscapeDataString(parent),
                listing => ShowPathMenu(project, path, absolute, line,
                    listing.Dirs.Contains(name), listing.Files.Contains(name)),
                error => ShowPathMenu(project, path, absolute, line));
        }

        void ShowPathMenu(string project, string path, string absolute, int line,
            bool isDirectory = false, bool isFile = false)
        {
            var options = new List<FloatMenuOption>();
            if (absolute != null && (isDirectory || isFile))
            {
                string name = Leaf(absolute);
                options.Add(new FloatMenuOption("Focus", () => FilesView.FocusPath(project, absolute)));
                if (isFile && FilesView.IsText(name))
                {
                    options.Add(new FloatMenuOption("View", () =>
                        FilesView.ViewFile(project, absolute, "view-" + name, line)));
                    options.Add(new FloatMenuOption("Edit", () =>
                        FilesView.EditFile(project, absolute, "edit-" + name, line)));
                }
                FilesView.AddOpenIn(options, absolute, project);
                FilesView.AddFileActions(options, project, absolute, name);
            }
            options.Add(new FloatMenuOption("Copy path", () => CopyText(absolute ?? path)));
            TerminalWindow.OpenOverPane(new UiMenu(options));
        }

        static string Leaf(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash < 0 ? path : path.Substring(slash + 1);
        }

        static List<string> AllBreadcrumbs() => SessionHub.Instance.Library
            .Where(s => s.Kind == LibraryItemKind.Breadcrumb)
            .Select(s => s.Name)
            .ToList();

        List<FloatMenuOption> BreadcrumbOptions(List<string> names)
        {
            var options = new List<FloatMenuOption>();
            foreach (string name in names)
            {
                string picked = name;
                options.Add(new FloatMenuOption(picked, () =>
                {
                    JumpToLive();
                    SessionHub.Instance.Terminal.PasteBreadcrumb(_state.Name, picked,
                        Patch_LoadingTips.RandomTips(Patch_LoadingTips.TipBatch));
                }));
            }
            return options;
        }

        // Falls back to the game's own buffer. A round trip, so the paste lands a frame or
        // two later.
        // Missing session metadata is conservative: a stale snapshot must not send an image to
        // a host shell.
        bool HostClipboardTextOnly => SessionHub.Instance.Get(_state.Name)?.Host != false;

        // Codex owns Ctrl+V for image clipboard data: its TUI turns that data into an attachment.
        // Sending image bytes through the daemon's text/JSON paste path turns them into a huge
        // string of replacement characters instead.
        bool CodexImagePaste => !HostClipboardTextOnly &&
            SessionHub.Instance.Get(_state.Name)?.CommandPreset == "codex";

        static void ForwardCodexImagePaste(string name)
        {
            SessionHub.Instance.Terminal.SendKeys(name, new[] { "C-v" }, false);
        }

        internal void PasteClipboard()
        {
            string name = _state.Name;
            if (CodexImagePaste && SessionHub.Instance.Capabilities.Clipboard)
            {
                Flush();
                // Codex's image handler claims Ctrl+V even when the clipboard only has text,
                // then reports "no image". Read the text format first and reserve Ctrl+V for an
                // image (or another non-text clipboard format).
                DaemonClient.Get<Wire.TextResult>(WireProtocol.Routes.ClipboardText,
                    j =>
                    {
                        string text = j.Text;
                        if (!string.IsNullOrEmpty(text))
                            SessionHub.Instance.Terminal.Paste(name, text);
                        else
                            ForwardCodexImagePaste(name);
                    },
                    _ => ForwardCodexImagePaste(name));
                return;
            }
            if (!SessionHub.Instance.Capabilities.Clipboard)
            {
                DeliverLocal(name);
                return;
            }
            string path = HostClipboardTextOnly ? WireProtocol.Routes.ClipboardText : WireProtocol.Routes.Clipboard;
            DaemonClient.Get<Wire.TextResult>(path,
                j => Deliver(name, j.Text),
                _ => DeliverLocal(name));
        }

        // Middle-click reads Wayland/X11 PRIMARY, not the ordinary CLIPBOARD. There is no
        // useful game-local fallback: Unity exposes the latter, if anything, and substituting
        // it would make a missing primary selection paste the wrong text.
        internal void PastePrimarySelection()
        {
            if (!SessionHub.Instance.Capabilities.Clipboard) return;
            string name = _state.Name;
            string path = HostClipboardTextOnly
                ? WireProtocol.Routes.ClipboardPrimaryText
                : WireProtocol.Routes.ClipboardPrimary;
            DaemonClient.Get<Wire.TextResult>(path,
                j => DeliverPrimary(name, j.Text),
                _ => { });
        }

        static void Deliver(string name, string text)
        {
            if (!string.IsNullOrEmpty(text)) SessionHub.Instance.Terminal.Paste(name, text);
        }

        static void DeliverLocal(string name)
        {
            string text = GUIUtility.systemCopyBuffer;
            if (!string.IsNullOrEmpty(text)) SessionHub.Instance.Terminal.Paste(name, text);
        }

        static void DeliverPrimary(string name, string text)
        {
            if (!string.IsNullOrEmpty(text)) SessionHub.Instance.Terminal.Paste(name, text);
        }
    }
}
