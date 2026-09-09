using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Clipboard and context-menu actions for TerminalWindow.
    public partial class TerminalWindow
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
            _selA = Vector2Int.zero;
            _selB = new Vector2Int(buf.Cols, buf.Runs.Length - 1);
            _hasSel = true;
            _dragging = false;
            _multiClickSelection = false;
            _wordDragging = false;
            _lineDragging = false;
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

            var info = SessionHub.Instance.Get(_name);
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

            var copy = new FloatMenuOption("Copy", CopySelection);
            copy.Disabled = !_hasSel;
            options.Add(copy);
            if (info != null)
            {
                string name = _name;
                options.Add(new FloatMenuOption("Label", () =>
                {
                    var current = SessionHub.Instance.Get(name);
                    if (current != null) LabelDialog.Open(name, current.Label);
                }));
            }
            options.Add(new FloatMenuOption("Paste", () => { JumpToLive(); PasteClipboard(); }));
            var breadcrumbs = AllBreadcrumbs();
            var breadcrumbMenu = new UiSubmenu("Breadcrumbs",
                () => BreadcrumbOptions(breadcrumbs));
            breadcrumbMenu.Disabled = info == null || !info.Alive || breadcrumbs.Count == 0;
            options.Add(breadcrumbMenu);
            options.Add(new FloatMenuOption("Select all", SelectAll));

            if (_scrollOff > 0)
                options.Add(new FloatMenuOption("Back to the live view", () =>
                {
                    JumpToLive();
                    ClearSelection();
                }));

            OpenOverPane(new UiMenu(options));
        }

        void ResolvePath(string project, string path, System.Action<string> action)
        {
            SessionHub.Instance.SessionStore.CurrentPath(_name, cwd =>
            {
                string absolute = FilesView.ResolveProjectPath(project, path, cwd);
                if (absolute == null)
                {
                    UiWidgets.Fail($"path is outside project: {path}");
                    return;
                }
                action(absolute);
            }, UiWidgets.Fail);
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
                    SessionHub.Instance.Terminal.PasteBreadcrumb(_name, picked,
                        Patch_LoadingTips.RandomTips(Patch_LoadingTips.TipBatch));
                }));
            }
            return options;
        }

        // Falls back to the game's own buffer. A round trip, so the paste lands a frame or
        // two later.
        // Missing session metadata is conservative: a stale snapshot must not send an image to
        // a host shell.
        bool HostClipboardTextOnly => SessionHub.Instance.Get(_name)?.Host != false;

        // Codex owns Ctrl+V for image clipboard data: its TUI turns that data into an attachment.
        // Sending image bytes through the daemon's text/JSON paste path turns them into a huge
        // string of replacement characters instead.
        bool CodexImagePaste => !HostClipboardTextOnly &&
            SessionHub.Instance.Get(_name)?.CommandPreset == "codex";

        static void ForwardCodexImagePaste(string name)
        {
            SessionHub.Instance.Terminal.SendKeys(name, new[] { "C-v" }, false);
        }

        internal void PasteClipboard()
        {
            string name = _name;
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
            string name = _name;
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
