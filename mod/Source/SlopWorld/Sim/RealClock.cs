using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// The bridge between the game's clock and the wall clock, and it runs both
    /// ways: durations are read off the game's clock in real units, and the game's
    /// calendar is steered off the real one.
    ///
    /// RimWorld counts in ticks and prints them in its own calendar, where an hour
    /// is 2500 ticks and a day 60000 - so a colonist whose agent died two real
    /// minutes ago is reported to have been down for three hours. On a board that
    /// mirrors live processes that reading is worse than useless: the only span
    /// anyone watching cares about is the one on their own clock. At Normal speed -
    /// the only speed TimeKeeper allows - the game runs 60 ticks a real second, so a
    /// span of ticks already is a real duration, which is all Seconds and Period do
    /// with it. RealTimePatches is where they land.
    ///
    /// The sky is the other direction and the harder half. Day and night are a pure
    /// function of the *absolute* tick and the tile's longitude - through
    /// GenCelestial for the glow and the shadows, GenLocalDate for the hour, both
    /// bottoming out in GenDate - and at 60 ticks a second a game day is sixteen
    /// minutes and forty seconds. So the sun crossed the board eighty-six times a
    /// day and midnight on the map meant nothing at all about the room the player
    /// was sitting in.
    ///
    /// The absolute tick is TicksGame plus TickManager.gameStartAbsTick and nothing
    /// else, and that field is public, written once when the game is made and read
    /// by three methods afterwards. So this needs no patch: rewriting the field
    /// every frame moves the whole calendar at once - the glow, the shadow vectors,
    /// the shader's DayPercent, the hour, the season - where patching would have
    /// meant finding every reader and hoping none of them was small enough for Mono
    /// to inline past the patch.
    ///
    /// What it is written to is the wall clock: one game day to a real day, and the
    /// local time at the colony's own tile equal to the local time on the player's
    /// machine, timezone and daylight saving included, because DateTime.Now already
    /// answers both. The colony's first day is day one of the game's year, so the
    /// year is sixty real days and a quadrum fifteen.
    ///
    /// Two things follow. An absolute tick is worth 1.44 real seconds now rather
    /// than a sixtieth of one, so anything holding a stamp - the pawn log's, which
    /// is the one place a viewer reads one - converts through SecondsPerAbsTick.
    /// And the slip this component used to bank is gone: it existed because game
    /// ticks stood in for real seconds and the game clock stops for a forced pause,
    /// a stutter and above all the stretch a save spends closed. A wall clock does
    /// none of that, so there is nothing left to write down.
    ///
    /// GameComponents are built for every subclass automatically, so this needs no
    /// def.
    /// </summary>
    public class RealClock : GameComponent
    {
        /// <summary>Ticks the game runs per real second at Normal speed.</summary>
        public const float TicksPerRealSecond = 60f;

        /// <summary>Real seconds one absolute tick stands for, the calendar being
        /// pinned to the wall clock at a game day to the real day.</summary>
        public const float SecondsPerAbsTick = 86400f / GenDate.TicksPerDay;

        // The local day the colony landed on, counted in DateTime's own days, so
        // that day comes out as day one of the game's year. Scribed, because a
        // colony that coined this afresh on every load would walk its date - and
        // with it its season - back to the start every time it was opened.
        long _epoch;

        public RealClock(Game game) { }

        /// <summary>
        /// The real seconds a span of game ticks stands for: the bare
        /// 60-ticks-a-second conversion, which is exact for anything the clock ran
        /// straight through and a floor for anything it did not.
        /// </summary>
        public static float Seconds(int ticks) => ticks / TicksPerRealSecond;

        /// <summary>Real seconds between an absolute game tick and now. Exact:
        /// absolute ticks are wall-clock instants, so a stamp keeps its distance
        /// across a pause, a stutter, and the time the game spent closed.</summary>
        public static float SecondsSince(int absTick)
        {
            if (Verse.Current.ProgramState != ProgramState.Playing) return 0f;

            var ticks = Find.TickManager;
            if (ticks == null) return 0f;

            return Mathf.Max((ticks.TicksAbs - absTick) * SecondsPerAbsTick, 0f);
        }

        /// <summary>
        /// A real-time duration in the shape RimWorld prints its own: one unit,
        /// rounded down. Minutes are the odd one out - the vanilla calendar has no
        /// such unit, so there is no key to reuse and the words are the mod's own,
        /// like the rest of its text.
        /// </summary>
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

            // Every frame rather than once, because the two clocks run at different
            // speeds by design: TicksGame gains sixty a second and the sun a little
            // under one, so the offset between them is a thing that moves.
            int start = solar.Value - ticks.TicksGame;

            // Never zero: TickManager reads that as "not set yet", logs about it and
            // hands back the game tick instead. A tick either way is a second and a
            // half of daylight nobody can see.
            ticks.gameStartAbsTick = start != 0 ? start : 1;
        }

        /// <summary>
        /// The absolute tick that reads, at the colony's own tile, as the day and
        /// the hour it is on the player's clock. Null while there is no tile to take
        /// a longitude from - map generation, mostly - where vanilla's own clock is
        /// left where it is.
        /// </summary>
        int? SolarTick()
        {
            float? longitude = Longitude;
            if (longitude == null) return null;

            // Local rather than UTC, so the machine's timezone and whatever it
            // believes about daylight saving both arrive already applied.
            DateTime now = DateTime.Now;
            long today = now.Ticks / TimeSpan.TicksPerDay;
            if (_epoch == 0) _epoch = today;

            // Day one rather than day zero, so the hour the longitude costs cannot
            // put the colony's first morning before the start of the year.
            long day = today - _epoch + 1;
            int within = (int)(now.TimeOfDay.TotalSeconds / 86400.0 * GenDate.TicksPerDay);

            // Everything downstream adds an hour per fifteen degrees of longitude
            // before it reads an hour off this, which is what makes a tile's time
            // local to the tile. Taking that back off here is what leaves the tile's
            // local time equal to the player's own. It is the same sum vanilla does
            // in GenCelestial.TicksAbsForSunPosInWorldSpace to find noon.
            return (int)(day * GenDate.TicksPerDay + within
                         - GenDate.LocalTicksOffsetFromLongitude(longitude.Value));
        }

        /// <summary>Where the colony is, east to west. The current map for all of
        /// this colony's life; the tile that was picked, for the frames between a
        /// world existing and a map standing on it.</summary>
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
