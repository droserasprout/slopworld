using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Native Markdown content view. Parsing, resource loading, layout, rendering, scrolling,
    // and selection are separate collaborators so this class only coordinates their lifecycle.
    public sealed class MarkdownPreview : ContentView
    {
        readonly string _project;
        readonly string _path;
        readonly string _name;
        readonly MarkdownPathResolver _paths;
        readonly MarkdownDocumentParser _parser;
        readonly MarkdownResourceStore _resources;
        readonly MarkdownLayoutEngine _layout;
        readonly MarkdownSelection _selection;
        readonly MarkdownRenderer _renderer;
        readonly MarkdownScrollView _scroll;
        readonly MarkdownInputController _input;
        string _inlineText;
        List<MarkdownBlock> _blocks;
        string _error;
        bool _loading;
        int _request;
        float _viewportWidth = -1f;
        float _viewportHeight = -1f;
        int _metricsRevision = int.MinValue;

        public MarkdownPreview(string project, string path, string name)
            : this(project, path,
                string.IsNullOrEmpty(name) ? System.IO.Path.GetFileName(path) : name, null)
        {
        }

        // Settings pages can preview a document before it exists on disk. Inline documents
        // share the normal Markdown pipeline but never cross the daemon's file-read boundary.
        public MarkdownPreview(string text, string name)
            : this("", "", string.IsNullOrEmpty(name) ? "preview.md" : name, text ?? "")
        {
        }

        MarkdownPreview(string project, string path, string name, string inlineText)
        {
            _project = project ?? "";
            _path = path ?? "";
            _name = name;
            _inlineText = inlineText;
            _paths = new MarkdownPathResolver(_project, _path);
            _parser = new MarkdownDocumentParser(_paths);
            _resources = new MarkdownResourceStore(_paths);
            _layout = new MarkdownLayoutEngine(_resources);
            _selection = new MarkdownSelection();
            _renderer = new MarkdownRenderer(_resources);
            _scroll = new MarkdownScrollView();
            _input = new MarkdownInputController(_selection, OpenLocalLink);
        }

        public string Path => _path;
        public string Project => _project;
        public override string Title => "Preview · " + _name;

        public static void Open(string project, string path, string name) =>
            TerminalWindow.OpenContent(new MarkdownPreview(project, path, name));

        public static bool IsShowing(string path) =>
            TerminalWindow.ShowingAs<MarkdownPreview>()?.Path == path;

        public static void CloseIfShowing()
        {
            if (TerminalWindow.ShowingAs<MarkdownPreview>() == null) return;
            Find.WindowStack?.WindowOfType<TerminalWindow>()?.Leave();
        }

        public override void Opened()
        {
            BeginLoad();
            int request = ++_request;
            if (_inlineText != null)
            {
                ApplyText(_inlineText, request);
                return;
            }

            DaemonClient.Get(WireContract.Routes.Read + "?path=" + Uri.EscapeDataString(_path),
                j =>
                {
                    if (!IsCurrent(request)) return;
                    ApplyText(j["text"].AsString(), request);
                },
                msg =>
                {
                    if (!IsCurrent(request)) return;
                    _loading = false;
                    _error = msg;
                });
        }

        public void SetInlineText(string text)
        {
            if (_inlineText == null || _inlineText == text) return;
            _inlineText = text ?? "";
            BeginLoad();
            ApplyText(_inlineText, ++_request);
        }

        public override void Closed()
        {
            ++_request;
            _scroll.Reset();
            _selection.Clear();
            _resources.Clear();
            _layout.Clear();
            _renderer.ClearLinks();
        }

        public override void Draw(Rect body)
        {
            if (_loading || _blocks == null)
            {
                Status(body, _error ?? "Loading Markdown…", _error == null
                    ? UiTheme.Dim : UiTheme.Bad);
                return;
            }

            _layout.EnsureStyles();
            _selection.AttachStyles(_layout.Styles);

            // A scrolling document normally keeps the narrower width from the prior frame.
            // Renegotiate the scrollbar only when the document or viewport changed; otherwise
            // wheel movement does not remeasure the whole file.
            bool viewportChanged = !Mathf.Approximately(body.width, _viewportWidth) ||
                !Mathf.Approximately(body.height, _viewportHeight);
            int metricsRevision = UiMetrics.Revision;
            bool metricsChanged = metricsRevision != _metricsRevision;
            if (_layout.Width < 0f || viewportChanged || metricsChanged)
            {
                // Discard stale hit regions before input can use the new layout.
                _renderer.ClearLinks();
                if (metricsChanged) _layout.Invalidate();
                _viewportWidth = body.width;
                _viewportHeight = body.height;
                _layout.Reflow(_blocks, body.width);
                _selection.Rebuild(_layout.Placements);
                float settledWidth = _layout.Height > body.height
                    ? Mathf.Max(1f, body.width - UiTheme.ScrollbarW)
                    : body.width;
                _layout.Reflow(_blocks, settledWidth);
                _selection.Rebuild(_layout.Placements);
                _metricsRevision = metricsRevision;
            }

            _scroll.Draw(body, _layout.Width, _layout.Height,
                (clipTop, clipBottom) => _renderer.Draw(_layout.Placements, _selection,
                    clipTop, clipBottom));
            _input.HandleLinks(body, _scroll.Position, _renderer.Links);
            _input.Handle(body, _scroll.Position, _renderer.Links);
        }

        void BeginLoad()
        {
            _loading = true;
            _error = null;
            _blocks = null;
            _resources.Clear();
            _layout.Clear();
            _selection.Clear();
            _renderer.ClearLinks();
        }

        void ApplyText(string text, int request)
        {
            try
            {
                _blocks = _parser.Parse(text);
                _resources.Request(_blocks, request, IsCurrent, _layout.Invalidate);
                _loading = false;
                _error = null;
                _scroll.Reset();
            }
            catch (Exception e)
            {
                _loading = false;
                _error = "Markdown could not be parsed: " + e.Message;
            }
        }

        bool IsCurrent(int request) => request == _request;

        void OpenLocalLink(string path)
        {
            string name = System.IO.Path.GetFileName(path);
            if (!FilesView.IsText(name))
            {
                UiLayout.Fail("binary local links are not previewable");
                return;
            }
            if (FilesView.IsMarkdown(name))
            {
                FilesView.ViewFile(_project, path, name);
                return;
            }

            FilesView.ViewFile(_project, path, "link-" + name);
        }

        static void Status(Rect body, string text, Color color)
        {
            var old = GUI.color;
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = color;
                Widgets.Label(body, text);
            }
            finally
            {
                GUI.color = old;
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
            }
        }
    }
}
