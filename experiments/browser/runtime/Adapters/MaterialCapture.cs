using System.Reflection;
using System.Runtime.InteropServices;
using Waylith.Rendering;

namespace BrowserAdapters;

// Keep material defaults, global values and per-draw precedence in the installed
// resolver. Copy its std140 bytes before the frame's property arena is reused.
public static class MaterialCapture
{
    public static string VerifyCpuTextureRetention()
    {
        using var texture = Texture2D.CreateSolid(
            "Browser.RetentionCheck",
            new Waylith.Math.Color32(12, 34, 56, 78)
        );
        var pixels = texture.EnsureCpuData()?.GetRgba32OrNull();
        if (pixels == null || !pixels.SequenceEqual(new byte[] { 12, 34, 56, 78 }))
            throw new InvalidOperationException(
                "Procedural texture pixels were discarded before WebGL upload"
            );
        return "actual CreateSolid/Apply retains all RGBA channels";
    }

    // Native GpuTextureService fallbacks fill all four channels, including alpha.
    private static readonly Lazy<Texture2D> Black = new(() =>
        Texture2D.CreateSolid("Browser.BlackFallback", new Waylith.Math.Color32(0, 0, 0, 0))
    );
    private static readonly Lazy<Texture2D> Gray = new(() =>
        Texture2D.CreateSolid("Browser.GrayFallback", new Waylith.Math.Color32(128, 128, 128, 128))
    );
    private static readonly MethodInfo ResolveMethod = typeof(Material)
        .Assembly.GetType("Waylith.Rendering.MaterialBindingResolver", true)!
        .GetMethod("ResolveMaterialData", BindingFlags.Static | BindingFlags.NonPublic)!;

    public static object[] Resolve(object snapshotValue, object commandValue)
    {
        var snapshot = (MeshDrawSubmissionSnapshot)snapshotValue;
        var command = (MeshDrawCommand)commandValue;
        object?[] args =
        {
            command.Material,
            command.PropertyRange,
            snapshot.PropertyArena,
            null,
            null,
            false,
        };
        ResolveMethod.Invoke(null, args);
        var uniforms = (MaterialUniformBytes)args[3]!;
        var resources = (MaterialTextureResourceSet<Texture?>)args[4]!;
        var textures = new Dictionary<string, object?>();
        foreach (var slot in command.Material.Shader.MaterialLayout.TextureSlots)
        {
            if (slot.Kind == MaterialTextureKind.Sampler)
                continue;
            var texture = resources[slot.ResourceSlot];
            // Unbound slots use the shader's authored default, including MainTex
            // on solid-color materials. Missing asset handling stays in the game.
            if (texture == null)
                texture = slot.Default switch
                {
                    MaterialTextureDefault.White => Texture2D.whiteTexture,
                    MaterialTextureDefault.Black => Black.Value,
                    MaterialTextureDefault.Gray => Gray.Value,
                    _ => throw new NotSupportedException("Unknown texture default"),
                };
            textures[slot.PropertyName] = texture;
        }
        return new object[]
        {
            MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in uniforms, 1)).ToArray(),
            textures,
        };
    }
}
