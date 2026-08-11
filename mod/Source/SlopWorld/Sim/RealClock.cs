using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Bridges game and wall clocks. Normal speed makes game-tick spans real-time spans.
    // The calendar instead maps one game day to one real day by rewriting
    // gameStartAbsTick; all vanilla sky, hour, season, and date readers then agree.
    // Absolute-tick timestamps must convert through SecondsPerAbsTick.
    public class RealClock : GameComponent
    {
        // Ticks the game runs per real second at Normal speed.
        public const float TicksPerRealSecond = 60f;

        // Real seconds one absolute tick stands for, the calendar being pinned to the
        // wall clock at a game day to the real day.
        public const float SecondsPerAbsTick = 86400f / GenDate.TicksPerDay;

        // Scribed, because a colony that coined this afresh on every load would walk its
        // date - and with it its season - back to the start every time it was opened.
        long _epoch;

        // The sun moves ~0.4° per real minute; recalculating 60 times a second is wasteful.
        // We cache the solar tick for a real second (60 frames) before calling DateTime.Now
        // again. Between recalculations, gameStartAbsTick drifts correctly because it is
        // recomputed every frame from the cached solar tick minus the current TicksGame.
        const int SolarRecalcInterval = 60;
        int _solarSkip;
        int? _cachedSolar;

        public RealClock(Game game) { }

        // Exact for anything the clock ran straight through, and a floor for anything it
        // did not.
        public static float Seconds(int ticks) => ticks / TicksPerRealSecond;

        // Exact: absolute ticks are wall-clock instants, so a stamp keeps its distance
        // across a pause, a stutter, and the time the game spent closed.
        public static float SecondsSince(int absTick)
        {
            if (Verse.Current.ProgramState != ProgramState.Playing) return 0f;

            var ticks = Find.TickManager;
            if (ticks == null) return 0f;

            return Mathf.Max((ticks.TicksAbs - absTick) * SecondsPerAbsTick, 0f);
        }

        // Minutes are the odd one out - the vanilla calendar has no such unit, so there
        // is no key to reuse and the words are the mod's own.
        public static string Period(float seconds, bool shortForm = false)
        {
            float s = Mathf.Max(seconds, 0f);
            if (s >= 86400f) return Unit(s / 86400f, "Period1Day", "PeriodDays", "LetterDay", shortForm);
            if (s >= 3600f) return Unit(s / 3600f, "Period1Hour", "PeriodHours", "LetterHour", shortForm);
            if (s >= 60f) return Unit(s / 60f, null, null, "LetterMinute", shortForm);
            return Unit(s, "Period1Second", "PeriodSeconds", "LetterSecond", shortForm);
        }

        static string Unit(float count, string oneKey, string manyKey, string letterKey, bool shortForm)
        {
            int n = Mathf.FloorToInt(count);

            if (shortForm)
            {
                string letter = letterKey.Translate();
                return n + letter;
            }

            if (oneKey == null) return n == 1 ? "1 minute" : n + " minutes";

            string word = n == 1 ? oneKey.Translate() : manyKey.Translate(n.ToString());
            return word;
        }

        public override void GameComponentUpdate()
        {
            if (Verse.Current.ProgramState != ProgramState.Playing) return;

            var ticks = Find.TickManager;
            if (ticks == null) return;

            // Recalculate the solar tick only every SolarRecalcInterval frames.
            // Between recalculations the cached value stays correct: the formula
            // gameStartAbsTick = solarTick - TicksGame is evaluated every frame,
            // and only solarTick is cached — TicksGame is always current.
            if (_cachedSolar == null || ++_solarSkip >= SolarRecalcInterval)
            {
                _solarSkip = 0;
                _cachedSolar = SolarTick();
                if (_cachedSolar == null) return;
            }

            int start = _cachedSolar.Value - ticks.TicksGame;

            // Never zero: TickManager reads that as "not set yet", logs about it and hands
            // back the game tick instead.
            ticks.gameStartAbsTick = start != 0 ? start : 1;
        }

        // Null while there is no tile to take a longitude from - map generation, mostly -
        // where vanilla's own clock is left where it is.
        int? SolarTick()
        {
            float? longitude = Longitude;
            if (longitude == null) return null;

            // Local rather than UTC, so the machine's timezone and daylight saving both
            // arrive already applied.
            DateTime now = DateTime.Now;
            long today = now.Ticks / TimeSpan.TicksPerDay;
            if (_epoch == 0) _epoch = today;

            // Day one rather than day zero, so the hour the longitude costs cannot put the
            // colony's first morning before the start of the year.
            long day = today - _epoch + 1;
            int within = (int)(now.TimeOfDay.TotalSeconds / 86400.0 * GenDate.TicksPerDay);

            // Everything downstream adds an hour per fifteen degrees of longitude before it
            // reads an hour off this, so taking that back off here is what leaves the tile's
            // local time equal to the player's. The same sum vanilla does in
            // GenCelestial.TicksAbsForSunPosInWorldSpace to find noon.
            return (int)(day * GenDate.TicksPerDay + within
                         - GenDate.LocalTicksOffsetFromLongitude(longitude.Value));
        }

        // The current map for all of this colony's life; the tile that was picked, for
        // the frames between a world existing and a map standing on it.
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
        }
    }
}
