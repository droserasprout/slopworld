using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    sealed class MarkdownInputController
    {
        readonly MarkdownSelection _selection;
        readonly Action<string> _openLocalLink;
        readonly MouseClickSequence _clicks = new MouseClickSequence();

        public MarkdownInputController(MarkdownSelection selection,
                                        Action<string> openLocalLink)
        {
            _selection = selection;
            _openLocalLink = openLocalLink;
        }

        public void Handle(Rect body, Vector2 scroll, List<LinkHit> links)
        {
            var e = Event.current;
            if (e == null) return;

            if (UiEvent.RawType(e) == EventType.KeyDown)
            {
                _selection.HandleKey(e);
                return;
            }

            if (e.button == 1 && UiEvent.RawType(e) == EventType.MouseDown &&
                body.Contains(e.mousePosition))
            {
                _selection.OpenMenu();
                e.Use();
                return;
            }

            if (e.button != 0) return;
            EventType type = UiEvent.RawType(e);
            if (type == EventType.MouseDown && body.Contains(e.mousePosition))
            {
                int clickCount = _clicks.Observe(e, Time.realtimeSinceStartup);
                if (clickCount >= 3)
                {
                    _selection.BeginMouse(body, e, clickCount, scroll);
                    _clicks.Reset();
                    return;
                }
                if (clickCount >= 2)
                {
                    _selection.BeginMouse(body, e, clickCount, scroll);
                    return;
                }
            }

            switch (type)
            {
                case EventType.MouseDown:
                    _selection.BeginMouse(body, e, 1, scroll);
                    return;

                case EventType.MouseDrag:
                    _selection.DragMouse(body, e, scroll);
                    return;

                case EventType.MouseUp:
                    _selection.EndMouse(body, e, scroll);
                    return;
            }
        }

        public void HandleLinks(Rect body, Vector2 scroll, List<LinkHit> links)
        {
            var e = Event.current;
            if (e == null) return;

            foreach (var hit in links)
            {
                var screen = new Rect(body.x + hit.Rect.x,
                    body.y + hit.Rect.y - scroll.y, hit.Rect.width, hit.Rect.height);
                if (Mouse.IsOver(screen))
                    TooltipHandler.TipRegion(screen, (hit.Url ?? hit.LocalPath) +
                        "\n\nCtrl+click to open it");
                if (UiEvent.RawType(e) == EventType.MouseDown && e.button == 0 && e.control &&
                    screen.Contains(e.mousePosition))
                {
                    if (hit.LocalPath != null) _openLocalLink(hit.LocalPath);
                    else if (!string.IsNullOrEmpty(hit.Url)) Application.OpenURL(hit.Url);
                    e.Use();
                    return;
                }
            }
        }

        // The containing fullscreen window can consume a mouse event before this view draws.
        // Keep the original gesture type so selection still sees MouseDown/Drag/Up, just as
        // the terminal pane does. UiEvent centralizes the Used/rawType recovery.
    }
}
