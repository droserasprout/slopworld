using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using static SlopWorld.MenuBackgroundTuning;

namespace SlopWorld
{
    // Draws a ramp plus a Depths x Phases frame grid; see mod-background.md.
    [StaticConstructorOnStartup]
    public static class MenuBackground
    {
        // Preset parameters and the frame set shape; adding one also requires bake support.
        internal sealed class Preset
        {
            public readonly string Name;
            // A single depth has no moving layer.
            public readonly int Depths;
            public readonly int Phases;
            // Closed motion must be played in phase order; independent noise does not.
            public readonly bool Closed;

            public Preset(string name, int depths, int phases, bool closed)
            {
                Name = name; Depths = depths; Phases = phases; Closed = closed;
            }

            public int Total => Onset + Depths * Phases;
        }

        static readonly Preset Rotting = new Preset("rot", 5, 8, false);
        internal static readonly Preset Sparkling = new Preset("glow", 1, 24, true);

        static Preset Chosen => Settings.GrandmaMode ? Sparkling : Rotting;

        // The resident frame set and its source are runtime layout state; animation never needs
        // to know how the set was loaded or which cache directory produced it.
        struct LayoutState
        {
            public Texture2D[] Frames;
            public Texture2D Source;
            public string SourceKey;
            public Preset Preset;
        }

        // The ramp and phase walk are one small state machine. Keeping them together prevents
        // a preset reload from accidentally preserving half of the old animation.
        struct AnimationState
        {
            public float Began;
            public float Depth;
            public int Phase;
            public float WalkAt;
            public float PhaseAt;
            public System.Random WalkRng;
        }

        static LayoutState _layout;
        static AnimationState _animation = new AnimationState
        {
            Began = -1f,
            WalkRng = new System.Random(),
        };

        // These aliases keep the animation readable while making ownership explicit in the two
        // state structs above.
        static Texture2D[] _frames { get => _layout.Frames; set => _layout.Frames = value; }
        static Texture2D _src { get => _layout.Source; set => _layout.Source = value; }
        static string _srcKey { get => _layout.SourceKey; set => _layout.SourceKey = value; }
        static Preset _preset { get => _layout.Preset; set => _layout.Preset = value; }
        static float _began { get => _animation.Began; set => _animation.Began = value; }

        public static bool HasFrames => _frames != null;

        // A null source means "whatever you baked from last time". Null back means there is
        // nothing to bake from and the caller should leave the field alone.
        public static Texture2D Current(Texture2D source)
        {
            // Keep using the baked source during a preset switch so Ready can observe the change.
            if (_frames != null && _preset != Chosen) source = _src;

            if (source != null && !Ready(source)) return null;
            if (_frames == null) return null;

            float now = Time.realtimeSinceStartup;
            if (_began < 0f)
            {
                _began = now;
                _depth = 0.5f;
                _phase = 0;
                _walkAt = 0f;
                _phaseAt = 0f;
            }

            float t = now - _began;

            // Smoothstepped, a ramp that starts instantly being a cut. Frame zero is the game's
            // own picture untouched, so this leaves it where the menu had it.
            if (t < OnsetSecs)
            {
                float u = Mathf.SmoothStep(0f, 1f, t / OnsetSecs);
                return _frames[Mathf.Clamp(Mathf.FloorToInt(u * Onset), 0, Onset - 1)];
            }

            Walk(t - OnsetSecs);
            int depth = Mathf.Clamp(
                Mathf.RoundToInt(_depth * (_preset.Depths - 1)), 0, _preset.Depths - 1);
            return _frames[Onset + depth * _preset.Phases + _phase];
        }

        // Stepped off absolute times rather than a delta, so a frame that asks twice gets one
        // answer - the menu patch and eco both call Current.
        static float _depth { get => _animation.Depth; set => _animation.Depth = value; }
        static int _phase { get => _animation.Phase; set => _animation.Phase = value; }
        static float _walkAt { get => _animation.WalkAt; set => _animation.WalkAt = value; }
        static float _phaseAt { get => _animation.PhaseAt; set => _animation.PhaseAt = value; }
        // Time-seeded, unlike everything the bake rolls: the frames are meant to match across
        // installs and the path across them is meant not to.
        static System.Random _walkRng => _animation.WalkRng;

