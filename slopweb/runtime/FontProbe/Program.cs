// Host-only probe of the installed rasterizer. No window, renderer, or colony.
// Output is local derived font data and must never enter a release payload.
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using Brutal.ImGuiApi;

if (args.Length is not (5 or 6 or 7))
    throw new ArgumentException(
        "GAME FONT OUTPUT SIZE DENSITY [STYLE [SCALARS_JSON]] or GAME --resolve MOD FAMILY OUTPUT_DIRECTORY"
    );
string game = Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
    File.Exists(Path.Combine(game, name.Name + ".dll"))
        ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, name.Name + ".dll"))
        : null;
Execute(args, game);

// Register resolution before the JIT touches signatures from installed game
// libraries. The packaged helper deliberately carries none of those libraries.
[MethodImpl(MethodImplOptions.NoInlining)]
static void Execute(string[] args, string game)
{
    if (args[1] == "--loader-flags")
    {
        var host = Assembly
            .LoadFrom(Path.Combine(game, "RimWorld.dll"))
            .GetType("RimWorldWaylith.Imgui.ImGuiHost", true)!;
        uint flags = (uint)
            host.GetMethod("LoaderFlagsFor", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [0])!;
        Directory.CreateDirectory(args[4]);
        File.WriteAllText(
            Path.Combine(args[4], "font-config.json"),
            JsonSerializer.Serialize(new { loaderFlags = flags })
        );
        return;
    }
    if (args[1] == "--catalog")
    {
        var mod = Assembly.LoadFrom(Path.GetFullPath(args[2]));
        var names = mod.GetType("SlopWorld.NativeFonts", true)!.GetMethod("Names")!;
        Directory.CreateDirectory(args[4]);
        File.WriteAllText(
            Path.Combine(args[4], "catalog.json"),
            JsonSerializer.Serialize(
                new
                {
                    all = (string[])names.Invoke(null, [false])!,
                    mono = (string[])names.Invoke(null, [true])!,
                }
            )
        );
        return;
    }
    if (args[1] == "--fallback")
    {
        FontDelivery.Deliver(args[2], args[3], args[4], game);
        return;
    }
    if (args[1] is "--resolve" or "--resolve-ui")
    {
        // Resolve using the same mod catalog and fontconfig policy as the desktop.
        // This reads fonts only; it creates no GUI, game, world, or renderer.
        var mod = Assembly.LoadFrom(Path.GetFullPath(args[2]));
        bool ui = args[1] == "--resolve-ui";
        var families = ui
            ? (
                args[3].Length > 0
                    ? new[] { args[3] }
                    : (string[])
                        mod.GetType("SlopWorld.UiFont", true)!
                            .GetField("Candidates", BindingFlags.Static | BindingFlags.NonPublic)!
                            .GetValue(null)!
            )
            : (string[])
                mod.GetType("SlopWorld.FontconfigFonts", true)!
                    .GetMethod("Families")!
                    .Invoke(null, [args[3]])!;
        var catalog = mod.GetType("SlopWorld.NativeFonts", true)!
            .GetMethod("Scan", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, null)!;
        var paths =
            (Dictionary<string, (string Path, int Face)>)
                catalog
                    .GetType()
                    .GetField("Paths", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(catalog)!;
        string selected = families.First(paths.ContainsKey);
        var source = paths[selected];
        var bytes = (byte[])
            mod.GetType("SlopWorld.OpenTypeCollection", true)!
                .GetMethod(
                    "Extract",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                )!
                .Invoke(null, [File.ReadAllBytes(source.Path), source.Face])!;
        Directory.CreateDirectory(args[4]);
        File.WriteAllBytes(Path.Combine(args[4], ui ? "ui.ttf" : "terminal.ttf"), bytes);
        var host = Assembly
            .LoadFrom(Path.Combine(game, "RimWorld.dll"))
            .GetType("RimWorldWaylith.Imgui.ImGuiHost", true)!;
        uint flags = (uint)
            host.GetMethod("LoaderFlagsFor", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [0])!;
        File.WriteAllText(
            Path.Combine(args[4], "font-config.json"),
            JsonSerializer.Serialize(new { loaderFlags = flags, family = selected })
        );
        File.WriteAllText(
            Path.Combine(args[4], ui ? "ui-config.json" : "terminal-config.json"),
            JsonSerializer.Serialize(new { loaderFlags = flags, family = selected })
        );
        if (!ui)
            File.WriteAllText(
                Path.Combine(args[4], "chains.json"),
                JsonSerializer.Serialize(new Dictionary<string, string[]> { [args[3]] = families })
            );
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    selected,
                    source.Path,
                    source.Face,
                    family = args[3],
                }
            )
        );
        return;
    }
    NativeLibrary.SetDllImportResolver(
        typeof(ImGui).Assembly,
        (name, _, _) => NativeLibrary.Load(Path.Combine(game, "lib" + name + ".so"))
    );
    Run(
        game,
        args[1],
        args[2],
        float.Parse(args[3], CultureInfo.InvariantCulture),
        float.Parse(args[4], CultureInfo.InvariantCulture),
        args.Length >= 6 ? int.Parse(args[5], CultureInfo.InvariantCulture) : 0,
        args.Length == 7 ? JsonSerializer.Deserialize<uint[]>(File.ReadAllText(args[6]))! : []
    );
}

