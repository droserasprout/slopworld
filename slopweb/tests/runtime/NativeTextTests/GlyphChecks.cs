// Execute the real glyph emitter against synthetic metrics and CPU-only pixels.
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BrowserAdapters;
using UnityEngine;

static class GlyphChecks
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Run(byte[] font, bool benchmark)
    {
        BrowserFontBank.Rasterize = Rasterize;
        int index = BrowserFontBank.Install(font, "{\"size\":15,\"loaderFlags\":0}", 0);
        BrowserFontBank.ObserveText(index, 15, FontStyle.Normal, TestFont.Ascii);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var text = "glyph clipping Agyp 0123";
        string Frame(BrowserGui gui, bool tail, int scenario)
        {
            BrowserGui.ConfigureGlyphTailClip(tail);
            float scale =
                scenario % 3 == 0 ? 1
                : scenario % 3 == 1 ? 5f / 3
                : 2;
            var matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            matrix.m03 = .35f * scenario;
            matrix.m13 = -.2f * scenario;
            if (scenario == 18)
                matrix.m11 += .0005f;
            if (scenario >= 30)
            {
                matrix.m03 = scenario % 2 == 0 ? 12345.125f : -8192.875f;
                if (scenario == 38)
                    matrix.m01 = .125f;
                if (scenario == 39)
                    matrix.m10 = -.125f;
                if (scenario == 40)
                    matrix.m00 = matrix.m11 = -1;
            }
            gui.SetMatrix(matrix);
            gui.PushScrollView(new Rect(2, 3, 280, 80), new Vector2(-1.3f, 2.75f));
            gui.PushClipRect(new Rect(3, 2, scenario == 4 ? 0 : 250, scenario == 5 ? 0 : 70));
            if (scenario >= 30)
            {
                gui.PushGroup(new Rect(-7.35f, .3f, 45.75f, 80));
                gui.PushGroup(new Rect(5.625f, -.2f, scenario % 4 == 0 ? 0 : .6f, 70));
            }
            var rect = new Rect(scenario % 4 - 5.25f, -3.75f, 275, 70);
            var clip = scenario % 2 == 0 ? new Rect(1.25f, 0, 91.5f, 17.25f) : default;
            var color = new Color(.7f, .3f, .9f, scenario % 2 == 0 ? .5f : 1);
            string label = scenario switch
            {
                20 or 21 => new string('x', 2000),
                22 or 23 => new string('x', 20) + "~x",
                24 or 25 => new string('x', 20) + "^^xx",
                26 or 27 => new string('_', 200) + "xx",
                28 or 29 => new string(' ', 2000) + "xx",
                >= 30 => new string('x', 30)
                    + (
                        scenario % 3 == 0 ? "~"
                        : scenario % 3 == 1 ? "^^"
                        : "_"
                    )
                    + new string('x', 1000),
                _ => scenario == 7 ? text + "\n" + text : text,
            };
            gui.DrawText(
                rect,
                label,
                index,
                15,
                FontStyle.Normal,
                (TextAnchor)(scenario % 9),
                false,
                color,
                clip,
                false
            );
            gui.FillRect(new Rect(1, 2, 3, 4), Color.red);
            gui.DrawText(
                rect,
                text,
                index,
                15,
                FontStyle.Normal,
                TextAnchor.UpperLeft,
                false,
                color,
                clip,
                false
            );
            if (scenario >= 30)
            {
                gui.PopGroup();
                gui.PopGroup();
            }
            gui.PopClipRect();
            gui.PopGroup();
            // Cross a partially filled batch's 8192-float boundary.
            if (scenario == 19)
                gui.DrawText(
                    new Rect(0, 0, 20000, 70),
                    new string('x', 1400),
                    index,
                    15,
                    FontStyle.Normal,
                    TextAnchor.UpperLeft,
                    false,
                    Color.white,
                    default,
                    false
                );
            int traced = (int)
                typeof(BrowserGui).GetField("outsideTargetQuads", flags)!.GetValue(gui)!;
            return traced + ":" + gui.Drain();
        }
        var originalDirect = new BrowserGui();
        var clippedDirect = new BrowserGui();
        bool previousProfile = BrowserGuiInput.Profile;
        for (int scenario = 0; scenario < 42; scenario++)
        for (int repeat = 0; repeat < 3; repeat++)
        {
            BrowserGuiInput.Profile = repeat != 0;
            if (Frame(originalDirect, false, scenario) != Frame(clippedDirect, true, scenario))
                throw new Exception(
                    $"Tail clipping changed packet or trace count at {scenario}/{repeat}"
                );
        }
        // A glyph appended between frames must still upload when clipping is enabled.
        BrowserFontBank.ObserveText(index, 15, FontStyle.Normal, "Ω");
        string mutated = Frame(originalDirect, false, 1);
        if (!mutated.Contains("\"kind\":\"image\"") || mutated != Frame(clippedDirect, true, 1))
            throw new Exception("Tail clipping changed the updated atlas upload");
        var fresh = new BrowserGui();
        fresh.DrawText(
            new Rect(0, 0, 200, 20),
            text,
            index,
            15,
            FontStyle.Normal,
            TextAnchor.UpperLeft,
            false,
            Color.white,
            default,
            false
        );
        fresh.Discard();
        if (Frame(new BrowserGui(), false, 1) != Frame(fresh, true, 1))
            throw new Exception("Tail clipping lost a discarded upload");
        Console.WriteLine(
            "Real glyph packets match with tail clipping off/on across clips, scales, anchors, scroll offsets, batching, image mutation and discarded uploads."
        );
        BrowserGuiInput.Profile = previousProfile;
        BrowserGui.ConfigureGlyphTailClip(true);
        CellChecks.Run(index, benchmark);
        RowChecks.Run(index);
    }

    static string Rasterize(string request)
    {
        using var document = JsonDocument.Parse(request);
        var item = document.RootElement;
        if (!item.TryGetProperty("scalar", out var scalar))
            return item.TryGetProperty("size", out _) ? "{\"ascent\":11.25,\"descent\":-3}" : "{}";
        int c = scalar.GetInt32();
        if (c < ' ' || c > '~' && c != 'Ω')
            return "{\"missing\":true}";
        float density = item.GetProperty("density").GetSingle();
        int style = item.GetProperty("flags").GetInt32() >> 5;
        // Later overhangs and backwards advances can move ink back into a narrow clip.
        int width = c == ' ' ? 0 : (int)Math.Ceiling((c == '~' ? 187 : 8) * density);
        int height = (int)Math.Ceiling(15 * density);
        return JsonSerializer.Serialize(
            new
            {
                advance = c switch
                {
                    '^' => -100,
                    '_' => 0,
                    _ => 7.3f + c % 3 * .25f + style * .1f,
                },
                x = (c == '~' ? -180 : -.75f) + style * .2f + density * .1f,
                y = -13.5f,
                width,
                height,
                pixels = Convert.ToBase64String(
                    Enumerable.Repeat((byte)c, width * height).ToArray()
                ),
            }
        );
    }
}
