using System;

namespace SlopWorld
{
    // Own both Unity pacing values together, saving the game's pair only when taking over.
    public sealed class FramePolicy
    {
        public const string Game = "game", Sync = "sync", Limit = "limit";
        bool _owned;
        int _savedTarget, _savedSync;

        public static string Normalize(string mode) => mode == Sync || mode == Limit ? mode : Game;
        public static int Clamp(int fps) => Math.Max(30, Math.Min(360, fps));
        public static string Label(string mode) => Normalize(mode) == Sync ? "Sync to display"
            : Normalize(mode) == Limit ? "FPS limit" : "Game default";

        public bool Follow(bool focused, string mode, int fps, ref int target, ref int sync)
        {
            mode = Normalize(mode);
            bool own = !focused || mode != Game;
            if (!own && !_owned) return false;
            if (!_owned)
            {
                _savedTarget = target;
                _savedSync = sync;
            }
            int nextTarget = !own ? _savedTarget : !focused ? 15 : mode == Sync ? -1 : Clamp(fps);
            int nextSync = !own ? _savedSync : focused && mode == Sync ? 1 : 0;
            _owned = own;
            bool changed = target != nextTarget || sync != nextSync;
            target = nextTarget;
            sync = nextSync;
            return changed;
        }
    }
}
