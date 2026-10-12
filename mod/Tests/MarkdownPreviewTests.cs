using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class MarkdownPreviewTests
    {
        static readonly Rect Body = new Rect(0, 0, 600, 400);

        static MarkdownPreview Ready()
        {
            var preview = new MarkdownPreview("# Visible document", "old.md");
            preview.Opened();
            preview.Draw(Body);
            return preview;
        }

        static MarkdownPreview Replace(MarkdownPreview old, string name)
        {
            var preview = new MarkdownPreview("demo", "/work/demo/" + name, name, "/work/demo");
            preview.RetainVisibleDocument(old);
            old.Closed();
            preview.Opened();
            return preview;
        }

        public static void ReplacementKeepsVisibleDocumentUntilReadAndParseComplete()
        {
            DaemonClient.Requests.Clear();
            var old = Ready();
            var renderer = MarkdownRenderer.LastDrawn;
            var next = Replace(old, "next.md");
            EditorTrace.Draws.Clear();
            MarkdownInputController.Calls = 0;
            next.Draw(Body);
            Assert.That(MarkdownRenderer.LastDrawn, Is.SameAs(renderer));
            Assert.That(EditorTrace.Draws, Is.Empty, "no loading label during replacement");
            Assert.That(MarkdownInputController.Calls, Is.Zero, "old links and selection are inactive");
            int clears = renderer.Clears;
            DaemonClient.Requests.Last().Reply(new Wire.TextResult { Text = "# New document" });
            Assert.That(renderer.Clears, Is.EqualTo(clears + 1), "completion releases retained render state");
            next.Draw(Body);
            Assert.That(MarkdownRenderer.LastDrawn, Is.Not.SameAs(renderer));
            next.Closed();
        }

        public static void RapidSwitchTransfersVisibleDocumentAndRejectsStaleReplies()
        {
            DaemonClient.Requests.Clear();
            var first = Ready();
            var renderer = MarkdownRenderer.LastDrawn;
            var second = Replace(first, "second.md");
            var stale = DaemonClient.Requests.Last();
            var third = Replace(second, "third.md");
            stale.Reply(new Wire.TextResult { Text = "stale document" });
            stale.Fail("stale error");
            third.Draw(Body);
            Assert.That(MarkdownRenderer.LastDrawn, Is.SameAs(renderer));
            int clears = renderer.Clears;
            third.Closed();
            Assert.That(renderer.Clears, Is.EqualTo(clears + 1));
            third.Closed();
            Assert.That(renderer.Clears, Is.EqualTo(clears + 1), "repeated dismissal does not release twice");
            DaemonClient.Requests.Last().Reply(new Wire.TextResult { Text = "late document" });
            Assert.That(renderer.Clears, Is.EqualTo(clears + 1));
        }

        public static void ReadFailureReleasesPreviousDocumentAndShowsError()
        {
            DaemonClient.Requests.Clear();
            var old = Ready();
            var renderer = MarkdownRenderer.LastDrawn;
            var next = Replace(old, "missing.md");
            int clears = renderer.Clears;
            DaemonClient.Requests.Last().Fail("missing file");
            Assert.That(renderer.Clears, Is.EqualTo(clears + 1));
            EditorTrace.Draws.Clear();
            next.Draw(Body);
            Assert.That(EditorTrace.Draws.Select(draw => draw.Name), Is.EqualTo(new[] { "missing file" }));
            next.Closed();
        }
    }
}
