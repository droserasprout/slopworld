using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class TerminalIOTests
    {
        public static void UnsubscribedRenameEmitsNoCommands()
        {
            var sent = new List<Wire.ClientMessage>();
            var terminal = new TerminalIO(sent.Add);
            terminal.Rename("closed", "renamed");
            terminal.Resubscribe();
            Assert.That(sent, Is.Empty, "unsubscribed names have no socket commands");
        }

        public static void RenameAndReconnect()
        {
            var sent = new List<Wire.ClientMessage>();
            var terminal = new TerminalIO(sent.Add);
            terminal.Subscribe("old");
            sent.Clear();
            terminal.Rename("missing", "ignored");
            terminal.Rename("old", "old");
            Assert.That(sent, Is.Empty);
            terminal.Rename("old", "new");
            Assert.That(sent.Select(m => m.PayloadCase), Is.EqualTo(new[] {
                Wire.ClientMessage.PayloadOneofCase.Unsub, Wire.ClientMessage.PayloadOneofCase.Sub }));
            Assert.That(sent[0].Unsub.Name, Is.EqualTo("old"));
            Assert.That(sent[1].Sub.Name, Is.EqualTo("new"));
            sent.Clear();
            terminal.Resubscribe();
            Assert.That(sent.Single().Sub.Name, Is.EqualTo("new"));
            terminal.Unsubscribe("new");
            Assert.That(sent.Last().Unsub.Name, Is.EqualTo("new"));
            sent.Clear();
            terminal.Resubscribe();
            Assert.That(sent, Is.Empty);
        }

        public static void RenameIntoExistingSubscription()
        {
            var sent = new List<Wire.ClientMessage>();
            var terminal = new TerminalIO(sent.Add);
            terminal.Subscribe("old");
            terminal.Subscribe("new");
            sent.Clear();
            terminal.Rename("old", "new");
            Assert.That(sent.Single().Unsub.Name, Is.EqualTo("old"));
            sent.Clear();
            terminal.Resubscribe();
            Assert.That(sent.Single().Sub.Name, Is.EqualTo("new"));
        }

        public static void InputPayloadsOwnTheirLists()
        {
            var sent = new List<Wire.ClientMessage>();
            var terminal = new TerminalIO(sent.Add);
            var keys = new List<string> { "C-c", "Enter" };
            var tips = new List<string> { "tip" };
            terminal.SendKeys("agent", keys, false);
            terminal.PasteBreadcrumb("agent", "project/file:12", tips);
            keys.Clear();
            tips.Clear();
            Assert.That(sent[0].Keys.Name, Is.EqualTo("agent"));
            Assert.That(sent[0].Keys.Keys, Is.EqualTo(new[] { "C-c", "Enter" }));
            Assert.That(sent[0].Keys.Literal, Is.False);
            Assert.That(sent[1].Breadcrumb.Name, Is.EqualTo("agent"));
            Assert.That(sent[1].Breadcrumb.Breadcrumb, Is.EqualTo("project/file:12"));
            Assert.That(sent[1].Breadcrumb.RandomTips, Is.EqualTo(new[] { "tip" }));
            terminal.SendKeys("agent", new[] { "literal text" }, true);
            Assert.That(sent.Last().Keys.Literal, Is.True);
            terminal.PasteBreadcrumb("agent", "file", null);
            Assert.That(sent.Last().Breadcrumb.RandomTips, Is.Empty);
            terminal.Paste("agent", "line one\nλ\tline two");
            Assert.That(sent.Last().Paste.Name, Is.EqualTo("agent"));
            Assert.That(sent.Last().Paste.Text, Is.EqualTo("line one\nλ\tline two"));
        }

        public static void GeometryAndHistoryPayloads()
        {
            var sent = new List<Wire.ClientMessage>();
            var terminal = new TerminalIO(sent.Add);
            terminal.RequestScroll("agent", 0, ulong.MaxValue);
            Assert.That(sent.Last().Scroll, Is.EqualTo(new Wire.ScrollReq
            {
                Name = "agent",
                Off = 0,
                RequestId = ulong.MaxValue
            }));
            terminal.SendMouse("agent", "press", 2, 79, 23, 3);
            Assert.That(sent.Last().Mouse, Is.EqualTo(new Wire.MouseReq
            {
                Name = "agent",
                Action = "press",
                Button = 2,
                Col = 79,
                Row = 23,
                Count = 3
            }));
            terminal.SendMouse("agent", "release", 0, 0, 0);
            Assert.That(sent.Last().Mouse.Count, Is.EqualTo(1));
            terminal.Resize("agent", 80, 24);
            Assert.That(sent.Last().Resize, Is.EqualTo(new Wire.ResizeReq { Name = "agent", Cols = 80, Rows = 24 }));
            terminal.RefreshPanels(120, 40);
            Assert.That(sent.Last().Redraw, Is.EqualTo(new Wire.RedrawReq { Cols = 120, Rows = 40 }));
            terminal.RefreshPanels();
            Assert.That(sent.Last().Redraw, Is.EqualTo(new Wire.RedrawReq()));
        }

        public static void InvalidGeometryDoesNotSend()
        {
            var sent = new List<Wire.ClientMessage>();
            var terminal = new TerminalIO(sent.Add);
            var invalid = new (string Name, Action Run)[] {
                ("negative scroll offset", () => terminal.RequestScroll("agent", -1, 1)),
                ("negative columns", () => terminal.Resize("agent", -1, 24)),
                ("negative rows", () => terminal.Resize("agent", 80, -1)),
                ("negative redraw columns", () => terminal.RefreshPanels(-1, 24)),
                ("negative redraw rows", () => terminal.RefreshPanels(80, -1)),
                ("negative mouse button", () => terminal.SendMouse("agent", "press", -1, 0, 0)),
                ("negative mouse column", () => terminal.SendMouse("agent", "press", 0, -1, 0)),
                ("negative mouse row", () => terminal.SendMouse("agent", "press", 0, 0, -1)),
                ("negative click count", () => terminal.SendMouse("agent", "press", 0, 0, 0, -1)),
            };
            foreach (var action in invalid) Assert.Throws<OverflowException>(() => action.Run(), action.Name);
            Assert.That(sent, Is.Empty);
        }
        public static void TerminalKeysReportTransportRejection()
        {
            bool accepted = false;
            var terminal = new TerminalIO(_ => accepted);
            Assert.That(terminal.SendKeys("agent", new[] { "text" }, true), Is.False);
            accepted = true;
            Assert.That(terminal.SendKeys("agent", new[] { "text" }, true), Is.True);
            var hub = new HubTransport();
            Assert.That(new TerminalIO(hub).SendKeys("agent", new[] { "text" }, true), Is.False);
        }
    }
}
