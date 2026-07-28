using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The bridge between the game's clock and the wall clock, both ways: durations
    // are read off the game's clock in real units, and the game's calendar is steered
    // off the real one.
    //
    // At Normal speed - the only speed TimeKeeper allows - the game runs 60 ticks a
    // real second, so a span of ticks already is a real duration. That is all Seconds
    // and Period do with it.
    //
    // The sky is the harder half. Day and night are a pure function of the *absolute*
    // tick and the tile's longitude, and at 60 ticks a second a game day is sixteen
    // minutes - so the sun crossed the board eighty-six times a day. The absolute
    // tick is TicksGame plus TickManager.gameStartAbsTick and nothing else, and that
    // field is public, so this needs no patch: rewriting it every frame moves glow,
    // shadow vectors, DayPercent, hour and season at once, where patching would mean
    // finding every reader and hoping Mono inlined past none of them.
    //
    // One game day to a real day, the colony's landing day being day one, so a year
    // is sixty real days. An absolute tick is therefore worth 1.44 real seconds
    // rather than a sixtieth of one, and anything holding a stamp converts through
    // SecondsPerAbsTick.
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

            int? solar = SolarTick();
            if (solar == null) return;

            // Every frame rather than once, because the two clocks run at different speeds by
            // design: TicksGame gains sixty a second and the sun a little under one.
            int start = solar.Value - ticks.TicksGame;

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
