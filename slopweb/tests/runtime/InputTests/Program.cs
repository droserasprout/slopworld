using System.Text;
using BrowserAdapters;
using UnityEngine;

// Named keys and paste flush preceding literal input through native owners. The
// final callback must retain trailing UTF-16 pairs and run once before any paint.
foreach (bool profile in new[] { false, true })
{
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
Console.WriteLine("Browser input batch ordering, UTF-16 and repeated-frame checks passed.");
