using System.Text.Json;
using BrowserAdapters;

PackedQuadTests.Run(args.Length == 2 && args[0] == "--quad-fixture" ? args[1] : null);

// Compare the complete generated packet against the original reflection writer,
// including Unicode/escaping and float values used by clipping and transforms.
float[] rect = [-1.25f, 0, 1280, 720];
float[] color = [1, .125f, 0, .5f];
List<BrowserGui.Command> commands =
[
    new("text", new BrowserGui.TextCommand(rect, "<b>é💡</b>\n\"\\\t", 16, 3, 4, color)),
    new("fill", new BrowserGui.FillCommand(rect, color, 3.5f)),
    new("image", new BrowserGui.ImageCommand(1, 1, 1, "AAECAw==")),
    new("imageTexture", new BrowserGui.TextureCommand(1, rect, [0, 0, 1, 1], color)),
    new(
        "imageBatch",
        new BrowserGui.ImageBatchCommand(1, color, [-1.25f, 0, 1280, 720, 0, 0, 1, 1])
    ),
    new("target", new BrowserGui.TargetCommand(2, 1280, 720)),
    new("targetTexture", new BrowserGui.TextureCommand(2, rect, [0, 0, 1, 1], color)),
    new("matrix", new float[] { 1, 0, 0, 1, -20, 40 }),
    new("clipScreen", rect),
    new("pop", 0),
    new("target", new BrowserGui.TargetCommand(0, 0, 0)),
    new("releaseImage", 1),
    new("releaseTarget", 2),
];
foreach (var packet in new[] { new List<BrowserGui.Command>(), commands })
{
    string expected = JsonSerializer.Serialize(packet);
    string actual = JsonSerializer.Serialize(packet, BrowserGuiJson.Default.ListCommand);
    if (actual != expected || BrowserCommandWriter.Serialize(packet) != expected)
        throw new Exception("Generated command writer changed the wire packet");
}

// Reuse after a failed or oversized packet must start a clean JSON document.
try
{
    BrowserCommandWriter.Serialize([new("matrix", new[] { float.NaN })]);
    throw new Exception("Non-finite drawing coordinate was accepted");
}
catch (ArgumentException) { }
var large = new List<BrowserGui.Command>
{
    new("image", new BrowserGui.ImageCommand(7, 1, 1, new string('a', 2200000))),
};
if (
    BrowserCommandWriter.Serialize(large)
        != JsonSerializer.Serialize(large, BrowserGuiJson.Default.ListCommand)
    || BrowserCommandWriter.Serialize(commands)
        != JsonSerializer.Serialize(commands, BrowserGuiJson.Default.ListCommand)
)
    throw new Exception("Writer did not recover after failed or oversized packets");
Console.WriteLine(
    "Direct and generated packets match the original JSON contract and recover after failed/oversized writes."
);
PacketCacheTests.Run(commands);

var quad = new BrowserGlyphQuad(-2, -3, 10, 9, 0, 0, 1, 1);
if (
    !quad.Clip(1, 7, 6)
    || quad.Left != 1
    || quad.Right != 7
    || quad.Top != -3
    || quad.Bottom != 6
    || quad.U0 != .25f
    || quad.U1 != .75f
    || quad.V0 != 0
    || quad.V1 != .75f
)
    throw new Exception("Glyph clipping lost proportional UVs or native ascender overhang");
foreach (var limits in new[] { (10f, 20f, 20f), (-20f, -2f, 20f), (-20f, 20f, -3f) })
{
    var outside = new BrowserGlyphQuad(-2, -3, 10, 9, 0, 0, 1, 1);
    if (outside.Clip(limits.Item1, limits.Item2, limits.Item3))
        throw new Exception("Fully clipped glyph was retained");
}
Console.WriteLine("Glyph clipping preserves UVs, edge exclusion and ascender overhang.");

var state = new BrowserCommandState();
float[] identity = [1, 0, 0, 1, 0, 0],
    translated = [1, 0, 0, 1, 12, 24];
if (!state.Accept("matrix", identity) || state.Accept("matrix", identity.ToArray()))
    throw new Exception("Unchanged transform was not suppressed");
state.Accept("clipScreen", rect);
state.Accept("matrix", translated);
state.Accept("clipScreen", rect);
state.Accept("matrix", identity);
state.Accept("pop", 0);
if (state.Accept("matrix", translated))
    throw new Exception("Nested clip did not restore its saved transform");
state.Accept("pop", 0);
if (state.Accept("matrix", identity))
    throw new Exception("Outer clip did not restore its saved transform");
state.Clear();
if (!state.Accept("matrix", identity))
    throw new Exception("A new packet must establish its own transform");
Console.WriteLine("Command state restores nested clips and resets across packets.");

var batches = new List<BrowserGui.Command>();
float[] first = [1, 2, 3, 4, 0, .5f, .25f, .5f],
    second = [9, 8, 7, 6, .25f, 0, .5f, 1];
