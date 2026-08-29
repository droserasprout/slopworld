using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class UsageWindowTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("requires a money amount for money rows", RequiresMoneyAmount);
        }

        static void RequiresMoneyAmount()
        {
            var defaults = new UsageWindow();
            AssertEx.Equal("pct", defaults.Unit, "default unit");
            AssertEx.Equal(-1f, defaults.Amount, "missing amount sentinel");
            AssertEx.False(defaults.IsMoney, "default window is not money");

            var money = new UsageWindow { Unit = "usd", Amount = 0f };
            AssertEx.True(money.IsMoney, "zero-dollar amount is still a money row");

            money.Amount = -1f;
            AssertEx.False(money.IsMoney, "money unit without an amount stays percentage-only");
            money.Unit = "USD";
            money.Amount = 2f;
            AssertEx.False(money.IsMoney, "units are wire-normalized before construction");
        }
    }
}
