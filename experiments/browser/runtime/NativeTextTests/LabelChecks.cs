// Observe real adapter geometry and glyph admission without an engine or browser.
using System.Text.Json;
using BrowserAdapters;
using UnityEngine;
using UnityStubs.Gui;

static class LabelChecks
{
    internal static void Run(int index)
    {
        var previousBackend = GuiBackend.Current;
        var previousEvent = Event.current;
        bool previous = BrowserGui.ConfigureObserveLabels(false);
        try
        {
            var gui = BrowserGui.Install();
            Event.current = new Event { type = EventType.Repaint };
            void Draw(string text, Rect? rect = null, Rect clip = default, Color? color = null) =>
                gui.DrawText(
                    rect ?? new Rect(10, 10, 80, 25),
                    text,
                    index,
                    15,
                    FontStyle.Normal,
                    TextAnchor.UpperLeft,
                    false,
                    color ?? Color.white,
                    clip,
                    false
                );
            JsonElement[] Read()
            {
                using var json = JsonDocument.Parse(BrowserGui.Labels());
                return json.RootElement.EnumerateArray().Select(e => e.Clone()).ToArray();
            }
            void Require(bool ok, string message)
            {
                if (!ok)
                    throw new Exception(message);
            }

            Draw("disabled");
            Require(Read().Length == 0, "Label observation must default off");
            Require(!BrowserGui.ConfigureObserveLabels(true), "Enable must return prior setting");
            gui.BeginLabelRepaint();
            var matrix = Matrix4x4.Scale(new Vector3(2, 2, 1));
            matrix.m03 = 10;
            matrix.m13 = 20;
            gui.SetMatrix(matrix);
            gui.PushGroup(new Rect(5, 7, 100, 100));
            gui.PushScrollView(new Rect(0, 0, 50, 40), new Vector2(-3, -4));
            gui.PushClipRect(new Rect(15, 12, 10, 10));
            Draw("menu", new Rect(10, 10, 30, 20), new Rect(12, 11, 10, 8));
            var label = Read().Single();
            Require(
                label.GetProperty("x").GetSingle() == 44
                    && label.GetProperty("y").GetSingle() == 50
                    && label.GetProperty("width").GetSingle() == 14
                    && label.GetProperty("height").GetSingle() == 14,
                "Labels must follow scale, translation, group/scroll origins and drawing clips"
            );
            gui.PushClipRect(new Rect(200, 200, 10, 10));
            Draw("hidden");
            gui.PopClipRect();
            Require(Read().Length == 1, "Empty nested drawing clips must reject labels");
            gui.PopClipRect();
            gui.PopGroup();
            gui.PopGroup();
            gui.SetMatrix(Matrix4x4.identity);
            gui.BeginLabelRepaint();
            Draw("~");
            Require(
                Read().Single().GetProperty("text").GetString() == "~",
                "One-character menu labels must survive"
            );
            Event.current.type = EventType.Layout;
            Draw("layout");
            Event.current.type = EventType.MouseDown;
            Draw("input");
            Event.current.type = EventType.Repaint;
            Draw("transparent", color: new Color(1, 1, 1, 0));
            Draw("   ");
            gui.PushClipRect(new Rect(100, 10, 10, 20));
            // The box overlaps the clip but this glyph's ink does not.
            Draw("~", new Rect(10, 10, 200, 25));
            gui.PopClipRect();
            Require(Read().Length == 1, "Only visible repaint glyphs should be observed");
            string snapshot = BrowserGui.Labels();
            gui.Drain();
            Require(
                BrowserGui.Labels() == snapshot && BrowserGui.Labels() == snapshot,
                "Reading and draining must preserve the latest repaint snapshot"
            );
            gui.BeginLabelRepaint();
            Require(Read().Length == 0, "An empty repaint must clear old labels");
            for (int i = 0; i < 5000; i++)
                Draw("x");
            Draw("dialog");
            Require(
                Read().Length == 4096 && Read()[^1].GetProperty("text").GetString() == "dialog",
                "Terminal cells must not crowd out later dialogs"
            );
            gui.BeginLabelRepaint();
            for (int i = 0; i < 100; i++)
                Draw(new string('x', 5000));
            var bounded = Read();
            Require(
                bounded.Sum(e => e.GetProperty("text").GetString()!.Length) <= 262144
                    && bounded.All(e => e.GetProperty("text").GetString()!.Length <= 4096),
                "Label text retention must be bounded"
            );

            // Compare production packets with observations off/on, including atlas upload.
            string Packet(bool enabled)
            {
                gui = BrowserGui.Install();
                BrowserGui.ConfigureObserveLabels(enabled);
                Draw("~");
                Draw("menu");
                return gui.Drain();
            }
            Require(
                Packet(false) == Packet(true),
                "Observation changed drawing commands or pixels"
            );
            BrowserGuiInput.Dispatch("[]", gui, () => { });
            Require(Read().Length == 0, "Dispatch must reset even an empty repaint");
            Require(
                BrowserGui.ConfigureObserveLabels(false) && Read().Length == 0,
                "Disable must return prior setting and release observations"
            );
            Console.WriteLine(
                "Label observations preserve packets and track repaint geometry, clips and bounded retention."
            );
        }
        finally
        {
            BrowserGui.ConfigureObserveLabels(previous);
            GuiBackend.Current = previousBackend;
            Event.current = previousEvent;
        }
    }
}