BrowserGui.AppendImageBatch(batches, 1, 1, 1, 1, .5f, first);
BrowserGui.AppendImageBatch(batches, 1, 1, 1, 1, .5f, second);
if (
    batches.Count != 1
    || !((BrowserGui.ImageBatchCommand)batches[0].value).quads.SequenceEqual(first.Concat(second))
)
    throw new Exception("Adjacent glyphs lost order or geometry");
BrowserGui.AppendImageBatch(batches, 1, 1, 1, 1, 1, first);
BrowserGui.AppendImageBatch(batches, 2, 1, 1, 1, 1, first);
batches.Add(new BrowserGui.Command("clipScreen", rect));
BrowserGui.AppendImageBatch(batches, 2, 1, 1, 1, 1, first);
if (batches.Count != 5)
    throw new Exception("Image batch crossed an alpha, image or clip boundary");
Console.WriteLine("Image batches preserve draw order and state boundaries.");

var textCache = new BrowserTextCache<float>(
    new Dictionary<char, float> { ['a'] = 1.25f, ['b'] = 2.5f },
    x => x
);
var textRun = textCache.Get("a\r\nbb\n")!;
if (
    textRun.Width != 5
    || textRun.Lines.Length != 3
    || textRun.Lines[0].Width != 1.25f
    || textRun.Lines[2].Glyphs.Length != 0
    || !ReferenceEquals(textRun, textCache.Get("a\r\nbb\n"))
)
    throw new Exception("Text cache changed line layout or failed to reuse a run");
if (textCache.Get("missing") != null)
    throw new Exception("Text cache claimed unsupported glyph coverage");
var retained = textCache.Get("ab")!;
var editable = retained.Lines[0];
if (
    retained.Measure(x => x, 1) != 4
    || retained.Measure(x => x, 4) != 3.75f
    || retained.Measure(x => x, 1) != 4
)
    throw new Exception(
        "Text measurement differs from per-glyph pixel rounding or retained an old scale"
    );
if (
    !editable.Carets(x => x, 4).SequenceEqual(new[] { 0f, 1.25f, 3.75f })
    || editable.IndexAt(-1, x => x, 4) != 0
    || editable.IndexAt(.624f, x => x, 4) != 0
    || editable.IndexAt(.625f, x => x, 4) != 1
    || editable.IndexAt(2.5f, x => x, 4) != 2
    || editable.IndexAt(100, x => x, 4) != 2
)
    throw new Exception("Proportional caret boundaries do not match glyph advances");
if (!editable.Carets(x => x, 1).SequenceEqual(new[] { 0f, 1f, 4f }))
    throw new Exception("Caret cache retained coordinates from another display density");
for (int i = 1; i < 600; i++)
{
    textCache.Get(new string('a', i));
    if (!ReferenceEquals(retained, textCache.Get("ab")))
        throw new Exception("Text cache evicted a recently used run");
}
if (ReferenceEquals(textRun, textCache.Get("a\r\nbb\n")))
    throw new Exception("Text cache did not bound retained runs");
var oversized = new string('a', 20000);
if (ReferenceEquals(textCache.Get(oversized), textCache.Get(oversized)))
    throw new Exception("Text cache retained an oversized run");
Console.WriteLine("Text runs preserve metrics, coverage and bounded LRU ownership.");

var packetCache = new BrowserQuadCache(2, 4);
float[] packetValues = [0f, 1f];
byte[] packetBytes = [1, 2, 3];
ulong packetHash = BrowserQuadCache.Hash(packetValues);
packetCache.Store(packetHash, packetValues, packetBytes);
packetValues[1] = 9f;
packetBytes[0] = 9;
if (packetCache.Find(packetHash, [0f, 1f]) is not { } retainedPacket || retainedPacket[0] != 1)
    throw new Exception("Packed cache did not detach retained inputs");
if (
    packetCache.Find(packetHash, [-0f, 1f]) != null
    || packetCache.Find(packetHash, [0f, 2f]) != null
)
    throw new Exception("Packed cache accepted a hash collision or different zero sign");
packetCache.Store(2, [2f, 3f], [2]);
packetCache.Find(packetHash, [0f, 1f]);
packetCache.Store(3, [3f, 4f], [3]);
if (packetCache.Find(2, [2f, 3f]) != null || packetCache.Find(packetHash, [0f, 1f]) == null)
    throw new Exception("Packed cache did not retain the recently used packet");
packetCache.Store(packetHash, [7f, 8f], [7]);
if (
    packetCache.Find(packetHash, [0f, 1f]) != null
    || packetCache.Find(packetHash, [7f, 8f]) == null
)
    throw new Exception("Packed cache did not replace a colliding sequence");
packetCache.Store(4, [1f, 2f, 3f, 4f, 5f], [4]);
if (
    packetCache.Count > 2
    || packetCache.RetainedValues > 4
    || packetCache.Find(4, [1f, 2f, 3f, 4f, 5f]) != null
)
    throw new Exception("Packed cache exceeded its retained-value bound");
Console.WriteLine(
    "Packed cache preserves bit identity, collision checks, detached storage and LRU bounds."
);

CommandWriterBenchmark.Run(args.Contains("--benchmark"));
