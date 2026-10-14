using System;
using NUnit.Framework;
using UnityEngine;
using UnityStubs.Gui;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class NativeTextLayoutTests
    {
        [TestCase(1, 4)]
        [TestCase(4, 1)]
        public void PasteReplacesEitherSelectionDirectionAndCollapsesAfterUnicode(
            int cursor,
            int select
        )
        {
            var editor = new TextEditor
            {
                text = "abcdef",
                cursorIndex = cursor,
                selectIndex = select,
            };
            NativeTextLayout.ReplaceSelection(editor, "🙂");
            Assert.That(editor.text, Is.EqualTo("a🙂ef"));
            Assert.That(editor.cursorIndex, Is.EqualTo(3));
            Assert.That(editor.selectIndex, Is.EqualTo(3));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SelectionHitTestRestoresBothEndpointsEvenWhenNativeHitTestThrows(bool fails)
        {
            var editor = new TextEditor
            {
                text = "abcdef",
                cursorIndex = 4,
                selectIndex = 1,
                position = new Rect(0, 0, 100, 20),
            };
            editor.HitTest = _ =>
            {
                editor.cursorIndex = editor.selectIndex = 2;
                if (fails)
                    throw new InvalidOperationException("hit test");
            };
            if (fails)
                Assert.Throws<InvalidOperationException>(() =>
                    NativeTextLayout.IsOverSelection(editor, new Vector2(5, 5))
                );
            else
                Assert.That(NativeTextLayout.IsOverSelection(editor, new Vector2(5, 5)), Is.True);
            Assert.That(editor.cursorIndex, Is.EqualTo(4));
            Assert.That(editor.selectIndex, Is.EqualTo(1));
        }

        [Test]
        public void CaretRevealUsesPaddedFieldWidthAndUnscrolledContentPosition()
        {
            var style = new GUIStyle { padding = new RectOffset(3, 7, 4, 0), wordWrap = true };
            var editor = new TextEditor
            {
                text = "wrapped",
                cursorIndex = 3,
                style = style,
                multiline = true,
                position = new Rect(0, 10, 100, 20),
            };
            GuiBackend.Current.Cursor = (text, width, index) =>
            {
                Assert.That(text, Is.EqualTo("wrapped"));
                Assert.That(width, Is.EqualTo(90));
                Assert.That(index, Is.EqualTo(3));
                return new Vector2(5, 32);
            };
            try
            {
                Assert.That(NativeTextLayout.CaretY(editor), Is.EqualTo(46));
            }
            finally
            {
                GuiBackend.Current.Cursor = null;
            }
        }
    }
}
