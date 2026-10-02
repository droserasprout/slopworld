using System;
using System.IO;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // A file reader owns its decoded Unity texture only while its content pane is open.
    // The daemon bounds the byte response; callbacks from an earlier opening cannot replace
    // a newer image or resurrect a texture after the pane closes.
    public sealed class ImagePreview : ContentView
    {
        readonly string _path;
        readonly string _name;
        readonly string _root;
        readonly SmoothScroll _scroll = new SmoothScroll();
        Texture2D _texture;
        string _error;
        int _request;
        bool _actualSize;

        public ImagePreview(string path, string name, string root = null)
        {
            _path = path ?? "";
            _root = root;
            _name = string.IsNullOrEmpty(name) ? Path.GetFileName(_path) : name;
        }

        public override string Title => "Preview · " + _name;

        public override void Opened()
        {
            int request = ++_request;
            _error = null;
            ReleaseTexture();
            _scroll.JumpTo(Vector2.zero);
            DaemonClient.Get<Wire.ImageResult>(WireProtocol.Routes.Image +
                "?path=" + Uri.EscapeDataString(_path) +
                (_root == null ? "" : "&root=" + Uri.EscapeDataString(_root)), result =>
                {
                    if (request != _request) return;
                    Texture2D texture = null;
                    try
                    {
                        var bytes = result.Data.ToByteArray();
                        if (!MarkdownImageHeader.TrySize(bytes, out int width, out int height))
                            throw new InvalidDataException("Image dimensions exceed the preview limit or are invalid");
                        texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (!texture.LoadImage(bytes, true) || texture.width != width || texture.height != height)
                            throw new InvalidDataException("Unity could not decode the image");
                        texture.name = "SlopWorld image " + _name;
                        texture.hideFlags = HideFlags.HideAndDontSave;
                        _texture = texture;
                        texture = null;
                    }
                    catch (Exception e)
                    {
                        _error = "Could not display image: " + e.Message;
                    }
                    finally
                    {
                        if (texture != null) UnityEngine.Object.Destroy(texture);
                    }
                }, message =>
                {
                    if (request == _request) _error = message;
                });
        }

        public override void Closed()
        {
            ++_request;
            ReleaseTexture();
        }

        void ReleaseTexture()
        {
            if (_texture == null) return;
            UnityEngine.Object.Destroy(_texture);
            _texture = null;
        }

        public override void Draw(Rect body)
        {
            if (_texture == null)
            {
                var oldColor = GUI.color;
                GUI.color = _error == null ? UiTheme.Dim : UiTheme.Bad;
                Widgets.Label(body, _error ?? "Loading image");
                GUI.color = oldColor;
                return;
            }

            var toolbar = new Rect(body.x, body.y, body.width, 30f);
            if (Widgets.ButtonText(new Rect(toolbar.x, toolbar.y, 90f, 26f),
                _actualSize ? "Fit" : "100%"))
            {
                _actualSize = !_actualSize;
                _scroll.JumpTo(Vector2.zero);
            }
            var oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(toolbar.x + 100f, toolbar.y, Mathf.Max(0f, toolbar.width - 100f), 26f),
                _texture.width + " × " + _texture.height);
            Text.Anchor = oldAnchor;

            var viewport = new Rect(body.x, body.y + toolbar.height,
                body.width, Mathf.Max(0f, body.height - toolbar.height));
            if (viewport.width <= 0f || viewport.height <= 0f) return;
            float scale = _actualSize ? 1f : Mathf.Min(1f,
                Mathf.Min(viewport.width / _texture.width, viewport.height / _texture.height));
            float width = _texture.width * scale;
            float height = _texture.height * scale;
            var content = new Rect(0f, 0f, Mathf.Max(viewport.width, width),
                Mathf.Max(viewport.height, height));
            using (_scroll.Scope(viewport, content))
            {
                if (Event.current.type == EventType.Repaint)
                    GUI.DrawTexture(new Rect((content.width - width) / 2f,
                        (content.height - height) / 2f, width, height), _texture);
            }
        }
    }
}
