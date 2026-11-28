// Track only serialized Canvas state. Native group coordinates and hit testing
// remain with BrowserGui; clips save/restore the transform just as Canvas does.
namespace BrowserAdapters;

internal sealed class BrowserCommandState
{
    float[]? matrix;
    readonly Stack<float[]?> clips = new();

    internal void Append(List<BrowserGui.Command> commands, string kind, object value)
    {
        if (!Accept(kind, value))
            return;
        if (kind == "pop")
        {
            // A clip surrounding only a transform never affected pixels. Canvas
            // restores the pre-clip transform on pop, as Accept has just done.
            int start = commands.Count - 1;
            if (start >= 0 && commands[start].kind == "matrix")
                start--;
            if (start >= 0 && commands[start].kind == "clipScreen")
            {
                commands.RemoveRange(start, commands.Count - start);
                return;
            }
        }
        if (kind == "matrix" && commands.Count > 0 && commands[^1].kind == "matrix")
            commands[^1] = new(kind, value);
        else
            commands.Add(new(kind, value));
    }

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
