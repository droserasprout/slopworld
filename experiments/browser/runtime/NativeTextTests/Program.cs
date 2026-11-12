// Exercise the installed parser without constructing an engine or loading a save.
using System.Reflection;
using System.Runtime.Loader;
using BrowserAdapters;

string game = Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
    File.Exists(Path.Combine(game, name.Name + ".dll"))
        ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, name.Name + ".dll"))
        : null;
Assembly.LoadFrom(
    args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal)
        ? Path.GetFullPath(args[1])
        : Path.Combine(game, "RimWorld.dll")
);
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
if (args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal))
    LayoutChecks.Run();

FontChecks.Run();
ImageChecks.Run();
GlyphChecks.Run(args.Contains("--glyph-benchmark"));
