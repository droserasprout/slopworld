using System.Text;
using System.Text.Json;
using BrowserAdapters;
using UnityEngine;

if (!BrowserGuiInput.ConfigureDiscardedDrawing(true))
    throw new Exception("Discarded drawing must be suppressed by default");

// Named keys and paste flush preceding literal input through native owners. The
// final callback must retain trailing UTF-16 pairs and run once before any paint.
foreach (bool skip in new[] { true, false })
foreach (bool profile in new[] { false, true })
{
    BrowserGuiInput.ConfigureDiscardedDrawing(skip);
    BrowserGuiInput.Profile = profile;
    List<string> trace = [];
    var pending = new StringBuilder();
    void Flush()
    {
        if (pending.Length == 0)
            return;
        // A split surrogate pair would become replacement bytes on the wire.
        trace.Add("send:" + Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(pending.ToString())));
        pending.Clear();
    }
    var gui = new BrowserGui { OnDrain = () => trace.Add("drain") };
    BrowserClipboard.OnSupply = text =>
    {
        Flush();
        trace.Add("paste:" + text);
    };
    void Draw()
    {
        var ev = Event.current;
        if (gui.SuppressDrawing != (skip && ev.type != EventType.Repaint))
            throw new Exception("Discarded-pass suppression crossed the repaint boundary");
        if (ev.type == EventType.KeyDown)
        {
            if (ev.character != '\0')
                pending.Append(ev.character);
            else if (ev.keyCode == KeyCode.Return)
            {
                Flush();
                trace.Add("key:Return");
            }
        }
        else
            trace.Add(ev.type.ToString());
    }
    void EndInput()
    {
        trace.Add("endInput");
        Flush();
    }
    BrowserGuiInput.Dispatch(
        """
        [{"kind":"text","character":97},
         {"kind":"key","key":"Return"},
         {"kind":"text","character":98},
         {"kind":"clipboard","text":"pasted"},
         {"kind":"text","character":55357},
         {"kind":"text","character":56481}]
        """,
        gui,
        Draw,
        EndInput
    );
    AssertTrace(
        "send:a",
        "key:Return",
        "send:b",
        "paste:pasted",
        "endInput",
        "send:💡",
        "Layout",
        "Repaint",
        "drain"
    );
    trace.Clear();
    BrowserGuiInput.Dispatch("[]", gui, Draw, EndInput);
    AssertTrace("endInput", "Layout", "Repaint", "drain");
    trace.Clear();
    // Other GUI probes need no terminal callback.
    BrowserGuiInput.Dispatch("[]", gui, Draw);
    AssertTrace("Layout", "Repaint", "drain");
    trace.Clear();
    // Sending is independent of repaint success: no trailing outer-frame flush
    // is needed, and the following frame cannot resend completed literal input.
    try
    {
        BrowserGuiInput.Dispatch(
            "[{\"kind\":\"text\",\"character\":99}]",
            gui,
            () =>
            {
                if (Event.current.type == EventType.Repaint)
                    throw new InvalidOperationException("paint failed");
                Draw();
            },
            EndInput
        );
        throw new Exception("Expected repaint failure");
    }
    catch (InvalidOperationException e) when (e.Message == "paint failed") { }
    AssertTrace("endInput", "send:c", "Layout");
    trace.Clear();
    BrowserGuiInput.Dispatch("[]", gui, Draw, EndInput);
    AssertTrace("endInput", "Layout", "Repaint", "drain");

    void AssertTrace(params string[] expected)
    {
        if (!trace.SequenceEqual(expected))
            throw new Exception(
                $"Expected {string.Join(", ", expected)}; got {string.Join(", ", trace)}"
            );
    }
}
BrowserGuiInput.ConfigureDiscardedDrawing(true);
var failedGui = new BrowserGui();
try
{
    BrowserGuiInput.Dispatch(
        "[]",
        failedGui,
        () => throw new InvalidOperationException("layout failed")
    );
}
catch (InvalidOperationException e) when (e.Message == "layout failed") { }
if (failedGui.SuppressDrawing)
    throw new Exception("Failed layout leaked drawing suppression");
using var empty = JsonDocument.Parse("[]");
var packetGui = new BrowserGui { PacketId = 9 };
foreach (bool profile in new[] { false, true })
{
    BrowserGuiInput.Profile = profile;
    foreach (double retained in new double[] { -1, 0, 8, 9 })
    {
        string packet = BrowserGuiInput.DispatchBatch(
            empty.RootElement,
            packetGui,
            () => { },
            null,
            retained,
            false
        );
        using var frame = JsonDocument.Parse(packet);
        var envelope = frame.RootElement;
        if (
            envelope.TryGetProperty("commands", out _) != (retained != 9)
            || envelope.TryGetProperty("repeat", out _) != (retained == 9)
            || envelope.TryGetProperty("packet", out _) != (retained is 0 or 8)
            || envelope.TryGetProperty("profile", out _) != profile
        )
            throw new Exception("Incorrect frame for acknowledged packet " + retained);
    }
}
Console.WriteLine("Browser input batch ordering, UTF-16 and repeated-frame checks passed.");
