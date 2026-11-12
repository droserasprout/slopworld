using System.Text.Json;
using BrowserAdapters;

static void Check(bool condition, string behavior)
{
    if (!condition)
        throw new Exception(behavior);
    Console.WriteLine(behavior);
}

static int Request(string kind)
{
    using var json = JsonDocument.Parse(BrowserClipboard.Take());
    var request = json.RootElement.EnumerateArray().Single();
    Check(request.GetProperty("kind").GetString() == kind, $"request kind {kind}");
    return request.GetProperty("id").GetInt32();
}

string text = "";
int images = 0;
int failures = 0;
byte[] png = [137, 80, 78, 71, 13, 10, 26, 10];
Action<byte[]> image = bytes =>
{
    Check(bytes.SequenceEqual(png), "PNG bytes preserved");
    images++;
};
BrowserClipboard.Supply("text wins", true);
BrowserClipboard.ReadLocalPaste(value => text = value, image, null);
Check(text == "text wins" && BrowserClipboard.Take() == "[]", "text precedes offered image");

BrowserClipboard.Supply("", true);
BrowserClipboard.ReadLocalPaste(value => text = value, null, null);
Check(text == "" && BrowserClipboard.Take() == "[]", "host text-only never requests image");

// Model the native pane/run guard: retain the original request callback even if
// the selected pane changes, and discard a reply after that owner is rebound.
int paneEpoch = 1;
int selectedPane = 1;
int deliveredPane = 0;
BrowserClipboard.Supply("", true);
BrowserClipboard.ReadLocalPaste(
    _ => throw new Exception("unexpected text"),
    bytes =>
    {
        if (paneEpoch == 1)
        {
            deliveredPane = 1;
            image(bytes);
        }
    },
    _ => failures++
);
int first = Request("eventImage");
selectedPane = 2;
BrowserClipboard.CompleteImage(first, Convert.ToBase64String(png), "");
BrowserClipboard.CompleteImage(first, Convert.ToBase64String(png), "");
Check(
    selectedPane == 2 && deliveredPane == 1 && images == 1,
    "late PNG stays with original callback and settles once"
);

BrowserClipboard.Supply("", true);
BrowserClipboard.ReadLocalPaste(
    _ => { },
    bytes =>
    {
        if (paneEpoch == 1)
            image(bytes);
    },
    null
);
int stale = Request("eventImage");
paneEpoch++;
BrowserClipboard.CompleteImage(stale, Convert.ToBase64String(png), "");
Check(images == 1, "retained native callback can reject stale pane/run");

BrowserClipboard.ReadLocalPaste(_ => { }, image, _ => failures++);
int malformed = Request("read");
BrowserClipboard.CompleteImage(malformed, "invalid!", "");
BrowserClipboard.CompleteImage(malformed, "invalid!", "");
Check(failures == 1, "malformed image fails once");

BrowserClipboard.Supply("old selection");
BrowserClipboard.EndEvent();
BrowserClipboard.ReadLocal(false, _ => throw new Exception("unexpected text"), _ => failures++);
int denied = Request("read");
BrowserClipboard.Complete(denied, "", "denied");
Check(BrowserClipboard.Get() == "" && failures == 2, "denied read clears stale fallback text");

BrowserClipboard.CopyPrimary("primary", null, null);
BrowserClipboard.ReadLocal(true, value => text = value, null);
Check(text == "primary" && BrowserClipboard.Take() == "[]", "PRIMARY remains local text only");
