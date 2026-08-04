using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The trophy icon on the About tab row, baked from the trophy emoji by
    // tools/emoji.py. The icon is a white alpha mask on a transparent background,
    // the same shape all the procedural icons here are, so it is tinted by the
    // caller. Loaded lazily and cached for the lifetime of the process.
    [StaticConstructorOnStartup]
    public static class TrophyIcon
    {
        const string Path = "SlopWorld/FileIcons/trophy";

        static Texture2D _tex;

        public static Texture2D Tex => _tex != null ? _tex
            : _tex = ContentFinder<Texture2D>.Get(Path, false);
    }
}