        // Three uniforms sum to a bounded bell, which is what a walk that must not bolt wants.
        static float Gauss() =>
            (float)(_walkRng.NextDouble() + _walkRng.NextDouble() + _walkRng.NextDouble()) - 1.5f;

        static void Walk(float t)
        {
            // Ornstein-Uhlenbeck: left free the walk settles at an end of the band and stays.
            if (_preset.Depths > 1)
            {
                if (t - _walkAt > WalkGapMax) _walkAt = t - WalkStep;
                while (t - _walkAt >= WalkStep)
                {
                    _walkAt += WalkStep;
                    _depth = Mathf.Clamp01(_depth + (0.5f - _depth) * WalkPull + WalkJitter * Gauss());
                }
            }

            if (_preset.Phases < 2) return;

            // A closed preset's phases are the loop itself and must arrive in order.
            if (_preset.Closed)
            {
                _phase = Mathf.FloorToInt(t / LoopSecs * _preset.Phases) % _preset.Phases;
                return;
            }

            if (t - _phaseAt < PhaseSecs) return;
            _phaseAt = t;

            // Drawn from the others, so a redraw always redraws; picking uniformly would repeat
            // one time in Phases, which reads as the animation catching.
            int step = 1 + _walkRng.Next(_preset.Phases - 1);
            _phase = (_phase + step) % _preset.Phases;
        }

        // False means we could not get any and the caller should stand down.
        static bool Ready(Texture2D source)
        {
            if (source == null) return false;

            Preset preset = Chosen;
            string key = MenuBackgroundBake.Key(source, preset);
            if (_frames != null && _srcKey == key) return true;

            _src = source;
            _srcKey = key;
            _preset = preset;
            _began = -1f;

            // The set being replaced is left to Unity. Its textures are marked
            // DontUnloadUnusedAsset, so they stay resident - the same price an expansion
            // background switch has always cost, paid once more if the setting is turned over.
            try
            {
                _frames = MenuBackgroundBake.Load(key, preset) ??
                    MenuBackgroundBake.Bake(source, key, preset);
            }
            catch (Exception e)
            {
                // A background that will not bake is a cosmetic loss. Parallel.For hands back
                // an AggregateException whose own Message says only that one happened, so the
                // cause is unwrapped or the single line this path ever prints says nothing.
                if (e is AggregateException agg) e = agg.Flatten().InnerException ?? e;
                Log.Warning($"[SlopWorld] background bake failed, leaving it clean: {e}");
                _frames = null;
            }

            if (_frames != null) MenuBackgroundBake.Sweep(key);
            return _frames != null;
        }

        // So the source capture below never bakes the rot from an already-rotted frame.
        public static bool IsOurs(Texture2D tex)
        {
            if (tex == null || _frames == null) return false;
            for (int i = 0; i < _frames.Length; i++)
                if (ReferenceEquals(_frames[i], tex)) return true;
            return false;
        }
    }

    // Patch the draw path: loading screens bypass MainMenuDrawer.Init, and BackgroundOnGUI owns
    // aspect fitting, letterboxing, and the expansion crossfade.
    [HarmonyPatch(typeof(UI_BackgroundMain), nameof(UI_BackgroundMain.BackgroundOnGUI))]
    public static class Patch_MenuBackgroundRot
    {
        static void Prefix(UI_BackgroundMain __instance)
        {
            Texture2D src = __instance.overrideBGImage;

            // Never rebake from an already-rotted frame.
            if (MenuBackground.IsOurs(src)) src = null;

            // Supply vanilla's source only before the first bake; ContentFinder scans loaded mods.
            if (src == null && !MenuBackground.HasFrames)
                src = ContentFinder<Texture2D>.Get("UI/HeroArt/BGPlanet", false);

            Texture2D frame = MenuBackground.Current(src);
            if (frame != null) __instance.overrideBGImage = frame;
        }
    }
}
