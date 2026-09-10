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
            AssertEx.Equal(WireContract.TerminalMinCols, cols, "tiny pane respects protocol minimum");
            AssertEx.Equal(WireContract.TerminalMinRows, rows, "tiny pane respects protocol minimum");
        }
    }
}
