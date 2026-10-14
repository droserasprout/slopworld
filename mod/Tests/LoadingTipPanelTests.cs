using System;
using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class LoadingTipPanelTests
    {
        [TestCase(1920, 1080)]
        [TestCase(960, 540)]
        [TestCase(320, 200)]
        public void StreamClipUsesFullScreenHeightAndRightAlignedPanel(int width, int height)
        {
            Rect frame = LoadingTipPanel.Frame(width, height);
            Assert.That(frame.x + frame.width, Is.EqualTo(width));
            Assert.That(frame.y, Is.Zero);
            Assert.That(frame.height, Is.EqualTo(height));
            int previousDepth = GUI.TextGroupDepth;
            bool drawn = false;
            LoadingTipPanel.Draw(
                frame,
                new Vector2(16, 16),
                () =>
                {
                    drawn = true;
                    Rect clip = GUI.TextGroups[GUI.TextGroups.Count - 1];
                    Assert.That(clip.y, Is.EqualTo(16));
                    Assert.That(clip.height, Is.EqualTo(height - 32));
                    Assert.That(clip.x, Is.EqualTo(frame.x + 16));
                    Assert.That(GUI.TextGroupDepth, Is.EqualTo(previousDepth + 1));
                }
            );
            Assert.That(drawn, Is.True);
            Assert.That(GUI.TextGroupDepth, Is.EqualTo(previousDepth));
        }

        [Test]
        public void DrawingFailureRestoresClip()
        {
            int previousDepth = GUI.TextGroupDepth;
            Assert.Throws<InvalidOperationException>(() =>
                LoadingTipPanel.Draw(
                    LoadingTipPanel.Frame(1920, 1080),
                    new Vector2(16, 16),
                    () => throw new InvalidOperationException("draw failed")
                )
            );
            Assert.That(GUI.TextGroupDepth, Is.EqualTo(previousDepth));
        }

        [TestCase(100f, 16f, 15f)]
        [TestCase(1048f, 17.6f, 16.5f)]
        public void LastVisibleGlyphFitsInsideClip(float height, float glyphHeight, float advance)
        {
            int lines = LoadingTipPanel.VisibleLines(height, glyphHeight, advance);
            Assert.That((lines - 1) * advance + glyphHeight, Is.LessThanOrEqualTo(height));
            Assert.That(lines * advance + glyphHeight, Is.GreaterThan(height));
        }
    }
}
