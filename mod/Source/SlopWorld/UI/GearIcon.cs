using UnityEngine;
using Verse;

namespace SlopWorld
{
    // A cog, drawn in code for the same reason TerminalIcon is: TexButton has no gear in
    // it, and this install keeps its textures in asset bundles, so the alternative is a
    // content path that resolves to null on somebody's copy and draws a button you
    // cannot see. White, so the caller tints it. The attribute only quiets the startup
    // scan, which warns about any type holding a static Texture2D.
    [StaticConstructorOnStartup]
    public static class GearIcon
    {
        const int N = 32;
        const int Teeth = 6;

        // Tip and root are the tooth; Hole is the bore, which is what stops the thing
        // reading as a flower at 18 pixels.
        const float Tip = 15f, Root = 10.5f, Hole = 4.6f;

        static Texture2D _tex;

        public static Texture2D Tex => _tex != null ? _tex : _tex = Build();

        // Polar rather than stroked: a cog is an annulus whose outer radius steps with the
        // angle, so both edges fall out of the same distance and antialias for free.
        static Texture2D Build()
        {
            var px = new Color[N * N];
            const float c = N / 2f;

            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float dx = x + 0.5f - c, dy = y + 0.5f - c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    // Flat tops and flat roots with a ramp between them. The ramp is stated in
                    // cos space, where one pixel at the tip is worth about a third of it - narrow
                    // enough to read as a tooth, wide enough not to alias.
                    float t = Mathf.Cos(Teeth * Mathf.Atan2(dy, dx));
                    float outer = Mathf.Lerp(Root, Tip,
                        Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.3f, 0.3f, t)));

                    float a = Mathf.Clamp01(outer + 0.5f - r) * Mathf.Clamp01(r - Hole + 0.5f);
                    px[y * N + x] = new Color(1f, 1f, 1f, a);
                }
            }

            var tex = new Texture2D(N, N, TextureFormat.ARGB32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixels(px);
            tex.Apply();
            // Nothing Unity can see roots a texture built here, so the unload the game runs on
            // any map switch would take it. Same trap as the terminal font.
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return tex;
        }
    }
}
