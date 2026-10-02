using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Bridges game and wall clocks. Normal speed makes game-tick spans real-time spans.
    // The calendar instead maps one game day to one real day by rewriting
    // gameStartAbsTick. All vanilla sky, hour, season, and date readers then agree.
    // Absolute-tick timestamps must convert through SecondsPerAbsTick.
    public partial class RealClock : GameComponent
    {
        // Real seconds per absolute tick. One calendar day equals one real day.
        public const float SecondsPerAbsTick = 86400f / GenDate.TicksPerDay;

        // Save the epoch so loading a colony does not reset its date and season.
        long _epoch;

        // Cache the solar tick for one real second to limit DateTime.Now calls.
        // Each frame computes gameStartAbsTick from the cached solar tick minus the current TicksGame.
        PeriodicWork _solarRecalc;
        int? _cachedSolar;
        bool _wasPlaying;
        bool _wasEco;
        int _ecoTicks;
        Map _lastMap;

        public RealClock(Game game) { }

        // Convert game ticks to seconds at normal speed.
        // Pauses and delayed ticks make this value lower than elapsed wall time.
        public static float Seconds(int ticks) => ticks / TicksPerRealSecond;

        // Absolute ticks represent wall time, including pauses, delayed frames, and time while the game was not running.
        public static float SecondsSince(int absTick)
        {
            if (Verse.Current.ProgramState != ProgramState.Playing) return 0f;

            var ticks = Find.TickManager;
            if (ticks == null) return 0f;

            return Mathf.Max((ticks.TicksAbs - absTick) * SecondsPerAbsTick, 0f);
        }

        public override void GameComponentUpdate()
        {
            if (Verse.Current.ProgramState != ProgramState.Playing)
            {
                _wasPlaying = false;
                _wasEco = false;
                _cachedSolar = null;
                return;
            }

            var ticks = Find.TickManager;
            if (ticks == null) return;

            var map = Find.CurrentMap;
            bool mapChanged = map != _lastMap;
            _lastMap = map;
            bool eco = Eco.Resting;

            // Eco stops game ticks. Absolute ticks still represent wall time for dates and log-entry ages.
            // Sample once per second and when state changes.
            // Refresh immediately on entering or leaving Eco.
            bool solarDue = _solarRecalc.Due(Time.realtimeSinceStartupAsDouble, 1.0);
            bool ecoRefresh = eco && (!_wasPlaying || !_wasEco || mapChanged ||
                ticks.TicksGame != _ecoTicks || _cachedSolar == null || solarDue);
            if (ecoRefresh)
            {
                _cachedSolar = SolarTick();
                if (_cachedSolar == null) return;
                Apply(ticks);
                _ecoTicks = ticks.TicksGame;
            }

            if (eco)
            {
                _wasPlaying = true;
                _wasEco = true;
                return;
            }

            bool leavingEco = _wasEco;
            _wasPlaying = true;
            _wasEco = false;
            if (mapChanged) _cachedSolar = null;

            // Recalculate the solar tick once per elapsed second, independent of frame rate.
            // Every frame computes gameStartAbsTick = solarTick - TicksGame.
            // Only solarTick is cached. TicksGame remains current.
            if (solarDue || leavingEco || _cachedSolar == null)
            {
                _cachedSolar = SolarTick();
                if (_cachedSolar == null) return;
            }

            Apply(ticks);
        }

        void Apply(TickManager ticks)
        {
            int start = _cachedSolar.Value - ticks.TicksGame;

            // Avoid zero because TickManager treats it as uninitialized.
            // That case logs a message and returns the game tick instead.
            ticks.gameStartAbsTick = start != 0 ? start : 1;
        }

        // Return null when no tile supplies a longitude, such as during map generation.
        // Leave the base-game clock unchanged in that case.
        int? SolarTick()
        {
            PerfTrace.Count("solar-clock-samples");
            float? longitude = Longitude;
            if (longitude == null) return null;

            // Use local time to include the machine's time zone and daylight-saving adjustment.
            DateTime now = DateTime.Now;
            long today = now.Ticks / TimeSpan.TicksPerDay;
            if (_epoch == 0) _epoch = today;

            // Start at day one so the longitude adjustment cannot place the first morning before the year starts.
            long day = today - _epoch + 1;
            int within = (int)(now.TimeOfDay.TotalSeconds / 86400.0 * GenDate.TicksPerDay);

            // Subtract the longitude offset that downstream clock readers add.
            // This makes the tile's local time match the player's local time.
            // GenCelestial.TicksAbsForSunPosInWorldSpace uses the same adjustment to calculate noon.
            return (int)(day * GenDate.TicksPerDay + within
                         - GenDate.LocalTicksOffsetFromLongitude(longitude.Value));
        }

        // Use an existing map's longitude, or the selected starting tile before map creation.
        static float? Longitude
        {
            get
            {
                var grid = Find.WorldGrid;
                if (grid == null) return null;

                var map = Find.CurrentMap;
                if (map == null && Find.Maps != null && Find.Maps.Count > 0) map = Find.Maps[0];
                if (map != null) return grid.LongLatOf(map.Tile).x;

                var init = Find.GameInitData;
                if (init != null && init.startingTile.Valid) return grid.LongLatOf(init.startingTile).x;

                return null;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _epoch, "solarEpochDay", 0L);
            // Reset derived frame state because loading can restore a different map or tick.
            // The next update must refresh the clock through the same path as Eco entry.
            _wasPlaying = false;
            _wasEco = false;
            _cachedSolar = null;
            _lastMap = null;
        }
    }
}
