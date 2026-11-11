// Adjacent texture draws may share one packet only while image, tint, transform,
// clip and target are unchanged. Any intervening command ends the batch.
using System.Runtime.InteropServices;

namespace BrowserAdapters;

public sealed partial class BrowserGui
{
    internal static void AppendImageBatch(
        List<Command> commands,
        int id,
        float red,
        float green,
        float blue,
        float alpha,
        ReadOnlySpan<float> quad
    )
    {
        ImageBatchCommand? batch =
            commands.Count > 0 && commands[^1].kind == "imageBatch"
                ? (ImageBatchCommand)commands[^1].value
                : null;
        if (
            batch == null
            || batch.id != id
            || batch.color[0] != red
            || batch.color[1] != green
            || batch.color[2] != blue
            || batch.color[3] != alpha
            || batch.quads.Count >= 8192
        )
        {
            batch = new ImageBatchCommand(id, [red, green, blue, alpha], new List<float>(64));
            commands.Add(new Command("imageBatch", batch));
        }
        int start = batch.quads.Count;
        CollectionsMarshal.SetCount(batch.quads, start + quad.Length);
        quad.CopyTo(CollectionsMarshal.AsSpan(batch.quads).Slice(start));
    }
}