static unsafe void Run(
    string game,
    string fontPath,
    string output,
    float size,
    float density,
    int style,
    uint[] scalars
)
{
    if (style is < 0 or > 3)
        throw new ArgumentOutOfRangeException(nameof(style));
    if (!float.IsFinite(size) || size <= 0 || size > 128)
        throw new ArgumentOutOfRangeException(nameof(size));
    if (!float.IsFinite(density) || density < .5f || density > 4f)
        throw new ArgumentOutOfRangeException(nameof(density));
    var assembly = Assembly.LoadFrom(Path.Combine(game, "RimWorld.dll"));
    var host = assembly.GetType("RimWorldWaylith.Imgui.ImGuiHost", true)!;
    var context = ImGui.CreateContext();
    try
    {
        var io = ImGui.GetIO();
        var library = NativeLibrary.Load(Path.Combine(game, "libimgui_freetype.so"));
        var loader = (delegate* unmanaged<ImFontLoader*>)
            NativeLibrary.GetExport(library, "GetFontLoader");
        io.Fonts.SetFontLoader(loader());
        // Ask the installed game for its config instead of reproducing its flags.
        const BindingFlags privateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        uint flags = (uint)host.GetMethod("LoaderFlagsFor", privateStatic)!.Invoke(null, [0])!;
        // Rendering mode and FontStyle are different enums. Match StyledFont's
        // synthetic styles on the selected source using the installed constants.
        if ((style & 1) != 0)
            flags |= (uint)host.GetField("FreeTypeBold", privateStatic)!.GetRawConstantValue()!;
        if ((style & 2) != 0)
            flags |= (uint)host.GetField("FreeTypeOblique", privateStatic)!.GetRawConstantValue()!;
        var config = (ImFontConfig)
            host.GetMethod("NewFontConfig", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [flags])!;
        var font = io.Fonts.AddFontFromFileTTF(fontPath, size, new ImFontConfigPtr(&config));
        host.GetMethod("KeepFractionalAdvances", privateStatic)!.Invoke(null, [io]);
        var baked = font.GetFontBaked(size, density);
        var metricsType = assembly.GetType("RimWorldWaylith.Imgui.FaceMetrics", true)!;
        var metrics = metricsType
            .GetMethod("Load", [typeof(string), typeof(ImFontPtr), typeof(float)])!
            .Invoke(null, [fontPath, font, size]);
        var lineMetrics = metricsType.GetMethod("At")!.Invoke(metrics, [size]);
        var glyphs = new List<object>();
        // The caller supplies observed text, never Unicode blocks. An empty request
        // prepares only native face metrics for startup and font selection.
        if (
            scalars.Length > 65536
            || scalars.Any(c => c > 0x10FFFF || c is >= 0xD800 and <= 0xDFFF)
        )
            throw new ArgumentException("Invalid Unicode scalars");
        scalars = scalars.Distinct().Order().ToArray();
        // Complete rasterization before reading UVs: atlas growth can relocate glyphs.
        foreach (uint scalar in scalars)
            baked.FindGlyph(scalar);
        foreach (uint scalar in scalars)
        {
            var glyph = baked.FindGlyph(scalar);
            if (glyph.IsNull())
                throw new InvalidOperationException("Missing glyph " + scalar);
            glyphs.Add(
                new
                {
                    scalar,
                    advance = glyph.AdvanceX,
                    x0 = glyph.X0,
                    y0 = glyph.Y0,
                    x1 = glyph.X1,
                    y1 = glyph.Y1,
                    u0 = glyph.U0,
                    v0 = glyph.V0,
                    u1 = glyph.U1,
                    v1 = glyph.V1,
                    // The installed binding aliases C bitfields as raw uints;
                    // Codepoint is not a decoded scalar. Query coverage natively.
                    fallback = baked.FindGlyphNoFallback(scalar).IsNull(),
                    visible = (glyph.Visible & 2) != 0,
                }
            );
        }
        var texture = io.Fonts.TexData;
        if (texture.IsNull() || texture.Width <= 0 || texture.Height <= 0)
            throw new InvalidOperationException("Rasterizer did not produce an atlas");
        byte* pixels = texture.Pixels;
        byte[] atlas = new ReadOnlySpan<byte>(
            pixels,
            checked(texture.Width * texture.Height * texture.BytesPerPixel)
        ).ToArray();
        File.WriteAllText(
            output,
            JsonSerializer.Serialize(
                new
                {
                    fontPath,
                    size,
                    density,
                    style,
                    loaderFlags = flags,
                    rasterizerDensity = baked.RasterizerDensity,
                    bakedSize = baked.Size,
                    lineMetrics,
                    ascent = baked.Ascent,
                    descent = baked.Descent,
                    glyphs,
                    atlas = new
                    {
                        width = texture.Width,
                        height = texture.Height,
                        bytesPerPixel = texture.BytesPerPixel,
                        pixels = Convert.ToBase64String(atlas),
                    },
                },
                FontJson.Options
            )
        );
        Console.WriteLine("Installed rasterizer metrics written to " + output);
    }
    finally
    {
        ImGui.DestroyContext(context);
    }
}

file static class FontJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        IncludeFields = true,
    };
}
