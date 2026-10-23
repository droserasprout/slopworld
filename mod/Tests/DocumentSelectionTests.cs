using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class DocumentSelectionTests
    {
        static readonly Rect Viewport = new Rect(0, 0, 1000, 500);

        static Event Mouse(float x, float y, bool shift = false) =>
            new Event { mousePosition = new Vector2(x, y), shift = shift };

        static TaskTextSelection.Line Line(string text, int start, float y)
        {
            var boundaries = TextElementLayout.Boundaries(text);
            var line = new TaskTextSelection.Line
            {
                Text = text,
                Start = start,
                End = start + text.Length,
                X = 0,
                Y = y,
                Height = 10,
                Width = boundaries.Length - 1,
                Boundaries = boundaries,
            };
            for (int i = 0; i <= text.Length; i++)
            {
                int at = System.Array.BinarySearch(boundaries, i);
                line.Edges.Add(at >= 0 ? at : ~at - 1);
            }
            return line;
        }

        public static void TaskSelectionPreservesSourceAcrossReflowAndCardGaps()
        {
            var selection = new TaskTextSelection();
            const string source = "ab cd\n\nnote\n";
            selection.Lines.Add(Line("ab cd", 0, 0));
            selection.Lines.Add(Line("note", 7, 30));
            selection.Lines.Add(Line("", 12, 40));
            selection.Rebuilt(source, 11, 1);
            AssertEx.Equal(
                "b cd\n\nnote",
                selection.SelectionText(),
                "reversed selection copies original card gap"
            );
            int anchor = selection.AnchorOffset,
                focus = selection.FocusOffset;
            selection.Lines.Clear();
            selection.Lines.Add(Line("ab ", 0, 0));
            selection.Lines.Add(Line("cd", 3, 10));
            selection.Lines.Add(Line("note", 7, 40));
            selection.Lines.Add(Line("", 12, 50));
            selection.Rebuilt(source, anchor, focus);
            AssertEx.Equal(
                "b cd\n\nnote",
                selection.SelectionText(),
                "wrapping preserves source offsets"
            );
            selection.SelectAll();
            AssertEx.Equal(
                source,
                selection.SelectionText(),
                "select all retains trailing newline"
            );
        }

        public static void TaskGesturesKeepUnicodeElementsAndReleaseCapture()
        {
            var selection = new TaskTextSelection();
            selection.Lines.Add(Line("😀e\u0301x", 0, 0));
            selection.Rebuilt("😀e\u0301x", 0, 0);
            selection.BeginMouse(Viewport, Mouse(0, 5), 1, new Vector2());
            AssertEx.True(GUIUtility.hotControl != 0, "drag captures mouse");
            selection.DragMouse(Viewport, Mouse(1.6f, 5), new Vector2());
            AssertEx.Equal(
                "😀e\u0301",
                selection.SelectionText(),
                "hit test never splits Unicode element"
            );
            selection.EndMouse(Viewport, Mouse(1.6f, 5), new Vector2());
            AssertEx.Equal(0, GUIUtility.hotControl, "mouseup releases capture");
            selection.BeginMouse(Viewport, Mouse(0, 5), 1, new Vector2());
            GUIUtility.hotControl = 999;
            selection.Clear();
            AssertEx.Equal(999, GUIUtility.hotControl, "cleanup cannot release another control");
            GUIUtility.hotControl = 0;
            selection.Clear();
            AssertEx.True(!selection.HasSelection, "repeated cleanup is harmless");
        }

        public static void ReadersRetainTheirClipboardPolicies()
        {
            var platform = Application.platform;
            var backend = DesktopClipboard.X11;
            using var completions = new BlockingCollection<Action>();
            var runner = new X11ClipboardTests.Runner(
                new ProcessRun { Started = true },
                new ProcessRun { Started = true },
                new ProcessRun { Started = true },
                new ProcessRun { Started = true }
            );
            void Complete()
            {
                Assert.That(completions.TryTake(out var complete, 5000), Is.True);
                complete();
            }
            string previous = GUIUtility.systemCopyBuffer;
            try
            {
                DaemonClient.Requests.Clear();
                Application.platform = RuntimePlatform.LinuxPlayer;
                DesktopClipboard.X11 = new X11Clipboard(runner, completions.Add);
                GUIUtility.systemCopyBuffer = "unchanged";
                var task = new TaskTextSelection();
                task.Lines.Add(Line("task", 0, 0));
                task.Rebuilt("task", 0, 0);
                task.BeginMouse(Viewport, Mouse(0, 5), 1, new Vector2());
                task.EndMouse(Viewport, Mouse(10, 5), new Vector2());
                Complete();
                AssertEx.Equal(
                    "-selection primary -in",
                    runner.Specs[0].Arguments,
                    "task mouse selection uses PRIMARY"
                );
                AssertEx.Equal(
                    "unchanged",
                    GUIUtility.systemCopyBuffer,
                    "task mouse selection preserves CLIPBOARD"
                );
                task.SelectAll();
                AssertEx.Equal(1, runner.Specs.Count, "task select all does not copy implicitly");
                task.OpenMenu();
                AssertEx.True(
                    !WorkspaceWindow.SelectionMenu.Options.Any(o => o.Label == "Paste"),
                    "task menu has no paste"
                );
                task.HandleKey(new Event { control = true, keyCode = KeyCode.C });
                Complete();
                AssertEx.Equal(
                    "-selection clipboard -in",
                    runner.Specs[1].Arguments,
                    "explicit task copy uses CLIPBOARD"
                );

                DaemonClient.Requests.Clear();
                var markdown = Markdown("hello world");
                markdown.BeginMouse(Viewport, Mouse(0, 5), 2, new Vector2());
                AssertEx.Equal(
                    "hello",
                    markdown.SelectionText(),
                    "Markdown double click selects word"
                );
                markdown.EndMouse(Viewport, Mouse(0, 5), new Vector2());
                Complete();
                AssertEx.Equal(
                    "-selection primary -in",
                    runner.Specs[2].Arguments,
                    "Markdown mouse selection uses PRIMARY"
                );
                markdown.Clear();
                markdown.SelectAll();
                Complete();
                AssertEx.Equal(
                    "-selection clipboard -in",
                    runner.Specs[3].Arguments,
                    "Markdown select all still copies"
                );
                markdown.OpenMenu();
                AssertEx.True(
                    WorkspaceWindow.SelectionMenu.Options.Any(o => o.Label == "Paste"),
                    "Markdown keeps agent paste"
                );
                markdown.Clear();
            }
            finally
            {
                Application.platform = platform;
                DesktopClipboard.X11 = backend;
                GUIUtility.systemCopyBuffer = previous;
                DaemonClient.Requests.Clear();
                WorkspaceWindow.SelectionMenu = null;
                GUIUtility.hotControl = 0;
            }
        }

        static MarkdownSelection Markdown(string text)
        {
            var styles = new StyleSet();
            var layout = new MarkdownTextLayout(
                styles,
                (run, width) => new ImageMetrics(1, 1)
            ).Wrap(new List<InlineRun> { new InlineRun { Text = text } }, 900, 0);
            var selection = new MarkdownSelection();
            selection.AttachStyles(styles);
            selection.Rebuild(
                new List<Placement>
                {
                    new Placement { Kind = PlacementKind.Text, Text = layout },
                }
            );
            return selection;
        }
    }
}
