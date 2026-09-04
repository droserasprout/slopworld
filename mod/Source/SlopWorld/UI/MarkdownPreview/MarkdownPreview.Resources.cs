using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SlopWorld
{
    sealed class MarkdownResourceStore
    {
        readonly MarkdownPathResolver _paths;
        readonly Dictionary<string, Texture2D> _images = new Dictionary<string, Texture2D>();
        readonly HashSet<string> _pendingImages = new HashSet<string>();
        readonly HashSet<string> _failedImages = new HashSet<string>();

        public MarkdownResourceStore(MarkdownPathResolver paths)
        {
            _paths = paths;
        }

        public Texture2D ImageFor(InlineRun image)
        {
            if (image == null || !image.IsImage || string.IsNullOrEmpty(image.ImagePath))
                return null;
            _images.TryGetValue(image.ImagePath, out var texture);
            return texture;
        }

        public void Request(List<MarkdownBlock> blocks, int request,
            Func<int, bool> isCurrent, Action invalidate)
        {
            foreach (var block in blocks ?? new List<MarkdownBlock>())
                RequestImages(block, request, isCurrent, invalidate);
            foreach (var block in blocks ?? new List<MarkdownBlock>())
                RequestHighlight(block, request, isCurrent, invalidate);
        }

        public void Clear()
        {
            foreach (var texture in _images.Values)
                if (texture != null) UnityEngine.Object.Destroy(texture);
            _images.Clear();
            _pendingImages.Clear();
            _failedImages.Clear();
        }

        void RequestHighlight(MarkdownBlock block, int request,
                              Func<int, bool> isCurrent, Action invalidate)
        {
            if (block == null) return;
            if (block.Kind == BlockKind.Code && !string.IsNullOrWhiteSpace(block.Info))
            {
                string body = "{" + $"\"text\":{JVal.Q(block.Code ?? "")}," +
                    $"\"language\":{JVal.Q(block.Info)}" + "}";
                SlopClient.Post("/api/highlight", body,
                    j =>
                    {
                        if (!isCurrent(request)) return;
                        block.Highlighted = j["text"].AsString();
                        invalidate();
                    },
                    _ => { });
            }
            if (block.Children != null)
                foreach (var child in block.Children)
                    RequestHighlight(child, request, isCurrent, invalidate);
        }

        void RequestImages(MarkdownBlock block, int request,
                           Func<int, bool> isCurrent, Action invalidate)
        {
            if (block == null) return;
            RequestImages(block.Runs, request, isCurrent, invalidate);
            if (block.Rows != null)
                foreach (var row in block.Rows)
                    foreach (var cell in row.Cells)
                        RequestImages(cell, request, isCurrent, invalidate);
            if (block.Children != null)
                foreach (var child in block.Children)
                    RequestImages(child, request, isCurrent, invalidate);
        }

        void RequestImages(List<InlineRun> runs, int request,
                           Func<int, bool> isCurrent, Action invalidate)
        {
            foreach (var run in runs ?? new List<InlineRun>())
            {
                if (!run.IsImage || string.IsNullOrWhiteSpace(run.ImagePath)) continue;
                string path = _paths.ResolveImagePath(run.ImagePath);
                if (path == null)
                {
                    run.ImagePath = null;
                    run.ImageFailed = true;
                    continue;
                }
                run.ImagePath = path;
                if (_images.ContainsKey(path) || _pendingImages.Contains(path) ||
                    _failedImages.Contains(path)) continue;

                _pendingImages.Add(path);
                SlopClient.Send("GET", "/api/image?path=" + Uri.EscapeDataString(path), null,
                    j =>
                    {
                        if (!isCurrent(request)) return;
                        _pendingImages.Remove(path);
                        try
                        {
                            var bytes = Convert.FromBase64String(j["data"].AsString());
                            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                            if (!texture.LoadImage(bytes, true))
                                throw new InvalidDataException("Unity could not decode the image");
                            texture.name = "SlopWorld Markdown " + Path.GetFileName(path);
                            texture.hideFlags = HideFlags.HideAndDontSave;
                            _images[path] = texture;
                            invalidate();
                        }
                        catch
                        {
                            run.ImageFailed = true;
                            _failedImages.Add(path);
                        }
                    },
                    msg =>
                    {
                        if (!isCurrent(request)) return;
                        _pendingImages.Remove(path);
                        run.ImageFailed = true;
                        _failedImages.Add(path);
                    });
            }
        }
    }
}
