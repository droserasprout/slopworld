// Track only serialized Canvas state. Native group coordinates and hit testing
// remain with BrowserGui; clips save/restore the transform just as Canvas does.
namespace BrowserAdapters;

internal sealed class BrowserCommandState
{
    float[]? matrix;
    readonly Stack<float[]?> clips = new();

    internal bool Accept(string kind, object value)
    {
        switch (kind)
        {
            case "matrix":
                var next = (float[])value;
                if (matrix != null && matrix.AsSpan().SequenceEqual(next))
                    return false;
                matrix = next;
                break;
            case "clipScreen":
                clips.Push(matrix);
                break;
            case "pop":
                matrix = clips.TryPop(out var saved) ? saved : null;
                break;
        }
        return true;
    }

    internal void Clear()
    {
        matrix = null;
        clips.Clear();
    }
}
