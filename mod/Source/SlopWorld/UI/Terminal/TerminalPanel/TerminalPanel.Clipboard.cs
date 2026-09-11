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

        // The clipboard errands, which never had a button anywhere. Nothing that ends an agent
        // is here - a menu opened to copy a line is the wrong place to find it. A Ctrl+RMB on a
        // project-relative path adds the same file errands the sidebar offers.
        internal void OpenMenu(string url, string path, int line)
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
            if (url == null && path != null && info != null && !string.IsNullOrEmpty(info.Project))
            {
                string project = info.Project;
                string picked = path;
                int pickedLine = line;
                string name = Leaf(path);
                options.Add(new FloatMenuOption("Focus", () => ResolvePath(project, picked,
                    absolute => FilesView.FocusPath(project, absolute))));
                options.Add(new FloatMenuOption("View", () => ResolvePath(project, picked,
                    absolute => FilesView.ViewFile(project, absolute, "view-" + name))));
                options.Add(new FloatMenuOption("Edit", () => ResolvePath(project, picked,
                    absolute => FilesView.EditFile(project, absolute, "edit-" + name, pickedLine))));
            }

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
            breadcrumbMenu.Disabled = !SessionHub.Instance.Config.ExperimentalBreadcrumbs ||
                info == null || !info.Alive || breadcrumbs.Count == 0;
            options.Add(breadcrumbMenu);
            SelectionCommands.AddSelectAll(options, selectionAvailability, SelectAll);
            options.Add(new UiSubmenu("Open beside", () =>
            {
                var sessions = new List<FloatMenuOption>();
                foreach (string session in TerminalWindow.TabOrder())
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

        void ResolvePath(string project, string path, System.Action<string> action)
        {
            SessionHub.Instance.SessionStore.CurrentPath(_state.Name, cwd =>
            {
                string absolute = FilesView.ResolveProjectPath(project, path, cwd);
                if (absolute == null)
                {
                    UiLayout.Fail($"path is outside project: {path}");
                    return;
                }
                action(absolute);
            }, UiLayout.Fail);
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
                DaemonClient.Get(WireContract.Routes.ClipboardText,
                    j =>
                    {
                        string text = j["text"].AsString();
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
                Deliver(name, GUIUtility.systemCopyBuffer);
                return;
            }
            string path = HostClipboardTextOnly ? WireContract.Routes.ClipboardText : WireContract.Routes.Clipboard;
            DaemonClient.Get(path,
                j => Deliver(name, j["text"].AsString()),
                _ => Deliver(name, null));
        }

        // Middle-click reads Wayland/X11 PRIMARY, not the ordinary CLIPBOARD. There is no
        // useful game-local fallback: Unity exposes the latter, if anything, and substituting
        // it would make a missing primary selection paste the wrong text.
        internal void PastePrimarySelection()
        {
            if (!SessionHub.Instance.Capabilities.Clipboard) return;
            string name = _state.Name;
            string path = HostClipboardTextOnly
                ? WireContract.Routes.ClipboardPrimaryText
                : WireContract.Routes.ClipboardPrimary;
            DaemonClient.Get(path,
                j => DeliverPrimary(name, j["text"].AsString()),
                _ => { });
        }

        static void Deliver(string name, string text)
        {
            if (string.IsNullOrEmpty(text)) text = GUIUtility.systemCopyBuffer;
            if (!string.IsNullOrEmpty(text)) SessionHub.Instance.Terminal.Paste(name, text);
        }

        static void DeliverPrimary(string name, string text)
        {
            if (!string.IsNullOrEmpty(text)) SessionHub.Instance.Terminal.Paste(name, text);
        }
    }
}
