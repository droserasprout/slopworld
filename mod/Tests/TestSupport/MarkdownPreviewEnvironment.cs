using System;
using System.Collections.Generic;
using UnityEngine;

namespace Verse
{
    static class Widgets
    {
        public static void Label(Rect body, string text) => SlopWorld.EditorTrace.Record(text, body);
    }
    static partial class Text { public static TextAnchor Anchor; }
    static class MarkdownWindowLookup
    {
        public static T WindowOfType<T>(this WindowStack stack) where T : class => null;
    }
}

namespace SlopWorld
{
    partial class TerminalWindow
    {
        public static IContentView Showing;
        public static T ShowingAs<T>() where T : class => Showing as T;
        public static void OpenContent(IContentView view) { Showing = view; }
        public void Leave() { Showing = null; }
    }
    static class FilesView
    {
        public static bool IsText(string name) => FileTypes.IsText(name);
        public static bool IsImage(string name) => FileTypes.IsImage(name);
        public static void ViewFile(string project, string path, string name, string previewRoot = null) { }
    }

    // Substitute drawing and input only; preview lifecycle, reads, parsing, resources,
    // selection and layout remain production code.
    sealed class MarkdownRenderer
    {
        public static MarkdownRenderer LastDrawn;
        public readonly List<LinkHit> Links = new List<LinkHit>();
        public int Clears;
        public MarkdownRenderer(MarkdownResourceStore resources) { }
        public void ClearLinks() { Links.Clear(); Clears++; }
        public void Draw(List<Placement> placements, int generation, MarkdownSelection selection,
            float top, float bottom)
        { LastDrawn = this; }
    }
    sealed class MarkdownScrollView
    {
        public Vector2 Position => new Vector2();
        public void Reset() { }
        public void Draw(Rect body, float width, float height, Action<float, float> draw) => draw(0, body.height);
    }
    sealed class MarkdownInputController
    {
        public static int Calls;
        public MarkdownInputController(MarkdownSelection selection, Action<string> openLink) { }
        public bool HandleLinks(Rect body, Vector2 scroll, List<LinkHit> links) { Calls++; return false; }
        public void Handle(Rect body, Vector2 scroll) { Calls++; }
    }
}
