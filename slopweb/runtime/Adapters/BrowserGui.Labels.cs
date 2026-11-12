// Opt-in label diagnostics own no drawing commands or retained terminal history.
using System.Text.Json;
using UnityEngine;
using UnityStubs.Gui;

namespace BrowserAdapters;

public sealed partial class BrowserGui
{
    static bool observeLabels;
    bool labelPainted;
    BrowserLabelObservations? labels;

    public static bool ConfigureObserveLabels(bool enabled)
    {
        bool previous = observeLabels;
        observeLabels = enabled;
        if (GuiBackend.Current is BrowserGui gui)
            gui.labels = null;
        return previous;
    }

    public static string Labels() =>
        observeLabels && GuiBackend.Current is BrowserGui gui && gui.labels != null
            ? gui.labels.Json()
            : "[]";

    public void BeginLabelRepaint() => labels?.Clear();

    void ObserveLabel(Rect rect, string text, Color color, Rect fineClip)
    {
        if (
            drawingTarget
            || Event.current?.type != EventType.Repaint
            || color.a <= 0
            || string.IsNullOrWhiteSpace(text)
        )
            return;
        var bounds = Bounds(rect);
        if (fineClip.width > 0)
            bounds = LabelIntersection(bounds, Bounds(fineClip));
        if (drawingClips.TryPeek(out var clip))
            bounds = LabelIntersection(bounds, clip);
        if (Screen.width > 0 && Screen.height > 0)
            bounds = LabelIntersection(bounds, new Rect(0, 0, Screen.width, Screen.height));
        if (
            bounds.width <= 0
            || bounds.height <= 0
            || !float.IsFinite(bounds.x)
            || !float.IsFinite(bounds.y)
            || !float.IsFinite(bounds.width)
            || !float.IsFinite(bounds.height)
        )
            return;
        (labels ??= new()).Add(text, bounds);
    }

    static Rect LabelIntersection(Rect a, Rect b)
    {
        float x = Math.Max(a.xMin, b.xMin),
            y = Math.Max(a.yMin, b.yMin);
        return new Rect(
            x,
            y,
            Math.Max(0, Math.Min(a.xMax, b.xMax) - x),
            Math.Max(0, Math.Min(a.yMax, b.yMax) - y)
        );
    }
}

// Keep the latest draw calls, including one-character controls. A terminal's
// many early cells cannot exhaust admission for dialogs drawn later in the pass.
sealed class BrowserLabelObservations
{
    const int MaxEntries = 4096,
        MaxCharacters = 262144,
        MaxTextLength = 4096;
    readonly Queue<Label> entries = new();
    int characters;

    sealed record Label(string text, float x, float y, float width, float height);

    public void Clear()
    {
        entries.Clear();
        characters = 0;
    }

    public void Add(string text, Rect bounds)
    {
        if (text.Length > MaxTextLength)
            text = text[..MaxTextLength];
        while (entries.Count >= MaxEntries || characters + text.Length > MaxCharacters)
            characters -= entries.Dequeue().text.Length;
        entries.Enqueue(new(text, bounds.x, bounds.y, bounds.width, bounds.height));
        characters += text.Length;
    }

    public string Json() => JsonSerializer.Serialize(entries);
}
