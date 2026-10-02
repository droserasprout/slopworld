using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class TerminalHistoryRestoreTests
    {
        public static void RestoredHistoryRejectsIncompatibleIdentityAndExtent()
        {
            var saved = new TerminalHistoryRestore {
                RunId = 10, ConnectionGeneration = 2, Cols = 80, Rows = 24,
                History = 100, Sequence = 5, AltScreen = false
            };
            var live = new ScreenBuf { Cols = 80, Rows = 24, History = 100, Seq = 5 };
            Assert.That(saved.Evaluate(10, 2, live).Compatible, Is.True);
            Assert.That(saved.Evaluate(null, 2, live).Compatible, Is.False);
            Assert.That(saved.Evaluate(11, 2, live).Compatible, Is.False);
            Assert.That(saved.Evaluate(10, 3, live).Compatible, Is.False);
            live.Cols++;
            Assert.That(saved.Evaluate(10, 2, live).Compatible, Is.False);
            live.Cols--;
            live.Rows++;
            Assert.That(saved.Evaluate(10, 2, live).Compatible, Is.False);
            live.Rows--;
            live.AltScreen = true;
            Assert.That(saved.Evaluate(10, 2, live).Compatible, Is.False);
            live.AltScreen = false;
            live.History++;
            Assert.That(saved.Evaluate(10, 2, live).Compatible, Is.False);
            live.Seq++;
            var grown = saved.Evaluate(10, 2, live);
            Assert.That(grown.Compatible, Is.True);
            Assert.That(grown.Shift, Is.EqualTo(1));
            live.History = 99;
            Assert.That(saved.Evaluate(10, 2, live).Compatible, Is.False);
            saved.History = -1;
            Assert.That(saved.Evaluate(10, 2, live).Compatible, Is.False);
            saved.Sequence = null;
            saved.Cols = saved.Rows = 0;
            Assert.That(saved.Evaluate(10, 2, live).Compatible, Is.True);
        }

        public static void PageUpStopsAtTheLiveHistoryCeiling()
        {
            var live = new ScreenBuf { History = 30 };
            int target = TerminalHistory.PageOffset(24, 24, true, live);
            Assert.That(target, Is.EqualTo(30));
            Assert.That(TerminalHistory.PageOffset(target, 24, true, live), Is.EqualTo(target));
            Assert.That(TerminalHistory.PageOffset(5, 24, false, live), Is.Zero);
            Assert.That(TerminalHistory.PageOffset(0, 24, true, null), Is.Zero);
        }
    }
}
