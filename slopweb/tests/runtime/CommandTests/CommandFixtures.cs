// Numeric reference packets exist only to verify the production packed writer.
using System.Text.Json.Serialization;

namespace BrowserAdapters;

public sealed partial class BrowserGui
{
    internal sealed record NumericImageBatchCommand(int id, float[] color, List<float> quads);
}

[JsonSerializable(typeof(List<BrowserGui.Command>))]
[JsonSerializable(typeof(BrowserGui.NumericImageBatchCommand))]
internal partial class CommandFixtureJson : JsonSerializerContext { }
