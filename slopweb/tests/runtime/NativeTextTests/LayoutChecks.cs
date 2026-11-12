using System.Runtime.CompilerServices;
using BrowserAdapters;
using UnityEngine;

static class LayoutChecks
{
    // Resolve installed assemblies before JIT touches Unity value types.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Run()
    {
        var layout = new BrowserNativeLayout(
            (font, size, style) =>
            {
                if (size <= 0)
                    size = 10;
                return new(
                    size,
                    size * .8f,
                    size,
                    size * 1.2f,
                    size * 1.2f,
                    c =>
                        size
                        * (
                            c == ' ' ? .3f
                            : style == FontStyle.Bold ? .6f
                            : .5f
                        )
                );
            }
        );
        var wrapped = layout.Layout(0, 10, FontStyle.Normal, "ab cd", 13, Color.white, true, 1);
        if (wrapped.Lines.Length != 2 || wrapped.Width != 10 || wrapped.Height != 22)
            throw new Exception(
                "Installed wrapping did not preserve word boundaries and native line metrics: "
                    + System.Text.Json.JsonSerializer.Serialize(wrapped)
            );
        if (
            layout.PositionOf(3) != new Vector2(0, 12)
            || layout.PositionOf(4) != new Vector2(5, 12)
            || layout.LineOf(4) != 1
            || layout.IndexAt(new Vector2(6, 13)) != 4
        )
            throw new Exception("Wrapped caret queries disagree with native line geometry");
        var rich = layout.Layout(
            0,
            10,
            FontStyle.Normal,
            "<color=red>ab</color> cd",
            13,
            Color.white,
            true,
            1
        );
        if (
            rich.Width != wrapped.Width
            || rich.Height != wrapped.Height
            || rich.Pieces[0].Color != Color.red
        )
            throw new Exception("Installed rich spans changed layout or lost color");
        var bold = layout.Layout(0, 10, FontStyle.Normal, "<b>ab</b>", -1, Color.white, true, 1);
        if (bold.Width != 12 || bold.Pieces[0].Style != FontStyle.Bold)
            throw new Exception("Installed style transitions did not select the requested metrics");
        var sized = layout.Layout(
            0,
            10,
            FontStyle.Normal,
            "<size=20>a</size>",
            -1,
            Color.white,
            true,
            1
        );
        if (sized.Width != 10 || sized.Height != 20)
            throw new Exception("Installed size transitions did not grow line metrics");
        var scaled = layout.Layout(0, 10, FontStyle.Normal, "aa", -1, Color.white, false, 1.5f);
        if (Math.Abs(scaled.Width - 2 * 8 / 1.5f) > .0001f)
            throw new Exception("Device rounding was not applied to each native glyph advance");
        Console.WriteLine(
            "Installed TextLayout executed wrapping, rich colors, style/size changes and device rounding without a game or native font calls."
        );
    }
}
