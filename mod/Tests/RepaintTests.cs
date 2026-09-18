using System;
using System.Collections;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class RepaintTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("uniform viewport boundaries and empty states", Uniform);
            yield return ("tree lookup agrees with linear visibility", TreeVisibility);
            yield return ("large tree lookup is logarithmic", LargeTree);
            yield return ("tree revisions and offscreen reveal", TreeRevision);
            yield return ("tree rows stay anchored as pager headers change", TreeAnchoring);
            yield return ("files panes have bounded independent geometry", FilesSplit);
            yield return ("project totals enumerate once per session revision", ProjectCounts);
            yield return ("routing preserves filtering sorting and preview height", Routing);
            yield return ("routing refreshes local changes without a session revision", RoutingRefresh);
            yield return ("terminal keeps cursor-only pixels and repaints sparse damage", TerminalDamage);
            yield return ("terminal broad and missing damage repaint completely", TerminalFallback);
            yield return ("terminal repaints damage from frames skipped between paints", TerminalSkippedFrames);
            yield return ("terminal invalidation overrides unchanged content", TerminalInvalidation);
            yield return ("terminal cache key checks every pixel dependency", TerminalKeys);
        }

        static void Range(int count, float top, float height, int first, int end)
        {
            VisibleRows.Uniform(count, 10f, top, height, out int actualFirst, out int actualEnd);
            AssertEx.Equal(first, actualFirst, "first row");
            AssertEx.Equal(end, actualEnd, "exclusive end");
        }

        static void TreeAnchoring()
        {
            float before = 80f + 300f - 120f;
            float added = ContentTreeIndex.AnchoredScroll(120f, 80f, 110f);
            AssertEx.Equal(before, 110f + 300f - added, "new header preserves row screen position");
            float removed = ContentTreeIndex.AnchoredScroll(added, 110f, 80f);
            AssertEx.Equal(120f, removed, "closing header restores scroll");
            AssertEx.Equal(0f, ContentTreeIndex.AnchoredScroll(10f, 110f, 80f), "top boundary clamps");
        }

        static void FilesSplit()
        {
            var body = new UiLayoutRect(10f, 20f, 200f, 400f);
            var split = SidebarFilesSplitGeometry.Arrange(body, true, 0.25f, 40f, 100f, 1f);
            AssertEx.Equal(99.75f, split.Upper.Height, "fraction uses space beside divider");
            AssertEx.Equal(119.75f, split.Divider.Y, "divider follows upper pane");
            AssertEx.Equal(299.25f, split.Lower.Height, "lower pane fills the rest");
            AssertEx.Equal(body.YMax, split.Lower.YMax, "split stays inside body");

            split = SidebarFilesSplitGeometry.Arrange(body, true, 0f, 40f, 100f, 1f);
            AssertEx.Equal(40f, split.Upper.Height, "upper minimum clamps the fraction");
            split = SidebarFilesSplitGeometry.Arrange(body, true, 1f, 40f, 100f, 1f);
            AssertEx.Equal(100f, split.Lower.Height, "lower minimum clamps the fraction");

            split = SidebarFilesSplitGeometry.Arrange(new UiLayoutRect(0f, 0f, 10f, 3f),
                true, 0f, 40f, 100f, 1f);
            AssertEx.Equal(1f, split.Upper.Height, "tiny upper pane remains non-negative and usable");
            AssertEx.Equal(1f, split.Divider.Height, "tiny divider is bounded");
            AssertEx.Equal(1f, split.Lower.Height, "tiny lower pane remains non-negative and usable");

            split = SidebarFilesSplitGeometry.Arrange(body, false, 0.75f, 40f, 100f, 1f);
            AssertEx.False(split.HasUpper, "no open files hides the upper pane");
            AssertEx.Equal(body.Height, split.Lower.Height, "tree gets the full body without files");
            AssertEx.Equal(0.75f, split.Fraction, "saved fraction survives hidden upper pane");

            float picked = SidebarFilesSplitGeometry.FractionAt(220f,
                body, 40f, 100f, 1f);
            AssertEx.Equal(0.5f, picked, "pointer maps to normalized divider position");
        }

        static void Uniform()
        {
            Range(10000, 5000, 100, 500, 510);
            Range(10000, 5000.5f, 100, 500, 511);
            Range(10000, 4999.5f, 100, 499, 510);
            Range(3, 20, 100, 2, 3);
            Range(3, 100, 100, 3, 3);
            Range(3, -5, 10, 0, 1);
            Range(0, 0, 100, 0, 0);
            Range(10, 0, 0, 0, 0);
        }

        static void TreeVisibility()
        {
            // A heading, a tall embedded body, and two ordinary rows.
            var ends = new[] { 14f, 214f, 224f, 234f };
            var tops = new[] { 4f, 14f, 214f, 224f };
            for (float top = -10f; top < 250f; top += 0.5f)
            {
                var expected = new List<int>();
                var actual = new List<int>();
                for (int i = 0; i < ends.Length; i++)
                    if (ends[i] > top && tops[i] < top + 15f) expected.Add(i);
                for (int i = VisibleRows.First(ends, top);
                     i < ends.Length && tops[i] < top + 15f; i++) actual.Add(i);
                AssertEx.Sequence(expected, actual, "visible drawing and hit rows");
            }
            AssertEx.Equal(0, VisibleRows.First(Array.Empty<float>(), 0), "empty tree");
        }

        static void LargeTree()
        {
            var ends = new CountingEnds(1000000);
            AssertEx.Equal(999990, VisibleRows.First(ends, 9999900), "deep scroll");
            AssertEx.True(ends.Reads <= 20, "lookup must not visit a million hidden rows");
        }

        static void TreeRevision()
        {
            var index = new ContentTreeIndex();
            AssertEx.False(index.IsCurrent(int.MinValue), "first revision is never cached");
            index.Add(4, 14, null);
            index.Add(14, 24, "project\nfile");
            index.Add(24, 34, "other\nfile");
            index.Commit(7);
            AssertEx.True(index.IsCurrent(7), "unchanged tree is cached");
            AssertEx.False(index.IsCurrent(8), "expansion or source edit invalidates");
            AssertEx.True(index.Reveal("other\nfile", out float top), "offscreen reveal");
            AssertEx.Equal(24f, top, "selection keys distinguish projects");
            AssertEx.Equal(2, index.First(top), "reveal lands on selected row");
            index.Clear();
            index.Add(4, 14, null);
            index.Commit(8);
            AssertEx.False(index.Reveal("other\nfile", out _), "collapsed rows are not revealed");
            AssertEx.Equal(1, index.First(14), "collapsed children have no hit rows");
        }

        static void ProjectCounts()
        {
            var cache = new ProjectSessionCounts();
            var sessions = new List<SessionInfo>
            {
                new SessionInfo { Project = "a" },
                new SessionInfo { Project = "a", Host = true },
                new SessionInfo { Project = "b", Worker = true },
                new SessionInfo { Project = null },
                new SessionInfo { Project = "" },
            };
            AssertEx.Equal(2, cache.Get(sessions, 0, "a"), "all sessions retain their count");
            AssertEx.Equal(1, cache.Get(ForbiddenSessions(), 0, "b"), "cached project");
            AssertEx.Equal(1, cache.Get(ForbiddenSessions(), 0, null), "null project");
            AssertEx.Equal(1, cache.Get(ForbiddenSessions(), 0, ""), "empty project");
            AssertEx.Equal(0, cache.Get(ForbiddenSessions(), 0, "missing"), "unknown project");
            sessions[0].Project = "b";
            sessions.RemoveAt(1);
            AssertEx.Equal(0, cache.Get(sessions, 1, "a"), "move and removal invalidate");
            AssertEx.Equal(2, cache.Get(ForbiddenSessions(), 1, "b"), "updated total");
        }

        static IEnumerable<SessionInfo> ForbiddenSessions()
        {
            yield return FailEnumeration();
        }

        static SessionInfo FailEnumeration() => throw new Exception("re-enumerated cached sessions");

        static void Routing()
        {
            var rows = new List<SessionInfo>();
            var sessions = new[]
            {
                new SessionInfo { Name = "z", Project = "a" },
                new SessionInfo { Name = "b", Project = "other" },
                new SessionInfo { Name = "a", Project = "a" },
            };
            float height = RoutedSessionRows.Rebuild(rows, sessions, s => s.Project == "a",
                list => list.Add(new SessionInfo { Name = "preview" }), 12f);
            AssertEx.Equal(36f, height, "native previews contribute height");
            AssertEx.Sequence(new[] { "a", "preview", "z" }, rows.ConvertAll(s => s.Name),
                "filtered ordinal order");
        }

        static void RoutingRefresh()
        {
            var rows = new List<SessionInfo>();
            var sessions = new[] { new SessionInfo { Name = "old" }, new SessionInfo { Name = "new" } };
            RoutedSessionRows.Rebuild(rows, sessions, s => s.Name == "old", null, 12);
            RoutedSessionRows.Rebuild(rows, sessions, s => s.Name == "new", null, 18);
            AssertEx.Sequence(new[] { "new" }, rows.ConvertAll(s => s.Name), "viewer handoff");
            float height = RoutedSessionRows.Rebuild(rows, sessions, s => false, null, 18);
            AssertEx.Equal(0f, height, "filter changes clear height");
            AssertEx.Equal(0, rows.Count, "no stale hit rows after filter change");
        }

        static ScreenBuf Screen(int[] changed) => new ScreenBuf
        {
            ContentRevision = 2, Lines = new[] { "a", "b", "c", "d" }, ChangedRows = changed,
        };

        static void TerminalDamage()
        {
            var screen = Screen(new[] { 1 });
            AssertEx.Equal(TerminalRepaint.Rows, TerminalRepaintPolicy.Choose(false, 1, screen),
                "single-row edit is selective");
            screen.Cx = 3;
            screen.Seq++;
            AssertEx.Equal(TerminalRepaint.None, TerminalRepaintPolicy.Choose(false, 2, screen),
                "cursor and sequence changes reuse pixels");
        }

        static void TerminalSkippedFrames()
        {
            var screen = new ScreenBuf();
            screen.FromJson(JVal.Parse(
                "{\"seq\":1,\"rows\":4,\"lines\":[\"old\",\"prompt\",\"\",\"\"]}"));
            int painted = screen.ContentRevision;
            screen.FromJson(JVal.Parse(
                "{\"seq\":2,\"rows\":4,\"lines\":[\"new\",\"prompt\",\"\",\"\"]}"));
            screen.FromJson(JVal.Parse(
                "{\"seq\":3,\"rows\":4,\"lines\":[\"new\",\"pasted\",\"\",\"\"]}"));

            AssertEx.Equal(TerminalRepaint.Full,
                TerminalRepaintPolicy.Choose(false, painted, screen),
                "latest row damage omits the unpainted change to row zero");
            AssertEx.Equal(TerminalRepaint.Rows,
                TerminalRepaintPolicy.Choose(false, screen.ContentRevision - 1, screen),
                "damage is sufficient when the immediately preceding revision was painted");
        }

        static void TerminalFallback()
        {
            foreach (var changed in new[] { new[] { 0, 1 }, Array.Empty<int>(), null })
                AssertEx.Equal(TerminalRepaint.Full,
                    TerminalRepaintPolicy.Choose(false, 1, Screen(changed)),
                    "half the rows or unknown damage repaints the whole pane");
        }

        static void TerminalInvalidation()
        {
            AssertEx.Equal(TerminalRepaint.Full,
                TerminalRepaintPolicy.Choose(true, 2, Screen(Array.Empty<int>())),
                "texture, session, geometry, theme or font invalidation forces repaint");
        }

        static void TerminalKeys()
        {
            var key = new TerminalCacheKey { Buffer = Screen(new[] { 1 }), Session = "a" };
            AssertEx.True(key.Matches(key), "unchanged key");
            key.Buffer.Cx++;
            key.Buffer.Seq++;
            AssertEx.True(key.Matches(key), "cursor and sequence do not invalidate pixels");
            var changes = new Func<TerminalCacheKey, TerminalCacheKey>[]
            {
                k => { k.Buffer = Screen(new[] { 1 }); return k; },
                k => { k.Session = "b"; return k; },
                k => { k.Offset++; return k; },
                k => { k.AltScreen = !k.AltScreen; return k; },
                k => { k.Theme++; return k; },
                k => { k.Font++; return k; },
                k => { k.X++; return k; },
                k => { k.Y++; return k; },
                k => { k.Width++; return k; },
                k => { k.Height++; return k; },
                k => { k.CellW++; return k; },
                k => { k.CellH++; return k; },
                k => { k.Lead++; return k; },
            };
            foreach (var change in changes)
                AssertEx.False(key.Matches(change(key)), "pixel dependency invalidates cache");
        }

        sealed class CountingEnds : IList<float>
        {
            public CountingEnds(int count) { Count = count; }
            public int Reads;
            public int Count { get; }
            public float this[int index]
            {
                get { Reads++; return (index + 1) * 10f; }
                set => throw new NotSupportedException();
            }
            public bool IsReadOnly => true;
            public IEnumerator<float> GetEnumerator() => throw new Exception("linear enumeration");
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            public int IndexOf(float item) => throw new NotSupportedException();
            public void Insert(int index, float item) => throw new NotSupportedException();
            public void RemoveAt(int index) => throw new NotSupportedException();
            public void Add(float item) => throw new NotSupportedException();
            public void Clear() => throw new NotSupportedException();
            public bool Contains(float item) => throw new NotSupportedException();
            public void CopyTo(float[] array, int index) => throw new NotSupportedException();
            public bool Remove(float item) => throw new NotSupportedException();
        }
    }
}
