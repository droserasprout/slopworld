using System;
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
            if (buf != null)
                CopyText(SelectionText(buf));
        }

        internal void CopyPrimarySelection()
        {
            var buf = DisplayedBuf();
            if (buf != null)
                CopyPrimaryText(SelectionText(buf));
        }

        // Trailing newlines go: a screen is padded to its row count, so an app half a screen
        // tall would copy the blank half with it.
        void SelectAll()
        {
            var buf = DisplayedBuf();
            if (buf == null || buf.Lines.Length == 0)
                return;

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

        // Clipboard transport chooses the game desktop on Linux; Unity's buffer alone
        // is unreliable under some compositors. Application OSC 52 has a separate owner.
        void CopyText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            DesktopClipboard.Copy(text, null, msg => Log.Warning($"[SlopWorld] clipboard: {msg}"));
        }

        void CopyPrimaryText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            DesktopClipboard.CopyPrimary(
                text,
                null,
                msg => Log.Warning($"[SlopWorld] primary selection: {msg}")
            );
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
                canCopy: _state.Selection.HasSelection,
                canPaste: true,
                canSelectAll: true,
                canCut: false
            );
            SelectionCommands.AddCopy(options, selectionAvailability, CopySelection);
            if (_state.Selection.HasSelection)
            {
                // Retain the displayed selection while the menu is open and live frames advance.
                var buf = DisplayedBuf();
                string text = buf == null ? "" : SelectionText(buf);
                if (!string.IsNullOrEmpty(text))
                    options.Add(new UiSubmenu("Save as", () => SaveSelectionOptions(text)));
            }
            if (info != null)
            {
                string name = _state.Name;
                options.Add(
                    new FloatMenuOption(
                        "Label",
                        () =>
                        {
                            var current = SessionHub.Instance.Get(name);
                            if (current != null)
                                LabelDialog.Open(name, current.Label);
                        }
                    )
                );
            }
            SelectionCommands.AddPaste(
                options,
                selectionAvailability,
                () =>
                {
                    JumpToLive();
                    PasteClipboard();
                }
            );
            var breadcrumbs = AllBreadcrumbs();
            var breadcrumbMenu = new UiSubmenu("Breadcrumbs", () => BreadcrumbOptions(breadcrumbs));
            breadcrumbMenu.Disabled = info == null || !info.Alive || breadcrumbs.Count == 0;
            options.Add(breadcrumbMenu);
            SelectionCommands.AddSelectAll(options, selectionAvailability, SelectAll);
            options.Add(
                new UiSubmenu(
                    "Open beside",
                    () =>
                    {
                        var sessions = new List<FloatMenuOption>();
                        foreach (string session in WorkspaceWindow.OpenBesideOrder())
                        {
                            if (session == _state.Name)
                                continue;
                            string picked = session;
                            sessions.Add(
                                new FloatMenuOption(picked, () => WorkspaceWindow.OpenSplit(picked))
                            );
                        }
                        return sessions;
                    }
                )
            );
            options.Add(new FloatMenuOption("Close pane", Close));

            if (_scrollOff > 0)
                options.Add(
                    new FloatMenuOption(
                        "Back to the live view",
                        () =>
                        {
                            JumpToLive();
                            ClearSelection();
                        }
                    )
                );

            WorkspaceWindow.OpenOverPane(new UiMenu(options));
        }

        static List<FloatMenuOption> SaveSelectionOptions(string text) =>
            new List<FloatMenuOption>
            {
                SaveSelectionOption("Prompt", LibraryItemKind.Prompt, text),
                SaveSelectionOption("Breadcrumb", LibraryItemKind.Breadcrumb, text),
                SaveSelectionOption("Shell script", LibraryItemKind.Shell, text),
                SaveSelectionOption("File action", LibraryItemKind.FileAction, text),
            };

        static FloatMenuOption SaveSelectionOption(
            string label,
            LibraryItemKind kind,
            string text
        ) =>
            new FloatMenuOption(
                label,
                () => WorkspaceWindow.OpenOverPane(EditLibraryItemDialog.New(kind, text))
            );

        static List<string> AllBreadcrumbs() =>
            SessionHub
                .Instance.Library.Where(s =>
                    s.Kind == LibraryItemKind.Breadcrumb && (!s.Builtin || !Settings.GrandmaMode)
                )
                .Select(s => s.Name)
                .ToList();

        List<FloatMenuOption> BreadcrumbOptions(List<string> names)
        {
            var options = new List<FloatMenuOption>();
            foreach (string name in names)
            {
                string picked = name;
                options.Add(
                    new FloatMenuOption(
                        picked,
                        () =>
                        {
                            JumpToLive();
                            SessionHub.Instance.Terminal.PasteBreadcrumb(
                                _state.Name,
                                picked,
                                Patch_LoadingTips.RandomTips(Patch_LoadingTips.TipBatch)
                            );
                        }
                    )
                );
            }
            return options;
        }

        // Missing session metadata is conservative: a stale snapshot must not send an image to
        // a host shell.
        bool HostClipboardTextOnly => SessionHub.Instance.Get(_state.Name)?.Host != false;

        // Codex accepts image attachments. Linux uploads binary clipboard data and
        // pastes a staged path. macOS uses local text paste only.
        bool CodexImagePaste =>
            !HostClipboardTextOnly
            && SessionHub.Instance.Get(_state.Name)?.CommandPreset == "codex";

        internal void PasteClipboard()
        {
            Flush();
            string name = _state.Name;
            var target = SessionHub.Instance.Get(name);
            long? runId = target?.RunId;
            int epoch = _pasteEpoch;
            if (DesktopClipboard.UsesX11)
            {
                bool images = CodexImagePaste;
                DesktopClipboard.ReadLocalPaste(
                    text => Deliver(name, text, runId, epoch),
                    images
                        ? bytes =>
                        {
                            if (PasteTargetCurrent(name, runId, epoch))
                                SessionHub.Instance.Terminal.PasteImage(
                                    name,
                                    bytes,
                                    (ulong)runId.Value
                                );
                        }
                        : (Action<byte[]>)null,
                    error =>
                    {
                        if (!PasteTargetCurrent(name, runId, epoch))
                            return;
                        if (images)
                            Log.Warning("[SlopWorld] image paste: " + error);
                        else
                            DeliverLocal(name, runId, epoch);
                    }
                );
                return;
            }
            DeliverLocal(name, runId, epoch);
        }

        // Middle-click reads X11 PRIMARY, not the ordinary CLIPBOARD. There is no
        // useful game-local fallback: Unity exposes the latter, if anything, and substituting
        // it would make a missing primary selection paste the wrong text.
        internal void PastePrimarySelection()
        {
            if (DesktopClipboard.UsesX11)
            {
                Flush();
                string localName = _state.Name;
                long? localRun = SessionHub.Instance.Get(localName)?.RunId;
                int localEpoch = _pasteEpoch;
                DesktopClipboard.ReadLocal(
                    true,
                    text => Deliver(localName, text, localRun, localEpoch)
                );
                return;
            }
        }

        bool PasteTargetCurrent(string name, long? runId, int epoch) =>
            _opened
            && _pasteEpoch == epoch
            && _state.Name == name
            && runId.HasValue
            && SessionHub.Instance.Get(name)?.RunId == runId;

        void Deliver(string name, string text, long? runId, int epoch)
        {
            if (!string.IsNullOrEmpty(text) && PasteTargetCurrent(name, runId, epoch))
                SessionHub.Instance.Terminal.PasteForRun(name, text, (ulong)runId.Value);
        }

        void DeliverLocal(string name, long? runId, int epoch) =>
            Deliver(name, GUIUtility.systemCopyBuffer, runId, epoch);
    }
}
