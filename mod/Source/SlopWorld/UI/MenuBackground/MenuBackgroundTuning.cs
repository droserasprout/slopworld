using UnityEngine;

namespace SlopWorld
{
    internal static class MenuBackgroundTuning
    {
        // Resident frames make this a memory limit (~18MB BC1, ~110MB RGB24 fallback).
        internal const int Onset = 14;

        // The frame-side ceiling.
        internal const int MaxSide = 1024;

        // The ramp stays between clean and fully dark.
        internal const float BreatheLow = 0.7f;
        internal const float BreatheHigh = 0.9f;
        internal static float BreatheMid => (BreatheLow + BreatheHigh) * 0.5f;

        internal const float OnsetSecs = 2f;

        // Mean-reverting walk avoids a learnable period. Step/pull/jitter set the baked range.
        internal const float WalkStep = 0.28f;
        internal const float WalkPull = 0.22f;
        internal const float WalkJitter = 0.24f;
        // Gaps larger than this are treated as an alt-tab rather than a new frame.
        internal const float WalkGapMax = 8f;

        // Duration of one moving frame.
        internal const float PhaseSecs = 0.11f;

        // Loop duration for closed presets and star blink rates.
        internal const float LoopSecs = 3.6f;

        // ---- Rot ----

        // SlopPlagueGas's violet, matched by eye rather than by reference.
        internal static readonly Color Sick = new Color(0.80f, 0.38f, 0.86f, 0.9f);

        // A third of the way down the rot: a picture alight at stage one has nowhere to go.
        internal const float FireFrom = 0.33f;
        // Tuned to the source luminance so fire remains visible before pure white.
        internal const float FuelFloor = 0.4f;
        internal const float FuelFull = 0.65f;
        // Minimum bright-region radius for fuel and star filtering.
        internal const int FuelErode = 3;
        // Fuel decay over roughly a hundred pixels at 1280px height.
        internal const float FuelDecay = 0.98f;
        // Stretch the fBm mid-band into a distinct flame body and gap.
        internal const float NoiseLow = 0.38f;
        internal const float NoiseHigh = 0.62f;
        internal const float FireGain = 1.15f;

        // Noise is drawn at 1/N resolution and sampled bilinearly.
        internal const int NoiseDiv = 4;
        internal const float NoiseFreq = 0.035f;
        internal const int Octaves = 4;
        // Phase separation keeps consecutive fire frames from reading as a pan.
        internal const float StagePhase = 37.7f;

        internal static readonly Color Ember = new Color(1f, 0.24f, 0.05f, 1f);
        internal static readonly Color Flame = new Color(1f, 0.76f, 0.28f, 1f);

        // ---- Grandma's visiting ----

        // Non-integer diagonal hue period avoids a spatial seam while closing in time.
        internal const float SheenCycles = 1.6f;
        // Palette swing around grey. This is chroma at full mask.
        internal const float SheenGain = 0.85f;
        // Band the still fBm field so the sheen has a body and gap rather than an even wash.
        internal const float HazeLow = 0.40f;
        internal const float HazeHigh = 0.66f;
        // Keep sheen phases distinct from fire phases.
        internal const float HazePhase = 11.3f;

        // The constellation is rolled once from this, so the bake stays reproducible.
        internal const int SparkSeed = 1971;
        internal const int SparkCount = 140;
        // Star arm lengths at MaxSide, scaled with the frame.
        internal const float SparkArmMin = 7f;
        internal const float SparkArmMax = 22f;
        internal const float SparkThick = 7f;
        internal const float SparkGain = 1.1f;
        // Integer blink rates ensure closed-loop alignment.
        internal const int SparkRateMin = 1;
        internal const int SparkRateMax = 4;

        // Reproducible 256x256 float value-noise LUT. Cheaper to sample than Unity Perlin.
        internal const int LutSide = 256;
        internal const int LutMask = LutSide - 1;

        // Tabulated cosine hue palette: branch-free full-circle hues at lower per-pixel cost.
        internal const int HueSide = 256;
        internal const int HueMask = HueSide - 1;

        // Low JPEG quality supplies the intended block/chroma artifacts and keeps the cache small.
        internal const int JpegQuality = 10;

        // Bump when bake arithmetic changes. Tuning constants are already in the cache key.
        internal const int Version = 3;

        // Batch scratch buffers to cap transient memory and parallelism independently of set size.
        internal const int BakeBudgetMB = 96;

        // Remove cache directories unused for this long.
        internal const int KeepDays = 30;
    }
}
