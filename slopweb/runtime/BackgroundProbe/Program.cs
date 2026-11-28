// Host-only background cache preparation. Installed readers and mod cache policy
// own source identity; no engine, renderer, world or save is initialized here.
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

if (args.Length != 3)
    throw new ArgumentException("GAME MOD OUTPUT");
string game = Path.GetFullPath(args[0]);
string modPath = Path.GetFullPath(args[1]);
string output = Path.GetFullPath(args[2]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    foreach (string directory in new[] { game, Path.GetDirectoryName(modPath)! })
    {
        string path = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(path))
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    }
    return null;
};
var waylith = Assembly.LoadFrom(Path.Combine(game, "Waylith.dll"));
var unity = Assembly.LoadFrom(Path.Combine(game, "UnityStubs.dll"));
var mod = Assembly.LoadFrom(modPath);
const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
const string sourceName = "Resources/Textures/UI/HeroArt/BGPlanet.png";
using var pack = (IDisposable)
    waylith
        .GetType("Waylith.Assets.AssetPack", true)!
        .GetMethod("TryOpen")!
        .Invoke(null, [Path.Combine(game, "Assets.zip")])!;
var content = waylith.GetType("Waylith.Assets.ContentPack", true)!;
content.GetMethod("Use")!.Invoke(null, [pack, game, new Dictionary<string, string>()]);
object?[] read = [pack, sourceName + ".dds.split", null];
if (!(bool)content.GetMethod("TryReadTexture")!.Invoke(null, read)!)
    throw new InvalidDataException("Installed BGPlanet source is missing");
var sourceType = waylith.GetType("Waylith.Graphics.Textures.PackTextureSource", true)!;
object?[] create = [read[2], null];
if (
    !(bool)
        sourceType
            .GetMethods()
            .Single(m => m.Name == "TryCreate" && m.GetParameters().Length == 2)
            .Invoke(null, create)!
)
    throw new InvalidDataException("Installed BGPlanet DDS is invalid");
using var source = (IDisposable)create[1]!;
int width = (int)sourceType.GetProperty("Width")!.GetValue(source)!;
int height = (int)sourceType.GetProperty("Height")!.GetValue(source)!;
var coreType = waylith.GetType("Waylith.Rendering.Texture2D", true)!;
using var core = (IDisposable)
    Activator.CreateInstance(
        coreType,
        width,
        height,
        sourceType.GetProperty("Format")!.GetValue(source),
        false,
        1,
        true
    )!;
coreType.GetProperty("SourcePath")!.SetValue(core, Path.Combine(game, sourceName));
var textureType = unity.GetType("UnityEngine.Texture2D", true)!;
var texture = Activator.CreateInstance(
    textureType,
    BindingFlags.Instance | BindingFlags.NonPublic,
    null,
    [core],
    null
)!;
var presets = mod.GetType("SlopWorld.MenuBackgroundPresets", true)!;
var preset = presets.GetProperty("Chosen", Static)!.GetValue(null)!;
var bake = mod.GetType("SlopWorld.MenuBackgroundBake", true)!;
string key = (string)bake.GetMethod("Key", Static)!.Invoke(null, [texture, preset])!;
string directory = (string)bake.GetMethod("Dir", Static)!.Invoke(null, [key])!;
int total = (int)preset.GetType().GetProperty("Total")!.GetValue(preset)!;
var frames = Enumerable.Range(0, total).Select(i => $"{i:D2}.jpg").ToArray();
bool cached = frames.All(name => File.Exists(Path.Combine(directory, name)));
Directory.CreateDirectory(output);

var tuning = mod.GetType("SlopWorld.MenuBackgroundTuning", true)!;
object Tune(string name) => tuning.GetField(name, Static)!.GetValue(null)!;
var offset = Tune("LayerOffset");

// Bake the exact installed source on a cold host; never borrow another source's
// cache or require the desktop game to have run first.
if (!cached)
{
    // Decode the original packed source without creating an engine or GPU target.
    object?[] load = [null];
    if (!(bool)sourceType.GetMethod("TryReadAll")!.Invoke(source, load)!)
        throw new InvalidDataException("Installed background pixels could not be read");
    object loaded = load[0]!;
    object Get(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value)!;
    coreType
        .GetMethod("ReplaceCpuData")!
        .Invoke(
            core,
            [
                Get(loaded, "Width"),
                Get(loaded, "Height"),
                Get(loaded, "Format"),
                Get(loaded, "Levels"),
                Get(loaded, "MipCount"),
                false,
                false,
            ]
        );
    var cpu = Get(core, "CpuData");
    cpu.GetType().GetProperty("Swizzle")!.SetValue(cpu, Get(loaded, "Swizzle"));
    cpu.GetType().GetProperty("AlphaIsPremultiplied")!.SetValue(cpu, Get(source, "Premultiplied"));
    BackgroundBake.EnsureFrames(
        unity,
        bake,
        tuning,
        textureType,
        texture,
        preset,
        width,
        height,
        directory,
        frames
    );
}

Directory.CreateDirectory(Path.Combine(output, key));
foreach (string name in frames)
    File.Copy(Path.Combine(directory, name), Path.Combine(output, key, name), true);

// Native onset frame zero contains the downsampled original, before any grading.
var publishedFrames = frames.Select(name => key + "/" + name).ToArray();

// The first response contains enough frames to play the intro and then loop
// indefinitely while the remaining depths arrive.
int onset = (int)Tune("Onset");
int phases = (int)preset.GetType().GetField("Phases")!.GetValue(preset)!;
int starter = Math.Min(onset + phases, frames.Length);
var framePacks = new List<object>();
foreach (var group in new[] { frames[..starter], frames[starter..] })
{
    if (group.Length == 0)
        continue;
    string file = $"{key}/frames-{framePacks.Count}.bin";
    using var packFile = File.Create(Path.Combine(output, file));
    var lengths = new List<long>();
    foreach (string frameName in group)
    {
        using var jpeg = File.OpenRead(Path.Combine(directory, frameName));
        lengths.Add(jpeg.Length);
        jpeg.CopyTo(packFile);
    }
    framePacks.Add(new { file, lengths });
}
var report = new
{
    source = sourceName,
    original = publishedFrames[0],
    width,
    height,
    key,
    cached = true,
    frames = publishedFrames,
    framePacks,
    animation = new
    {
        onset,
        onsetSeconds = Tune("OnsetSecs"),
        step = Tune("WalkStep"),
        pull = Tune("WalkPull"),
        jitter = Tune("WalkJitter"),
        gapMax = Tune("WalkGapMax"),
        phaseSeconds = Tune("PhaseSecs"),
        depths = preset.GetType().GetField("Depths")!.GetValue(preset),
        phases,
        offsetX = offset.GetType().GetField("x")!.GetValue(offset),
        offsetY = offset.GetType().GetField("y")!.GetValue(offset),
    },
};
string manifest = Path.Combine(output, "manifest.json");
File.WriteAllText(manifest + ".tmp", JsonSerializer.Serialize(report));
File.Move(manifest + ".tmp", manifest, true);
Console.WriteLine(
    JsonSerializer.Serialize(
        new
        {
            source = sourceName,
            width,
            height,
            key,
            cached = true,
            frames = total,
        }
    )
);
