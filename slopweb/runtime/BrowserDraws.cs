using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;

public partial class Probe
{
    [JSExport]
    public static void InstallSceneShaders(string vertex, string fragment)
    {
        Directory.CreateDirectory("/browser-shaders");
        File.WriteAllText("/browser-shaders/SceneDefault.vert.glsl", vertex);
        File.WriteAllText("/browser-shaders/SceneDefault.frag.glsl", fragment);
        Invoke(
            "Waylith",
            "Waylith.Rendering.ShaderSourceLoader",
            "RegisterShaderSourceDirectory",
            "/browser-shaders"
        );
    }

    private static object Get(object owner, string name) =>
        owner.GetType().GetProperty(name)!.GetValue(owner)!;

    private static object? DrawCall(string name, params object[] args) =>
        Invoke("Waylith", "Waylith.Rendering.MeshDrawSubmission", name, args);

    // Observe actual game submissions without treating an inventory as rendering.
    [JSExport]
    public static string DrawInventory()
    {
        var snapshot = DrawCall("CaptureSnapshot")!;
        int count = (int)Get(snapshot, "CommandCount");
        var commands = (Array)Get(snapshot, "Commands");
        var groups = commands
            .Cast<object>()
            .Take(count)
            .GroupBy(command =>
            {
                var material = Get(command, "Material");
                return new
                {
                    shader = (string)Get(Get(material, "Shader"), "Key"),
                    keywords = (string)Get(material, "KeywordKey"),
                    instanced = Get(command, "Instancing") != null,
                    properties = (int)Get(Get(command, "PropertyRange"), "Count"),
                    queue = (int)Get(command, "RenderQueueOverride"),
                };
            })
            .Select(group => new { state = group.Key, count = group.Count() })
            .ToArray();
        return JsonSerializer.Serialize(
            new
            {
                commands = count,
                persistent = (
                    (System.Collections.ICollection)Get(snapshot, "PersistentSources")
                ).Count,
                groups,
                rendered = false,
            }
        );
    }

    // Copy the actual submission snapshot before its arenas can be reused.
    // This first encoder supports plain SceneDefault, non-instanced draws;
    // readable opaque CPU textures can be uploaded with point/clamp sampling.
    [JSExport]
    public static string CaptureDraws()
    {
        var snapshot = DrawCall("CaptureSnapshot")!;
        if (((System.Collections.ICollection)Get(snapshot, "PersistentSources")).Count != 0)
            throw new NotSupportedException("Persistent draw sources need a browser adapter");
        var count = (int)Get(snapshot, "CommandCount");
        var commands = (Array)Get(snapshot, "Commands");
        var draws = new List<object>();
        for (int i = 0; i < count; i++)
        {
            var command = commands.GetValue(i)!;
            var material = Get(command, "Material");
            var mesh = Get(command, "Mesh");
            var storage = Get(mesh, "CpuStorage");
            if (
                Get(command, "Instancing") != null
                || Get(command, "Storage") is Array { Length: > 0 }
                || (int)Get(Get(command, "PropertyRange"), "Count") != 0
                || (string)Get(material, "KeywordKey") != ""
                || (int)Get(command, "RenderQueueOverride") != 0
                || (string)Get(Get(material, "Shader"), "Key") != "scene-default"
            )
                throw new NotSupportedException(
                    "Browser draw encoder requires plain SceneDefault commands"
                );
            object? Read(string method, params object[] args) =>
                Invoke("Waylith", "Waylith.Graphics.Meshes.MeshArrays", method, args);
            var positions = (Vector3[])Read("GetPositions", storage)!;
            var normals = (Vector3[])Read("GetNormals", storage)!;
            var uv = (Vector4[])Read("GetUv", storage, 0)!;
            var colors = (Array)Read("GetColors", storage)!;
            if (colors.Length != 0)
                throw new NotSupportedException("Vertex colors need a browser mesh format adapter");
            var indices = (int[])Read("GetTriangles", storage, (int)Get(command, "SubmeshIndex"))!;
            var submesh = ((Array)Get(storage, "SubMeshes")).GetValue(
                (int)Get(command, "SubmeshIndex")
            )!;
            if (
                submesh.GetType().GetField("Topology")!.GetValue(submesh)!.ToString() != "Triangles"
            )
                throw new NotSupportedException("Browser draw encoder requires triangle topology");
            int baseVertex = (int)submesh.GetType().GetField("BaseVertex")!.GetValue(submesh)!;
            var vertices = new List<float>();
            foreach (var relativeIndex in indices)
            {
                int index = checked(relativeIndex + baseVertex);
                var p = positions[index];
                var n = normals.Length == 0 ? Vector3.UnitY : normals[index];
                var t = uv.Length == 0 ? Vector4.Zero : uv[index];
                vertices.AddRange(
                    new[] { p.X, p.Y, p.Z, n.X, n.Y, n.Z, 1f, 1f, 1f, 1f, t.X, t.Y, t.Z, t.W }
                );
            }
            var matrix = (Matrix4x4)Get(command, "Matrix");
            var color = Get(material, "Color");
            if ((float)Get(color, "a") != 1f)
                throw new NotSupportedException(
                    "Transparent draws need browser blend-state support"
                );
            object? textureData = null;
            var texture = Get(material, "MainTexture");
            if (texture != null)
            {
                if (
                    Get(texture, "filterMode").ToString() != "Point"
                    || Get(texture, "wrapMode").ToString() != "Clamp"
                )
                    throw new NotSupportedException(
                        "Browser texture adapter currently requires point/clamp sampling"
                    );
                var cpu = Get(texture, "CpuData");
                var rgba =
                    (byte[]?)cpu.GetType().GetMethod("GetRgba32OrNull")!.Invoke(cpu, null)
                    ?? throw new NotSupportedException("Texture has no decodable CPU pixels");
                if (Enumerable.Range(0, rgba.Length / 4).Any(p => rgba[p * 4 + 3] != 255))
                    throw new NotSupportedException(
                        "Non-opaque texture requires browser blend handling"
                    );
                var offset = (Vector2)Get(material, "MainTextureOffset");
                var scale = (Vector2)Get(material, "MainTextureScale");
                if (offset != Vector2.Zero || scale != Vector2.One)
                    throw new NotSupportedException("Texture transform is not yet encoded");
                textureData = new
                {
                    width = Get(cpu, "Width"),
                    height = Get(cpu, "Height"),
                    format = Get(cpu, "Format").ToString(),
                    rgba,
                    name = Get(texture, "Name"),
                };
            }
            draws.Add(
                new
                {
                    meshId = Get(mesh, "Id"),
                    version = Get(mesh, "Version"),
                    shader = Get(Get(material, "Shader"), "Key"),
                    vertices,
                    texture = textureData,
                    // UnityStubs.ToNumerics retains column-vector conventions:
                    // translation is M14/M24/M34. Upload columns explicitly.
                    model = new[]
                    {
                        matrix.M11,
                        matrix.M21,
                        matrix.M31,
                        matrix.M41,
                        matrix.M12,
                        matrix.M22,
                        matrix.M32,
                        matrix.M42,
                        matrix.M13,
                        matrix.M23,
                        matrix.M33,
                        matrix.M43,
                        matrix.M14,
                        matrix.M24,
                        matrix.M34,
                        matrix.M44,
                    },
                    color = new[]
                    {
                        (float)Get(color, "r"),
                        (float)Get(color, "g"),
                        (float)Get(color, "b"),
                        (float)Get(color, "a"),
                    },
                }
            );
        }
        return JsonSerializer.Serialize(new { commandCount = count, draws });
    }

