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
            public bool HasSize;
            public int Width = 320;
            public int Height = 180;
        }

        readonly Dictionary<string, ImageResource> _images =
            new Dictionary<string, ImageResource>();
        readonly MarkdownImageBudget _budget = new MarkdownImageBudget();
        readonly HashSet<string> _wanted = new HashSet<string>();
        readonly List<string> _wantedOrder = new List<string>();
        sealed class ImagePlacement
        {
            public InlineRun Run;
            public float Y;
            public float Height;
            public float PrefixBottom;
        }
        readonly List<ImagePlacement> _imagePlacements = new List<ImagePlacement>();
        int _layoutGeneration = -1;
        long _plannedBytes;
        int _generation;
        int _highlightGeneration;

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

        public ImageMetrics ImageSizeFor(InlineRun image)
        {
            return image.ImagePath != null && _images.TryGetValue(image.ImagePath, out var resource)
                ? new ImageMetrics(resource.Width, resource.Height) : new ImageMetrics(320f, 180f);
        }

        public void Request(List<MarkdownBlock> blocks, int request,
            Func<int, bool> isCurrent, Action invalidate)
        {
            foreach (var block in blocks ?? new List<MarkdownBlock>())
                RegisterImages(block);
            RefreshHighlight(blocks, request, isCurrent, invalidate);
        }

        public void RefreshHighlight(List<MarkdownBlock> blocks, int request,
            Func<int, bool> isCurrent, Action invalidate)
        {
            int generation = ++_highlightGeneration;
            foreach (var block in blocks ?? new List<MarkdownBlock>())
                RequestHighlight(block, request, isCurrent, invalidate, generation);
            invalidate();
        }

        public void Clear()
        {
            foreach (var texture in _images.Values)
                if (texture.Texture != null) UnityEngine.Object.Destroy(texture.Texture);
            _images.Clear();
            _budget.Clear();
            _imagePlacements.Clear();
            _layoutGeneration = -1;
            _wanted.Clear();
            _wantedOrder.Clear();
            _generation++;
            _highlightGeneration++;
        }

        void RequestHighlight(MarkdownBlock block, int request,
                              Func<int, bool> isCurrent, Action invalidate, int generation)
        {
            if (block == null) return;
            if (block.Kind == BlockKind.Code && !string.IsNullOrWhiteSpace(block.Info))
            {
                block.Highlighted = null;
                var body = CodeHighlight.Request(block.Code ?? "", block.Info);
                DaemonClient.Post<Wire.TextResult>(WireProtocol.Routes.Highlight, body,
                    j =>
                    {
                        if (!isCurrent(request) || generation != _highlightGeneration) return;
                        block.Highlighted = j.Text;
                        invalidate();
                    },
                    _ => { });
            }
            if (block.Children != null)
                foreach (var child in block.Children)
                    RequestHighlight(child, request, isCurrent, invalidate, generation);
        }

        void RegisterImages(MarkdownBlock block)
        {
            if (block == null) return;
            RegisterImages(block.Runs);
            if (block.Rows != null)
                foreach (var row in block.Rows)
                    foreach (var cell in row.Cells) RegisterImages(cell);
            if (block.Children != null)
                foreach (var child in block.Children) RegisterImages(child);
        }

        void RegisterImages(List<InlineRun> runs)
        {
            if (runs == null) return;
            foreach (var run in runs)
            {
                if (!run.IsImage || string.IsNullOrWhiteSpace(run.ImagePath)) continue;
                string path = _paths.ResolveImagePath(run.ImagePath);
                run.ImagePath = path;
                run.ImageFailed = path == null;
                if (path != null && !_images.ContainsKey(path)) _images.Add(path, new ImageResource());
            }
        }

        // Placements are sorted by Y. Register only the viewport plus one screen on either
        // side, and keep dimensions after eviction so scrolling cannot change layout height.
        public void RequestVisible(List<Placement> placements, int layoutGeneration, float top, float height, int request,
            Func<int, bool> isCurrent, Action invalidate)
        {
            EnsureImageIndex(placements, layoutGeneration);
            _wanted.Clear();
            _wantedOrder.Clear();
            _plannedBytes = 0;
            // Visible images take priority over speculative images on adjacent screens.
            WantRange(top, top + height);
            WantRange(top - height, top + height * 2f);
            foreach (var pair in _images)
            {
                var resource = pair.Value;
                if (_wanted.Contains(pair.Key) || resource.Texture == null) continue;
                UnityEngine.Object.Destroy(resource.Texture);
                resource.Texture = null;
                _budget.Release(pair.Key);
            }
            foreach (string path in _wantedOrder)
            {
                var resource = _images[path];
                if (resource.Texture != null || resource.Pending || resource.Failed || !_budget.TryReserve(path)) continue;
                RequestImage(path, resource, request, isCurrent, invalidate);
            }
        }

        void EnsureImageIndex(List<Placement> placements, int generation)
        {
            if (_layoutGeneration == generation) return;
            _imagePlacements.Clear();
            foreach (var placement in placements)
            {
                Index(placement.Image, placement.Y, placement.Height);
                Index(placement.Text, placement.Y);
                if (placement.Table != null)
                    foreach (var row in placement.Table.Rows)
                        foreach (var cell in row.Cells) Index(cell, placement.Y + row.Offset);
            }
            _imagePlacements.Sort((a, b) => a.Y.CompareTo(b.Y));
            float bottom = float.MinValue;
            foreach (var image in _imagePlacements)
            {
                bottom = Math.Max(bottom, image.Y + image.Height);
                image.PrefixBottom = bottom;
            }
            _layoutGeneration = generation;
        }

        void Index(TextLayout text, float y)
        {
            if (text == null) return;
            foreach (var line in text.Lines)
                foreach (var piece in line.Pieces)
                    Index(piece.Run, y + line.Offset + piece.OffsetY, piece.Height);
        }

        void Index(InlineRun run, float y, float height)
        {
            if (run != null && run.IsImage && run.ImagePath != null)
                _imagePlacements.Add(new ImagePlacement { Run = run, Y = y, Height = height });
        }

        void WantRange(float first, float last)
        {
            int low = 0, high = _imagePlacements.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (_imagePlacements[middle].PrefixBottom < first) low = middle + 1;
                else high = middle;
            }
            for (int i = low; i < _imagePlacements.Count; i++)
            {
                var image = _imagePlacements[i];
                if (image.Y > last) break;
                if (image.Y + image.Height < first) continue;
                string path = image.Run.ImagePath;
                if (_wanted.Contains(path) || !_images.TryGetValue(path, out var resource) || resource.Failed) continue;
                long bytes = resource.HasSize ? (long)resource.Width * resource.Height * 4 : MarkdownImageBudget.ImageBytes;
                if (_plannedBytes + bytes > MarkdownImageBudget.TotalBytes) continue;
                _plannedBytes += bytes;
                _wanted.Add(path);
                _wantedOrder.Add(path);
            }
        }

        void RequestImage(string path, ImageResource resource, int request,
            Func<int, bool> isCurrent, Action invalidate)
        {
            resource.Pending = true;
            int generation = _generation;
            DaemonClient.Send<Wire.ImageResult>("GET", WireProtocol.Routes.Image + _paths.ScopedQuery(path), null,
                j =>
                {
                    if (generation != _generation || !isCurrent(request)) return;
                    resource.Pending = false;
                    // A fast scroll may retire this request before its response arrives.
                    if (!_wanted.Contains(path)) { _budget.Release(path); return; }
                    Texture2D texture = null;
                    try
                    {
                        var bytes = j.Data.ToByteArray();
                        if (!MarkdownImageHeader.TrySize(bytes, out int width, out int height))
                            throw new InvalidDataException("Image dimensions exceed the Markdown preview limit or are invalid");
                        texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (!texture.LoadImage(bytes, true) || texture.width != width || texture.height != height)
                            throw new InvalidDataException("Unity could not decode the image dimensions");
                        if (!_budget.Complete(path, (long)width * height * 4))
                            throw new InvalidDataException("Image exceeds the Markdown memory budget");
                        texture.name = "SlopWorld Markdown " + Path.GetFileName(path);
                        texture.hideFlags = HideFlags.HideAndDontSave;
                        resource.Texture = texture;
                        resource.HasSize = true;
                        resource.Width = width;
                        resource.Height = height;
                        resource.Failed = false;
                        texture = null;
                        invalidate();
                    }
                    catch
                    {
                        if (texture != null) UnityEngine.Object.Destroy(texture);
                        _budget.Release(path);
                        resource.Failed = true;
                        invalidate();
                    }
                },
                msg =>
                {
                    if (generation != _generation || !isCurrent(request)) return;
                    resource.Pending = false;
                    _budget.Release(path);
                    resource.Failed = true;
                    invalidate();
                });
        }
    }
}
