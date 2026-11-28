// Exercise the installed parser without constructing an engine or loading a save.
using System.Reflection;
using System.Runtime.Loader;
using BrowserAdapters;
using Mono.Cecil;

string game = Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
    File.Exists(Path.Combine(game, name.Name + ".dll"))
        ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, name.Name + ".dll"))
        : null;
var layoutPath =
    args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal)
        ? Path.GetFullPath(args[1])
        : Path.Combine(game, "RimWorld.dll");
using (var resolver = new DefaultAssemblyResolver())
{
    resolver.AddSearchDirectory(game);
    resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));
    using var assembly = AssemblyDefinition.ReadAssembly(
        layoutPath,
        new ReaderParameters { AssemblyResolver = resolver }
    );
    BrowserTextLayoutPatch.Apply(assembly, AppContext.BaseDirectory);
    using var patched = new MemoryStream();
    assembly.Write(patched);
    patched.Position = 0;
    AssemblyLoadContext.Default.LoadFromStream(patched);
}
string[] plain = ["plain text", "a < b", "<", "<unknown>name</unknown>", "List<T>", "<<unfinished"];
string[] markup =
[
    "<b>bold</b>",
    "<I>italic</I>",
    "<color=red>red</color>",
    "<size=20>large</size>",
    "<color=invalid>text",
    "<size=invalid>text",
    "a < b <b>bold",
    "</b>",
];
foreach (string text in plain)
    if (BrowserNativeText.HasMarkup(text))
        throw new Exception("Literal text treated as markup: " + text);
foreach (string text in markup)
    if (!BrowserNativeText.HasMarkup(text))
        throw new Exception("Native markup treated as literal: " + text);
Console.WriteLine(
    $"Installed parser preserved {plain.Length} literal and {markup.Length} markup cases."
);
LayoutChecks.Run();

TestFont.Run(LocalFontChecks.Run);
ImageChecks.Run();
TestFont.Run(font => GlyphChecks.Run(font, args.Contains("--glyph-benchmark")));
