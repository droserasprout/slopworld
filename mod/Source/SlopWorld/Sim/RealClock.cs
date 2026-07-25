using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// The bridge between the game's clock and the wall clock.
    ///
    /// RimWorld counts in ticks and prints them in its own calendar, where an hour
    /// is 2500 ticks and a day 60000 - so a colonist whose agent died two real
    /// minutes ago is reported to have been down for three hours. On a board that
    /// mirrors live processes that reading is worse than useless: the only span
    /// anyone watching cares about is the one on their own clock.
    ///
    /// At Normal speed - the only speed TimeKeeper allows - the game runs 60 ticks
    /// a real second, so a tick delta already is a real duration. It is off by
    /// exactly the stretches where the clock did not run: windows that force a
    /// pause, frames the game could not keep up with, and above all the time the
    /// game spent closed between a save and its load. Those are sampled every
    /// frame and banked against the tick they happened at, so looking up an old
    /// event still lands on the right answer.
    ///
    /// GameComponents are built for every subclass automatically, so this needs no
    /// def.
    /// </summary>
    public class RealClock : GameComponent
    {
        /// <summary>Ticks the game runs per real second at Normal speed.</summary>
        public const float TicksPerRealSecond = 60f;

        // Slip is only banked once it is worth a whole second, and past the cap the
        // two oldest entries are folded together: a save has no business growing a
        // row per frame because the game spent an afternoon below 60 TPS.
        const float BankAt = 1f;
        const int MaxEntries = 200;

        // One entry per banked gap, ascending by tick, the two lists in step.
        List<int> _ticks = new List<int>();
        List<float> _seconds = new List<float>();

        float _pending;   // slip not yet worth an entry
        long _sampledAt;  // DateTime.UtcNow.Ticks at the last sample; 0 = never
        int _sampledTick; // TicksGame at the last sample

        public RealClock(Game game) { }

        public static RealClock Current => Verse.Current.Game?.GetComponent<RealClock>();

        /// <summary>
        /// The real seconds a span of game ticks stands for: the bare
        /// 60-ticks-a-second conversion. Unanchored - with no idea when the span
        /// happened there is no slip to add back - so this is a floor, exact for
        /// anything the clock ran straight through.
        /// </summary>
        public static float Seconds(int ticks) => ticks / TicksPerRealSecond;

        /// <summary>Real seconds between an absolute game tick and now.</summary>
        public static float SecondsSince(int absTick)
        {
            if (Verse.Current.ProgramState != ProgramState.Playing) return 0f;
            if (Find.TickManager == null) return 0f;

            int tick = GenDate.TickAbsToGame(absTick);
            float elapsed = Seconds(Find.TickManager.TicksGame - tick);
            var clock = Current;
            if (clock != null) elapsed += clock.SlipSince(tick);
            return Mathf.Max(elapsed, 0f);
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

            long now = DateTime.UtcNow.Ticks;
            int tick = ticks.TicksGame;

            // Nothing to measure against on the first frame of a fresh game. After
            // a load there is: the stamp comes back from the save, so the whole
            // time the game spent closed is banked as one gap right here.
            if (_sampledAt != 0)
            {
                float real = (now - _sampledAt) / (float)TimeSpan.TicksPerSecond;
                float ran = Seconds(tick - _sampledTick);
                _pending += real - ran;

                // Signed on purpose: at a speed above Normal the clock outruns the
                // wall and the slip owed is negative. Nothing here sets such a
                // speed, but the dev tools can.
                if (Mathf.Abs(_pending) >= BankAt)
                {
                    Bank(tick, _pending);
                    _pending = 0f;
                }
            }

            _sampledAt = now;
            _sampledTick = tick;
        }

        // Slip banked at or after a tick - the stretches the clock missed while
        // that event was already in the past.
        float SlipSince(int tick)
        {
            float total = _pending;
            for (int i = _ticks.Count - 1; i >= 0 && _ticks[i] >= tick; i--)
                total += _seconds[i];
            return total;
        }

        void Bank(int tick, float seconds)
        {
            _ticks.Add(tick);
            _seconds.Add(seconds);
            if (_ticks.Count <= MaxEntries) return;

            // Fold the two oldest into one at the later tick. That misdates the
            // older half by however far apart the pair was, which for the far end
            // of a long game buys a list that never grows.
            _seconds[1] += _seconds[0];
            _ticks.RemoveAt(0);
            _seconds.RemoveAt(0);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref _ticks, "slipTicks", LookMode.Value);
            Scribe_Collections.Look(ref _seconds, "slipSeconds", LookMode.Value);
            Scribe_Values.Look(ref _pending, "slipPending", 0f);
            Scribe_Values.Look(ref _sampledAt, "sampledAtUtc", 0L);
            Scribe_Values.Look(ref _sampledTick, "sampledTick", 0);

            if (_ticks == null) _ticks = new List<int>();
            if (_seconds == null) _seconds = new List<float>();

            // A save from before this component, or one that lost a list, would
            // leave the pair out of step, and a mismatched pair reads worse than
            // no history at all.
            if (_ticks.Count != _seconds.Count)
            {
                _ticks.Clear();
                _seconds.Clear();
            }
        }
    }
}
