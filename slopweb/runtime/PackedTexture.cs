using System.Reflection;

public partial class Probe
{
    private const string TerrainTexture =
        "Resources/Textures/Terrain/Surfaces/Concrete.psd.dds.split";

    // Decode the installed pack/stream-split DDS through its actual managed
    // readers, then retain its CPU texture data for browser GPU upload.
    private static IDisposable LoadPackedTexture()
    {
        using var pack = (IDisposable)
            Invoke("Waylith", "Waylith.Assets.AssetPack", "TryOpen", "/Assets.zip")!;
        object?[] read = { pack, TerrainTexture, null };
        if (
            !(bool)
                Type("Waylith", "Waylith.Assets.ContentPack")
                    .GetMethod("TryReadTexture")!
                    .Invoke(null, read)!
        )
            throw new InvalidDataException("Installed terrain texture was not found");
        object?[] create = { read[2], null };
        var sourceType = Type("Waylith", "Waylith.Graphics.Textures.PackTextureSource");
        var factory = sourceType
            .GetMethods()
            .Single(m => m.Name == "TryCreate" && m.GetParameters().Length == 2);
        if (!(bool)factory.Invoke(null, create)!)
            throw new InvalidDataException("Invalid installed DDS");
        using var source = (IDisposable)create[1]!;
        object?[] load = { null };
        if (!(bool)sourceType.GetMethod("TryReadAll")!.Invoke(source, load)!)
            throw new InvalidDataException("Installed texture levels could not be read");
        var loaded = load[0]!;
        var type = Type("Waylith", "Waylith.Rendering.Texture2D");
        var texture = (IDisposable)
            Activator.CreateInstance(
                type,
                Get(loaded, "Width"),
                Get(loaded, "Height"),
                Get(loaded, "Format"),
                false,
                Get(loaded, "MipCount"),
                true
            )!;
        try
        {
            type.GetMethod("ReplaceCpuData")!
                .Invoke(
                    texture,
                    new[]
                    {
                        Get(loaded, "Width"),
                        Get(loaded, "Height"),
                        Get(loaded, "Format"),
                        Get(loaded, "Levels"),
                        Get(loaded, "MipCount"),
                        false,
                        false,
                    }
                );
            type.GetProperty("Name")!.SetValue(texture, TerrainTexture);
            // The initial WebGL texture adapter implements nearest/clamp only.
            foreach (var (name, value) in new[] { ("filterMode", "Point"), ("wrapMode", "Clamp") })
            {
                var property = type.GetProperty(name)!;
                property.SetValue(texture, Enum.Parse(property.PropertyType, value));
            }
            var cpu = Get(texture, "CpuData");
            cpu.GetType().GetProperty("Swizzle")!.SetValue(cpu, Get(loaded, "Swizzle"));
            cpu.GetType()
                .GetProperty("AlphaIsPremultiplied")!
                .SetValue(cpu, Get(source, "Premultiplied"));
            return texture;
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }
}