    [JSExport]
    public static string SubmitGameMeshes()
    {
        try
        {
            return SubmitGameMeshesCore();
        }
        catch (Exception error)
        {
            throw new InvalidOperationException(error.ToString());
        }
    }

    [JSExport]
    public static string SubmitPackedGameMeshes()
    {
        try
        {
            return SubmitGameMeshesCore(true);
        }
        catch (Exception error)
        {
            throw new InvalidOperationException(error.ToString());
        }
    }

    private static string SubmitGameMeshesCore(bool textured = false)
    {
        DrawCall("ClearFrameSubmissions");
        var mesh = Invoke("Assembly-CSharp", "Verse.MeshMakerPlanes", "NewPlaneMesh", 1f)!;
        using var texture = textured ? LoadPackedTexture() : null;
        var materialType = Type("Waylith", "Waylith.Rendering.Material");
        var materials = new List<IDisposable>();
        try
        {
            for (int i = 0; i < 2; i++)
            {
                var material = Activator.CreateInstance(
                    materialType,
                    new object?[] { null, "browser-submission" }
                )!;
                materials.Add((IDisposable)material);
                if (texture != null)
                    materialType.GetProperty("MainTexture")!.SetValue(material, texture);
                int colorId = (int)
                    Invoke(
                        "Waylith",
                        "Waylith.Rendering.ShaderPropertyId",
                        "GetOrCreate",
                        "_Color"
                    )!;
                materialType
                    .GetMethod("SetColor", new[] { typeof(int), typeof(Vector4) })!
                    .Invoke(
                        material,
                        new object[]
                        {
                            colorId,
                            textured ? Vector4.One
                            : i == 0 ? new Vector4(1, 0, 0, 1)
                            : new Vector4(0, 1, 0, 1),
                        }
                    );
                object Vector(float x, float y, float z) =>
                    Activator.CreateInstance(Type("UnityStubs", "UnityEngine.Vector3"), x, y, z)!;
                var rotation = Type("UnityStubs", "UnityEngine.Quaternion")
                    .GetProperty("identity")!
                    .GetValue(null)!;
                var matrix = Invoke(
                    "UnityStubs",
                    "UnityEngine.Matrix4x4",
                    "TRS",
                    Vector(i == 0 ? -.5f : .5f, 0, 0),
                    rotation,
                    Vector(.7f, 1, .7f)
                )!;
                Invoke("UnityStubs", "UnityEngine.Graphics", "DrawMesh", mesh, matrix, material, 0);
            }
            var result = CaptureDraws();
            DrawCall("ClearFrameSubmissions");
            if ((int)Get(DrawCall("CaptureSnapshot")!, "CommandCount") != 0)
                throw new InvalidOperationException("Stale draws survived frame reset");
            return result;
        }
        finally
        {
            DrawCall("ClearFrameSubmissions");
            foreach (var material in materials)
                material.Dispose();
            mesh.GetType().GetMethod("Dispose")!.Invoke(mesh, null);
        }
    }
}
