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
        int _pasteEpoch;

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
            var selectionAvailability = new SelectionCommandAvailability(canCopy: _state.Selection.HasSelection, canPaste: true, canSelectAll: true, canCut: false);
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

        static void ForwardCodexImagePaste(string name, long runId)
        {
            SessionHub.Instance.Terminal.SendKeysForRun(name, new[] { "C-v" }, false, (ulong)runId);
        }

        internal void PasteClipboard()
        {
            Flush();
            string name = _state.Name;
            var target = SessionHub.Instance.Get(name);
            long? runId = target?.RunId;
            int epoch = _pasteEpoch;
            if (CodexImagePaste && SessionHub.Instance.Capabilities.Clipboard)
            {
                // Codex's image handler claims Ctrl+V even when the clipboard only has text,
                // then reports "no image". Read the text format first and reserve Ctrl+V for an
                // image (or another non-text clipboard format).
                DaemonClient.Get<Wire.TextResult>(WireProtocol.Routes.ClipboardText,
                    j =>
                    {
                        if (!PasteTargetCurrent(name, runId, epoch)) return;
                        string text = j.Text;
                        if (!string.IsNullOrEmpty(text))
                            SessionHub.Instance.Terminal.PasteHostClipboard(name, text, (ulong)runId.Value, true);
                        else
                            ForwardCodexImagePaste(name, runId.Value);
                    },
                    _ => { if (PasteTargetCurrent(name, runId, epoch)) ForwardCodexImagePaste(name, runId.Value); });
                return;
            }
            if (!SessionHub.Instance.Capabilities.Clipboard)
            {
                DeliverLocal(name, runId, epoch);
                return;
            }
            string path = HostClipboardTextOnly ? WireProtocol.Routes.ClipboardText : WireProtocol.Routes.Clipboard;
            DaemonClient.Get<Wire.TextResult>(path,
                j => Deliver(name, j.Text, runId, epoch, false),
                _ => DeliverLocal(name, runId, epoch));
        }

        // Middle-click reads Wayland/X11 PRIMARY, not the ordinary CLIPBOARD. There is no
        // useful game-local fallback: Unity exposes the latter, if anything, and substituting
        // it would make a missing primary selection paste the wrong text.
        internal void PastePrimarySelection()
        {
            if (!SessionHub.Instance.Capabilities.Clipboard) return;
            Flush();
            string name = _state.Name;
            long? runId = SessionHub.Instance.Get(name)?.RunId;
            int epoch = _pasteEpoch;
            bool images = CodexImagePaste;
            string path = HostClipboardTextOnly
                ? WireProtocol.Routes.ClipboardPrimaryText
                : WireProtocol.Routes.ClipboardPrimary;
            DaemonClient.Get<Wire.TextResult>(path,
                j => Deliver(name, j.Text, runId, epoch, images),
                _ => { });
        }

        bool PasteTargetCurrent(string name, long? runId, int epoch) =>
            _opened && _pasteEpoch == epoch && _state.Name == name && runId.HasValue &&
            SessionHub.Instance.Get(name)?.RunId == runId;

        void Deliver(string name, string text, long? runId, int epoch, bool images)
        {
            if (!string.IsNullOrEmpty(text) && PasteTargetCurrent(name, runId, epoch))
                SessionHub.Instance.Terminal.PasteHostClipboard(name, text, (ulong)runId.Value, images);
        }

        void DeliverLocal(string name, long? runId, int epoch) =>
            Deliver(name, GUIUtility.systemCopyBuffer, runId, epoch, false);
    }
}
