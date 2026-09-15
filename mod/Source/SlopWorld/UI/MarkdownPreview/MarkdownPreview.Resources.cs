using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SlopWorld
{
    sealed class MarkdownResourceStore
    {
        readonly MarkdownPathResolver _paths;
        sealed class ImageResource
        {
            public Texture2D Texture;
            public bool Pending;
            public bool Failed;
        }

        readonly Dictionary<string, ImageResource> _images =
            new Dictionary<string, ImageResource>();
        int _generation;

        public MarkdownResourceStore(MarkdownPathResolver paths)
        {
            _paths = paths;
        }

        public Texture2D ImageFor(InlineRun image)
        {
            if (image == null || !image.IsImage || string.IsNullOrEmpty(image.ImagePath))
                return null;
            if (!_images.TryGetValue(image.ImagePath, out var resource)) return null;
            image.ImageFailed = resource.Failed;
            return resource.Texture;
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
                if (texture.Texture != null) UnityEngine.Object.Destroy(texture.Texture);
            _images.Clear();
            _generation++;
        }

        void RequestHighlight(MarkdownBlock block, int request,
                              Func<int, bool> isCurrent, Action invalidate)
        {
            if (block == null) return;
            if (block.Kind == BlockKind.Code && !string.IsNullOrWhiteSpace(block.Info))
            {
                string body = "{" + $"\"text\":{JVal.Q(block.Code ?? "")}," +
                    $"\"language\":{JVal.Q(block.Info)}" + "}";
                DaemonClient.Post(WireProtocol.Routes.Highlight, body,
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
                if (!_images.TryGetValue(path, out var resource))
                {
                    resource = new ImageResource();
                    _images.Add(path, resource);
                }
                run.ImageFailed = resource.Failed;
                if (resource.Texture != null || resource.Pending || resource.Failed) continue;

                resource.Pending = true;
                int generation = _generation;
                DaemonClient.Send("GET", WireProtocol.Routes.Image + "?path=" + Uri.EscapeDataString(path), null,
                    j =>
                    {
                        if (generation != _generation || !isCurrent(request)) return;
                        resource.Pending = false;
                        Texture2D texture = null;
                        try
                        {
                            var bytes = Convert.FromBase64String(j["data"].AsString());
                            // Keep local ownership until the fully configured texture enters
                            // the store. Every decode or setup failure must release native data.
                            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                            if (!texture.LoadImage(bytes, true))
                                throw new InvalidDataException("Unity could not decode the image");
                            texture.name = "SlopWorld Markdown " + Path.GetFileName(path);
                            texture.hideFlags = HideFlags.HideAndDontSave;
                            if (resource.Texture != null)
                                UnityEngine.Object.Destroy(resource.Texture);
                            resource.Texture = texture;
                            resource.Failed = false;
                            texture = null;         // ownership moved into _images
                            invalidate();
                        }
                        catch
                        {
                            if (texture != null) UnityEngine.Object.Destroy(texture);
                            resource.Failed = true;
                            invalidate();
                        }
                    },
                    msg =>
                    {
                        if (generation != _generation || !isCurrent(request)) return;
                        resource.Pending = false;
                        resource.Failed = true;
                        invalidate();
                    });
            }
        }
    }
}
