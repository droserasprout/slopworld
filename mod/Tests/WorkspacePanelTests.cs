using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class WorkspacePanelTests
    {
        sealed class Panel : IWorkspacePanel
        {
            public string PanelId { get; }
            public PanelSize MinimumSize => new PanelSize(200f, 100f);
            public UiLayoutRect Bounds;
            readonly List<string> _events;
            public Panel(string id, List<string> events) { PanelId = id; _events = events; }
            void Note(string message) => _events.Add(PanelId + ":" + message);
            public void Opened() => Note("open");
            public void Closed() => Note("close");
            public void VisibilityChanged(bool visible) => Note(visible ? "show" : "hide");
            public void FocusChanged(bool focused) => Note(focused ? "focus" : "blur");
            public void Arrange(UiLayoutRect bounds) { Bounds = bounds; }
        }

        public static void Lifecycle()
        {
            var events = new List<string>();
            var terminal = new Panel("terminal", events);
            var settings = new Panel("settings", events);
            var owner = new WorkspacePanelOwner<Panel>();
            owner.SetBacking(terminal);
            owner.SetFocus(true);
            owner.SetContent(settings);
            AssertEx.Equal("terminal:open,terminal:show,terminal:focus,terminal:blur,terminal:hide,settings:open,settings:show,settings:focus",
                string.Join(",", events), "covering a terminal hides it without closing it");
            events.Clear();
            NUnit.Framework.Assert.Throws<System.ArgumentException>(() => owner.SetContent(terminal));
            NUnit.Framework.Assert.Throws<System.ArgumentException>(() => owner.SetBacking(settings));
            AssertEx.Equal(0, events.Count, "alias rejection has no lifecycle side effects");
            events.Clear();
            owner.SetContent(settings);
            owner.Arrange(new UiLayoutRect(20f, 30f, 400f, 300f));
            owner.Arrange(new UiLayoutRect(50f, 40f, 250f, 200f));
            AssertEx.Equal(0, events.Count, "placement and repeat activation preserve lifecycle");
            AssertEx.Equal(250f, settings.Bounds.Width, "active panel receives assigned geometry");
            owner.SetFocus(false);
            owner.SetContent(null);
            AssertEx.Equal("settings:blur,settings:hide,settings:close,terminal:show",
                string.Join(",", events), "returning while host is unfocused cannot steal focus");
            AssertEx.True(ReferenceEquals(terminal, owner.Active), "same terminal survives covering content");
            events.Clear();
            owner.SetFocus(true);
            owner.Close();
            owner.Close();
            AssertEx.Equal("terminal:focus,terminal:blur,terminal:hide,terminal:close",
                string.Join(",", events), "close releases once without reopening anything");

            events.Clear();
            owner.SetContent(settings);
            owner.SetBacking(terminal);
            owner.Close();
            AssertEx.Equal("settings:open,settings:show,terminal:open,settings:hide,settings:close,terminal:close",
                string.Join(",", events), "content-only host can acquire a hidden backing panel");
        }

        public static void SplitLifecycle()
        {
            var events = new List<string>();
            var first = new Panel("a", events);
            var second = new Panel("b", events);
            var split = new WorkspaceSplit<Panel>(first);
            split.Open();
            split.SetVisible(true);
            split.SetFocus(true);
            NUnit.Framework.Assert.Throws<System.ArgumentNullException>(() => split.Add(null));
            NUnit.Framework.Assert.Throws<System.ArgumentException>(() => split.Add(first));
            AssertEx.Equal<Panel>(null, split.Second, "invalid adds preserve empty second slot");
            split.Add(second);
            AssertEx.Equal("a:open,a:show,a:focus,b:open,b:show,a:blur,b:focus",
                string.Join(",", events), "opening a split transfers focus without closing the first");
            events.Clear();
            split.SetFocus(false);
            split.SetVisible(false);
            split.Select(first);
            split.SetVisible(true);
            split.SetFocus(true);
            AssertEx.Equal("b:blur,a:hide,b:hide,a:show,b:show,a:focus",
                string.Join(",", events), "covering retains both children and restores only selected focus");
            events.Clear();
            split.Remove(first);
            AssertEx.True(ReferenceEquals(second, split.First), "surviving pane fills the workspace");
            AssertEx.True(ReferenceEquals(second, split.Selected), "removal transfers focus");
            split.Close();
            split.Close();
            AssertEx.Equal("a:blur,b:focus,a:hide,a:close,b:blur,b:hide,b:close",
                string.Join(",", events), "removed and retained children each close once");

            split = new WorkspaceSplit<Panel>(first);
            split.Open();
            split.Add(second);
            split.SetVisible(true);
            split.SetFocus(true);
            events.Clear();
            split.Remove(first);
            AssertEx.Equal("a:hide,a:close", string.Join(",", events),
                "removing an unfocused session leaves the selected session focused");
            split.Add(first);
            split.SetVisible(false);
            events.Clear();
            split.Close();
            AssertEx.Equal("b:close,a:close", string.Join(",", events),
                "closing a covered workspace releases both retained terminals");
        }

        public static void SplitGeometry()
        {
            var bounds = new UiLayoutRect(40f, 60f, 1000f, 500f);
            var split = new WorkspaceSplitGeometry(bounds, 0.3f, 10f, 160f);
            AssertEx.Equal(297f, split.First.Width, "ratio applies to space left after divider");
            AssertEx.Equal(347f, split.Second.X, "second follows divider in workspace coordinates");
            AssertEx.Equal(bounds.XMax, split.Second.XMax, "split fills width exactly");
            split = new WorkspaceSplitGeometry(bounds, -1f, 10f, 160f);
            AssertEx.Equal(160f, split.First.Width, "drag clamps at first pane minimum");
            split = new WorkspaceSplitGeometry(bounds, 2f, 10f, 160f);
            AssertEx.Equal(160f, split.Second.Width, "drag clamps at second pane minimum");
            split = new WorkspaceSplitGeometry(new UiLayoutRect(0f, 0f, 100f, 40f), 0.1f, 10f, 160f);
            AssertEx.Equal(45f, split.First.Width, "small viewports share available width");
            AssertEx.Equal(45f, split.Second.Width, "minimums cannot overflow viewport");
            split = new WorkspaceSplitGeometry(new UiLayoutRect(0f, 0f, 3f, 0f), 0.5f, 10f, 160f);
            AssertEx.Equal(0f, split.Second.Width, "divider is bounded on tiny viewports");
            AssertEx.Equal(3f, split.Second.XMax, "tiny geometry remains within viewport");
        }

        public static void Geometry()
        {
            var wide = new UiLayoutRect(30f, 60f, 1000f, 500f);
            var narrow = new UiLayoutRect(400f, 60f, 400f, 300f);
            AssertEx.True(TerminalPanelGeometry.TryMeasure(wide, 10f, 20f, out int cols, out int rows),
                "wide pane has a grid");
            AssertEx.Equal(100, cols, "wide columns");
            AssertEx.Equal(25, rows, "wide rows");
            TerminalPanelGeometry.TryMeasure(narrow, 10f, 20f, out cols, out rows);
            AssertEx.Equal(40, cols, "another panel uses its own width");
            AssertEx.Equal(15, rows, "another panel uses its own height");
            TerminalPanelGeometry.TryMeasure(wide, 10f, 20f, out cols, out rows);
            AssertEx.Equal(100, cols, "another panel's measurement cannot overwrite the first");
            AssertEx.False(TerminalPanelGeometry.TryMeasure(new UiLayoutRect(0f, 0f, 0f, 0f),
                10f, 20f, out cols, out rows), "unplaced panels do not negotiate a size");
            AssertEx.False(TerminalPanelGeometry.TryMeasure(wide, 0f, 20f, out cols, out rows),
                "uninitialized font does not negotiate");
            TerminalPanelGeometry.TryMeasure(new UiLayoutRect(0f, 0f, 1f, 1f),
                10f, 20f, out cols, out rows);
            AssertEx.Equal(WireProtocol.TerminalMinCols, cols, "tiny pane respects protocol minimum");
            AssertEx.Equal(WireProtocol.TerminalMinRows, rows, "tiny pane respects protocol minimum");
        }
    }
}
