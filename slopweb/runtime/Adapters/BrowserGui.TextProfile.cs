// Optional repaint-only text attribution; native layout and emission remain unchanged.
namespace BrowserAdapters;

public sealed partial class BrowserGui
{
    public long ProfileClippedTailTraceTicks,
        ProfileBakedTicks,
        ProfileBakedPrepareTicks,
        ProfileBakedDrawTicks,
        ProfileNativeTicks;
    public int ProfileBakedRuns,
        ProfileBakedCharacters,
        ProfileNativeRuns,
        ProfileFallbackRuns;

    public void ResetTextProfile()
    {
        ProfileClippedTailTraceTicks = 0;
        ProfileBakedTicks =
            ProfileBakedPrepareTicks =
            ProfileBakedDrawTicks =
            ProfileNativeTicks =
                0;
        ProfileBakedRuns = ProfileBakedCharacters = ProfileNativeRuns = ProfileFallbackRuns = 0;
    }
}
