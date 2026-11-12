// Browser frame scheduling observes transport activity only after it is published.
// This callback must only schedule work, never reenter the native update loop.
namespace BrowserAdapters;

public static class BrowserActivity
{
    public static Action? Wake;

    public static void Notify() => Wake?.Invoke();
}
