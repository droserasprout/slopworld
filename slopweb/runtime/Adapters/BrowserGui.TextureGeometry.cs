// Native ImGui owns texture fitting/cropping policy; this boundary converts its
// top-down UV endpoints to the Unity texcoord rectangle used by browser packets.
using System.Reflection;
using Brutal.Numerics;
using UnityEngine;

namespace BrowserAdapters;

public sealed partial class BrowserGui
{
    static class TextureGeometry
    {
        internal delegate void FitImage(
            Rect rect,
            UnityEngine.Texture image,
            ScaleMode mode,
            out Rect destination,
            out float2 uvMin,
            out float2 uvMax
        );

        internal static readonly FitImage Fit = AppDomain
            .CurrentDomain.GetAssemblies()
            .Single(a => a.GetName().Name == "RimWorld")
            .GetType("RimWorldWaylith.Imgui.ImGuiGuiBackend", true)!
            .GetMethod("FitImage", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<FitImage>();
    }

    static void FitTexture(
        Rect rect,
        UnityEngine.Texture image,
        ScaleMode mode,
        out Rect destination,
        out Rect texCoords
    )
    {
        // Bind the private CPU-only helper once; ordinary draws use a typed call
        // without reflection, argument arrays or boxed geometry.
        TextureGeometry.Fit(rect, image, mode, out destination, out var min, out var max);
        texCoords = new Rect(min.X, 1 - max.Y, max.X - min.X, max.Y - min.Y);
    }
}
