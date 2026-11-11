// Optional repaint-only text attribution; native layout and emission remain unchanged.
namespace BrowserAdapters;

public sealed partial class BrowserGui
{
    public long ProfileBakedTicks,
        ProfileNativeTicks;
    public int ProfileBakedRuns,
        ProfileBakedCharacters,
        ProfileNativeRuns,
        ProfileFallbackRuns;

    public void ResetTextProfile()
    {
        ProfileBakedTicks = ProfileNativeTicks = 0;
        ProfileBakedRuns = ProfileBakedCharacters = ProfileNativeRuns = ProfileFallbackRuns = 0;
    }
}
