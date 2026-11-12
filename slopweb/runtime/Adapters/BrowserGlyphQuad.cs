// Axis-aligned glyph clipping matches installed ImGuiGuiBackend.DrawGlyph.
// Text lines own the bottom limit; glyph ascenders are not clipped at the top.
namespace BrowserAdapters;

internal struct BrowserGlyphQuad(
    float left,
    float top,
    float right,
    float bottom,
    float u0,
    float v0,
    float u1,
    float v1
)
{
    internal float Left = left,
        Top = top,
        Right = right,
        Bottom = bottom;
    internal float U0 = u0,
        V0 = v0,
        U1 = u1,
        V1 = v1;

    internal bool Clip(float clipLeft, float clipRight, float clipBottom)
    {
        if (Left >= clipRight || Right <= clipLeft || Top >= clipBottom)
            return false;
        float du = (U1 - U0) / (Right - Left);
        if (Left < clipLeft)
        {
            U0 += (clipLeft - Left) * du;
            Left = clipLeft;
        }
        if (Right > clipRight)
        {
            U1 -= (Right - clipRight) * du;
            Right = clipRight;
        }
        if (Bottom > clipBottom)
        {
            V1 -= (Bottom - clipBottom) * (V1 - V0) / (Bottom - Top);
            Bottom = clipBottom;
        }
        return Right > Left && Bottom > Top;
    }
}
