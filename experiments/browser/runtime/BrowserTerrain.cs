using System.Numerics;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;

public partial class Probe
{
    private static float[] Columns(Matrix4x4 m) =>
        new[]
        {
            m.M11,
            m.M21,
            m.M31,
            m.M41,
            m.M12,
            m.M22,
            m.M32,
            m.M42,
            m.M13,
            m.M23,
            m.M33,
            m.M43,
            m.M14,
            m.M24,
            m.M34,
            m.M44,
        };

    // Encode the terrain subset of real game submissions. Retain the counts of
    // every omitted shader; a partial terrain view must never pass as a full map.
    [JSExport]
    public static string CaptureTerrain() => CaptureMap(false);

    [JSExport]
    public static string CaptureObjects() => CaptureMap(true);

    private static string CaptureMap(bool objects)
    {
        var snapshot = DrawCall("CaptureSnapshot")!;
        int count = (int)Get(snapshot, "CommandCount");
        var commands = (Array)Get(snapshot, "Commands");
        var textures = new List<object>();
        var textureIds = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        object? ResolveTexture(object material, string name)
        {
            var texture = InstanceCall(material, "GetTexture", name);
            if (texture != null)
                return texture;
            // Match MaterialBindingResolver: material properties precede globals.
            object?[] args =
            {
                Invoke("Waylith", "Waylith.Rendering.ShaderPropertyId", "GetOrCreate", name),
                null,
            };
            if (
                (bool)
                    Type("Waylith", "Waylith.Rendering.GlobalShaderState")
                        .GetMethod("TryGetTexture")!
                        .Invoke(null, args)!
                && args[1] != null
            )
                return args[1];
            // The installed binding path uses the slot's declared default for
            // an unbound optional noise sampler. Keep missing main assets fatal.
            if (name is "_AlphaAddTex" or "_MaskTex")
            {
                var slots = (Array)Get(
                    Get(Get(material, "Shader"), "MaterialLayout"),
                    "TextureSlots"
                );
                var slot = slots.Cast<object>().Single(s => (string)Get(s, "PropertyName") == name);
                if (Get(slot, "Default").ToString() == "White")
                    return Type("Waylith", "Waylith.Rendering.Texture2D")
                        .GetProperty("whiteTexture")!
                        .GetValue(null);
                if (Get(slot, "Default").ToString() == "Black")
                    return Get(
                        Type("UnityStubs", "UnityEngine.Texture2D")
                            .GetProperty("blackTexture")!
                            .GetValue(null)!,
                        "CoreTexture"
                    );
            }
            return null;
        }
        int Texture(object? texture)
        {
            if (texture == null)
                throw new InvalidOperationException("Terrain material has no required texture");
            if (textureIds.TryGetValue(texture, out var id))
                return id;
            var cpu =
                InstanceCall(texture, "EnsureCpuData")
                ?? throw new NotSupportedException("Terrain texture has no readable CPU data");
            var rgba =
                (byte[]?)InstanceCall(cpu, "GetRgba32OrNull")
                ?? throw new NotSupportedException(
                    $"Texture {Get(texture, "Name")} cannot be decoded: {Get(cpu, "Format")}, hasData={Get(cpu, "HasData")}, source={Get(texture, "SourcePath")}"
                );
            id = textures.Count;
            textures.Add(
                new
                {
                    name = Get(texture, "Name"),
                    width = Get(cpu, "Width"),
                    height = Get(cpu, "Height"),
                    format = Get(cpu, "Format").ToString(),
                    rgba,
                }
            );
            textureIds.Add(texture, id);
            return id;
        }
        var draws = new List<object>();
        var omitted = new Dictionary<string, int>();
        foreach (
            var command in commands
                .Cast<object>()
                .Take(count)
                .OrderBy(c =>
                    (int)Get(c, "RenderQueueOverride") is var q && q != 0
                        ? q
                        : (int)Get(Get(c, "Material"), "RenderQueue")
                )
        )
        {
            var material = Get(command, "Material");
            var shader = (string)Get(Get(material, "Shader"), "Key");
            bool sprite =
                objects
                && shader
                    is "Cutout"
                        or "CutoutComplex"
                        or "Transparent"
                        or "CutoutPlant"
                        or "CutoutSkin"
                        or "CutoutHair";
            bool overlay = objects && shader is "LightOverlay" or "EdgeShadow" or "FogOfWar";
            if (
                !sprite
                && !overlay
                && shader is not ("TerrainHard" or "TerrainFade" or "TerrainFadeRough")
            )
            {
                omitted[shader] = omitted.GetValueOrDefault(shader) + 1;
                continue;
            }
            if (
                Get(command, "Instancing") != null
                || Get(command, "Storage") is Array { Length: > 0 }
                || (!objects && (int)Get(Get(command, "PropertyRange"), "Count") != 0)
                || (string)Get(material, "KeywordKey") != ""
            )
            {
                if (!sprite)
                    throw new NotSupportedException("Unexpected terrain per-draw state");
                omitted[shader + ": unsupported per-draw state"] =
                    omitted.GetValueOrDefault(shader + ": unsupported per-draw state") + 1;
                continue;
            }
            var storage = Get(Get(command, "Mesh"), "CpuStorage");
            var resolved = objects
                ? (object[])
                    Invoke(
                        "Adapters",
                        "BrowserAdapters.MaterialCapture",
                        "Resolve",
                        snapshot,
                        command
                    )!
                : null;
            object? BoundTexture(string name) =>
                resolved == null
                    ? ResolveTexture(material, name)
                    : ((Dictionary<string, object?>)resolved[1]).GetValueOrDefault(name);
            object? Read(string method, params object[] args) =>
                Invoke("Waylith", "Waylith.Graphics.Meshes.MeshArrays", method, args);
            var positions = (Vector3[])Read("GetPositions", storage)!;
            var colors = (Array)Read("GetColors", storage)!;
            var uv = sprite ? (Vector4[])Read("GetUv", storage, 0)! : Array.Empty<Vector4>();
            int submeshIndex = (int)Get(command, "SubmeshIndex");
            var indices = (int[])Read("GetTriangles", storage, submeshIndex)!;
            var submesh = ((Array)Get(storage, "SubMeshes")).GetValue(submeshIndex)!;
            if (
                submesh.GetType().GetField("Topology")!.GetValue(submesh)!.ToString() != "Triangles"
            )
                throw new NotSupportedException("Terrain topology is not triangles");
            int baseVertex = (int)submesh.GetType().GetField("BaseVertex")!.GetValue(submesh)!;
            var vertices = new List<float>(indices.Length * 7);
            foreach (int relative in indices)
            {
                int index = checked(relative + baseVertex);
                var p = positions[index];
                var color = colors.Length == 0 ? null : colors.GetValue(index);
                // MeshArrays exposes the installed byte Color32 storage.
                float Channel(string channel) =>
                    color == null
                        ? 1f
                        : Convert.ToSingle(color.GetType().GetField(channel)!.GetValue(color))
                            / 255f;
                vertices.AddRange(
                    new[] { p.X, p.Y, p.Z, Channel("r"), Channel("g"), Channel("b"), Channel("a") }
                );
                if (sprite)
                {
                    var t = uv.Length == 0 ? Vector4.Zero : uv[index];
                    vertices.AddRange(new[] { t.X, t.Y, t.Z, t.W });
                }
            }
            var tint = Get(material, "Color");
            var offset = (Vector2)Get(material, "MainTextureOffset");
            var scale = (Vector2)Get(material, "MainTextureScale");
            if ((bool)Get(Get(material, "Shader"), "FlipTextureST"))
            {
                offset.Y = 1 - offset.Y;
                scale.Y = -scale.Y;
            }
            var second =
                shader == "CutoutComplex" ? InstanceCall(material, "GetColor", "_ColorTwo") : null;
            draws.Add(
                new
                {
                    shader,
                    uniforms = resolved?[0],
                    stride = sprite ? 11 : 7,
                    vertices,
                    textureST = new[] { offset.X, offset.Y, scale.X, scale.Y },
                    colorTwo = second == null
                        ? new[] { 1f, 1f, 1f, 1f }
                        : new[]
                        {
                            (float)Get(second, "r"),
                            (float)Get(second, "g"),
                            (float)Get(second, "b"),
                            (float)Get(second, "a"),
                        },
                    model = Columns((Matrix4x4)Get(command, "Matrix")),
                    color = new[]
                    {
                        (float)Get(tint, "r"),
                        (float)Get(tint, "g"),
                        (float)Get(tint, "b"),
                        (float)Get(tint, "a"),
                    },
                    texture = shader is "LightOverlay" or "EdgeShadow"
                        ? (int?)null
                        : Texture(BoundTexture("_MainTex")),
                    detail = shader == "FogOfWar"
                        ? (int?)Texture(BoundTexture("_DetailTex"))
                        : null,
                    noise = shader == "TerrainFadeRough"
                        ? (int?)Texture(BoundTexture("_AlphaAddTex"))
                        : null,
                    mask = shader == "CutoutComplex"
                        ? (int?)Texture(BoundTexture("_MaskTex"))
                        : null,
                    fall = shader == "CutoutPlant"
                        ? (int?)Texture(BoundTexture("_FallColorDestination"))
                        : null,
                }
            );
        }
        return JsonSerializer.Serialize(
            new
            {
                camera = JsonSerializer.Deserialize<JsonElement>(
                    (string)Invoke("Adapters", "BrowserAdapters.GameSession", "Camera")!
                ),
                draws,
                textures,
                omitted,
                persistent = (
                    (System.Collections.ICollection)Get(snapshot, "PersistentSources")
                ).Count,
                fullColonyFrame = false,
            }
        );
    }
}
