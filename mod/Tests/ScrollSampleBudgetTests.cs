namespace SlopWorld.Tests
{
    static class ScrollSampleBudgetTests
    {
        public static void WheelFloodAfterLayoutIsBounded()
        {
            var budget = new ScrollSampleBudget();
            int sampleGrants = budget.Take(10, false) ? 1 : 0;
            for (int i = 0; i < 10000; i++)
                if (budget.Take(10, true)) sampleGrants++;
            AssertEx.Equal(2, sampleGrants, "layout plus one late-input sampling grant");
            AssertEx.Equal(false, budget.Take(10, false), "repaint uses cached sample");
            AssertEx.Equal(true, budget.Take(11, false), "next frame samples immediately");
            AssertEx.Equal(true, budget.Take(11, true), "next frame can refresh again");
        }

        public static void WheelFirstNeedsOnlyOneGrant()
        {
            var budget = new ScrollSampleBudget();
            int sampleGrants = 0;
            for (int i = 0; i < 10000; i++)
                if (budget.Take(20, true)) sampleGrants++;
            AssertEx.Equal(1, sampleGrants, "first wheel receives the only sampling grant");
            AssertEx.Equal(false, budget.Take(20, false), "nested views reuse sample");
            AssertEx.Equal(true, budget.Take(25, true), "sampling resumes after inactive frames");
        }

        public static void LayoutPassesDoNotSpendRefresh()
        {
            var budget = new ScrollSampleBudget();
            AssertEx.Equal(true, budget.Take(1, false), "initial sample");
            for (int i = 0; i < 100; i++)
                AssertEx.Equal(false, budget.Take(1, false), "shared cached layout sample");
            AssertEx.Equal(true, budget.Take(1, true), "late wheel can still refresh");
            AssertEx.Equal(false, budget.Take(1, true), "only once");
        }
    }
}
