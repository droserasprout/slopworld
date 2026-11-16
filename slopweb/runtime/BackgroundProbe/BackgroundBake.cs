using System.Reflection;

// Cold-cache preparation uses the installed mod's pixel math and native CPU
// texture codecs. It never constructs an engine, GPU target, scene, or world.
internal static class BackgroundBake
{
    internal static void EnsureFrames(
        Assembly unity,
        Type bake,
        Type tuning,
        Type textureType,
        object texture,
        object preset,
        int width,
        int height,
        string directory,
        string[] frames
    )
    {
        const BindingFlags flags =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        int limit = (int)tuning.GetField("MaxSide", flags)!.GetValue(null)!;
        int quality = (int)tuning.GetField("JpegQuality", flags)!.GetValue(null)!;
        float scale = Math.Min(1f, (float)limit / Math.Max(width, height));
        int bw = Math.Max(1, (int)MathF.Round(width * scale));
        int bh = Math.Max(1, (int)MathF.Round(height * scale));
        var pixels = textureType.GetMethod("GetPixels", Type.EmptyTypes)!.Invoke(texture, null)!;
        var clean = (Array)
            bake.GetMethod("Downsample", flags)!.Invoke(null, [pixels, width, height, bw, bh])!;
        var fuel = bake.GetMethod("Fuel", flags)!.Invoke(null, [clean, bw, bh])!;
        var stage = bake.GetMethod("Stage", flags)!;
        var colors = Array.CreateInstance(clean.GetType().GetElementType()!, bw * bh);
        var rgb = Enum.Parse(unity.GetType("UnityEngine.TextureFormat", true)!, "RGB24");
        var frame = Activator.CreateInstance(textureType, [bw, bh, rgb, false])!;
        var setPixels = textureType.GetMethod("SetPixels", [colors.GetType()])!;
        var encode = unity
            .GetType("UnityEngine.ImageConversion", true)!
            .GetMethod("EncodeToJPG", [textureType, typeof(int)])!;
        var destroy = unity
            .GetType("UnityEngine.Object", true)!
            .GetMethod("Destroy", [unity.GetType("UnityEngine.Object", true)!])!;
        Directory.CreateDirectory(directory);
        try
        {
            for (int index = 0; index < frames.Length; index++)
            {
                stage.Invoke(null, [preset, clean, colors, fuel, bw, bh, index]);
                setPixels.Invoke(frame, [colors]);
                var bytes = (byte[])encode.Invoke(null, [frame, quality])!;
                if (bytes.Length == 0)
                    throw new InvalidDataException("Background frame encoding returned no bytes");
                string path = Path.Combine(directory, frames[index]);
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllBytes(temporary, bytes);
                    File.Move(temporary, path, true);
                }
                finally
                {
                    File.Delete(temporary);
                }
            }
        }
        finally
        {
            destroy.Invoke(null, [frame]);
        }
    }
}
