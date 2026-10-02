using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class MarkdownResourcesTests
    {
        internal static byte[] Png(int width, int height)
        {
            var bytes = new byte[33];
            byte[] header = { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82 };
            Array.Copy(header, bytes, header.Length);
            for (int i = 0; i < 4; i++)
            {
                bytes[16 + i] = (byte)(width >> ((3 - i) * 8));
                bytes[20 + i] = (byte)(height >> ((3 - i) * 8));
            }
            return bytes;
        }

        static Wire.ImageResult Image(int width = 32, int height = 24) =>
            new Wire.ImageResult { Data = ByteString.CopyFrom(Png(width, height)) };

        public static void BudgetReservesConcurrentDecodesAndRecoversReleasedCapacity()
        {
            var budget = new MarkdownImageBudget();
            Assert.That(budget.TryReserve("a"), Is.True);
            Assert.That(budget.TryReserve("a"), Is.False);
            Assert.That(budget.TryReserve("b"), Is.True);
            Assert.That(budget.TryReserve("c"), Is.False, "at most two pending loads");
            Assert.That(budget.Complete("a", MarkdownImageBudget.ImageBytes + 1), Is.False);
            Assert.That(budget.Complete("a", MarkdownImageBudget.ImageBytes), Is.True);
            Assert.That(budget.TryReserve("c"), Is.True);
            Assert.That(budget.Complete("b", MarkdownImageBudget.ImageBytes), Is.True);
            Assert.That(budget.TryReserve("d"), Is.True);
            Assert.That(budget.Complete("c", MarkdownImageBudget.ImageBytes), Is.True);
            Assert.That(budget.TryReserve("e"), Is.False, "resident images and pending decodes share one budget");
            budget.Release("a");
            Assert.That(budget.TryReserve("e"), Is.True);
            budget.Clear();
            Assert.That(budget.UsedBytes, Is.Zero);
            Assert.That(budget.Complete("d", 4), Is.False, "retired request cannot repopulate accounting");
        }

        public static void ImageHeadersRejectOversizedAndTruncatedDimensions()
        {
            Assert.That(MarkdownImageHeader.TrySize(Png(2048, 2048), out var w, out var h), Is.True);
            Assert.That((w, h), Is.EqualTo((2048, 2048)));
            foreach (var bytes in new[] { Png(4096, 4096), Png(0, 1), Png(-1, 1), Png(1, 5000), new byte[4] })
                Assert.That(MarkdownImageHeader.TrySize(bytes, out _, out _), Is.False);
            byte[] jpeg = { 255, 216, 255, 224, 0, 2, 255, 192, 0, 8, 8, 0, 24, 0, 32, 0 };
            Assert.That(MarkdownImageHeader.TrySize(jpeg, out w, out h), Is.True);
            Assert.That((w, h), Is.EqualTo((32, 24)));
            Assert.That(MarkdownImageHeader.TrySize(jpeg.Take(13).ToArray(), out _, out _), Is.False);
            jpeg[8] = 255;
            Assert.That(MarkdownImageHeader.TrySize(jpeg, out _, out _), Is.False);
        }

        public static void ImagesLoadNearViewportAndEvictWithoutChangingNaturalDimensions()
        {
            DaemonClient.Requests.Clear();
            var store = new MarkdownResourceStore(new MarkdownPathResolver("demo", "/work/demo/doc.md", () => "/work/demo"));
            var runs = Enumerable.Range(0, 8).Select(i => new InlineRun { IsImage = true, ImagePath = i + ".png" }).ToList();
            var blocks = runs.Select(run => new MarkdownBlock { Kind = BlockKind.Paragraph, Runs = new List<InlineRun> { run } }).ToList();
            store.Request(blocks, 1, _ => true, () => { });
            Assert.That(DaemonClient.Requests, Is.Empty, "parsing does not load all images");
            var placements = runs.Select((run, i) => new Placement { Kind = PlacementKind.Image, Image = run, Y = i * 1000, Height = 100 }).ToList();
            store.RequestVisible(placements, 1, 0, 100, 1, _ => true, () => { });
            Assert.That(DaemonClient.Requests.Count, Is.EqualTo(1));
            Assert.That(DaemonClient.Requests[0].Path, Does.Contain("root=%2Fwork%2Fdemo"));
            DaemonClient.Requests[0].Reply(Image());
            var first = store.ImageFor(runs[0]);
            Assert.That(first, Is.Not.Null);
            store.RequestVisible(placements, 1, 5000, 100, 1, _ => true, () => { });
            Assert.That(first.Destroyed, Is.True);
            Assert.That(store.ImageFor(runs[0]), Is.Null);
            Assert.That(store.ImageSizeFor(runs[0]).Width, Is.EqualTo(32f));
            Assert.That(DaemonClient.Requests.Count, Is.EqualTo(2));
            var late = DaemonClient.Requests[1];
            store.Clear();
            int decoded = Texture2D.DecodeCalls;
            late.Reply(Image());
            Assert.That(Texture2D.DecodeCalls, Is.EqualTo(decoded), "retired document must not decode a late reply");
        }

        public static void VisibleImagesPrecedePrefetchAndFailuresReleaseRequestSlots()
        {
            DaemonClient.Requests.Clear();
            var store = new MarkdownResourceStore(new MarkdownPathResolver("demo", "/work/demo/doc.md", () => "/work/demo"));
            var runs = Enumerable.Range(0, 5).Select(i => new InlineRun { IsImage = true, ImagePath = i + ".png" }).ToList();
            store.Request(runs.Select(run => new MarkdownBlock { Runs = new List<InlineRun> { run } }).ToList(), 1, _ => true, () => { });
            var placements = runs.Select((run, i) => new Placement { Image = run, Y = i * 100, Height = 20 }).ToList();
            store.RequestVisible(placements, 1, 200, 100, 1, _ => true, () => { });
            Assert.That(DaemonClient.Requests.Count, Is.EqualTo(2));
            Assert.That(DaemonClient.Requests[0].Path, Does.Contain("2.png"));
            int decoded = Texture2D.DecodeCalls;
            DaemonClient.Requests[0].Reply(Image(4096, 4096));
            Assert.That(Texture2D.DecodeCalls, Is.EqualTo(decoded), "dimension bomb is rejected before decoding");
            DaemonClient.Requests[1].Fail("missing");
            store.RequestVisible(placements, 1, 200, 100, 1, _ => true, () => { });
            Assert.That(DaemonClient.Requests.Count, Is.EqualTo(4), "failed loads free both request slots");
            store.Clear();
        }
    }
}
