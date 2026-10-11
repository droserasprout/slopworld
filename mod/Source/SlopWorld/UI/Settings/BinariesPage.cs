using System.Linq;
using System.Threading;
using UnityEngine;
using Verse;
using BinaryResult = SlopWorld.BinaryDiscovery.BinaryResult;

namespace SlopWorld
{
    // BinaryDiscovery.Inventory for the commands SlopWorld runs, integrates with, or offers as a default. Host
    // The game resolves host rows. Daemon rows come from /api/whereis so native and
    // sidecar deployments report the environment that actually launches the command.
    public class BinariesPage : IOptionPage
    {
        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly BinaryDiscovery _discovery = new BinaryDiscovery(
            (ok, fail) => DaemonClient.Get<Wire.WhereIsReply>(WireProtocol.Routes.Whereis, ok, fail),
            HostBinaryProbe.Locate, action => ThreadPool.QueueUserWorkItem(_ => action()),
            DaemonClient.OnMainThread);

        public void Load() => _discovery.Load();

        public void Draw(Rect rect)
        {
            using (WidgetState.Save()) DrawCore(rect);
        }

        void DrawCore(Rect rect)
        {
            var inner = SettingsPageLayout.Body(rect);
            if (_scroll.HandleWheel(inner)) return;
            string caption = "Commands used, integrated, or recommended by SlopWorld. " +
                "Host rows check the game's PATH. Daemon rows use the daemon's effective PATH.";
            float width = UiScrollBody.Measure(inner, 0f,
                UiScrollbarReservation.Always).ContentWidth;
            float captionH = UiText.PlainStatusLabelHeight(caption, width);
            float top = captionH + UiTheme.GapS + UiTheme.RowH;
            var geometry = UiScrollBody.Measure(inner, top + ContentHeight(width),
                UiScrollbarReservation.Always);
            using (_scroll.Scope(inner, geometry.View))
            {
                // Even the first wheel pass must not construct native path fields.
                if (SmoothScroll.WheelOnly) return;
                UiText.PlainStatusLabel(new Rect(0f, 0f, geometry.View.width, captionH), caption,
                    UiTheme.Dim);
                DrawHeader(new Rect(0f, captionH + UiTheme.GapS, geometry.View.width,
                    UiTheme.RowH));
                if (_discovery.Results == null)
                {
                    UiText.PlainStatusLabel(new Rect(0f, top, geometry.View.width,
                        Mathf.Max(UiTheme.LineH, geometry.View.height - top)),
                        _discovery.Error ?? (_discovery.HostLoading ? "Checking host PATH" : "No scan results."),
                        _discovery.Error != null ? UiTheme.Bad : UiTheme.Dim);
                }
                else
                {
                    DrawRows(geometry.View, top, inner.height);
                }
            }

            var foot = new UiLayout.Bar(SettingsPageLayout.Footer(rect));
            if (foot.Left("Refresh", UiTheme.Btn.Ghost, !_discovery.HostLoading && !_discovery.DaemonLoading)) Load();
            string status = _discovery.Results == null ? (_discovery.HostLoading ? "Checking" : "") :
                $"{_discovery.Results.Count(result => result.Found)} of {_discovery.Results.Count} found";
            GUI.color = _discovery.Error != null ? UiTheme.Bad : UiTheme.Dim;
            UiText.RowLabel(foot.Rest(), _discovery.Error ?? status, TextAnchor.MiddleRight);
            GUI.color = Color.white;
        }

        void DrawHeader(Rect r)
        {
            Slab.Fill(r, UiTheme.RowBg);
            if (r.width < 520f)
            {
                UiText.RowLabel(r, "Binary / Use / Path or status");
                return;
            }
            float nameW, pathW, useX, useW;
            Columns(r.width, out nameW, out pathW, out useX, out useW);
            UiText.RowLabel(new Rect(r.x + UiTheme.GapS, r.y, 28f, r.height), "", TextAnchor.MiddleCenter);
            UiText.RowLabel(new Rect(r.x + 28f, r.y, nameW - 28f, r.height), "Binary");
            UiText.RowLabel(new Rect(r.x + nameW, r.y, pathW, r.height), "Path");
            UiText.RowLabel(new Rect(r.x + useX, r.y, useW, r.height), "Use / Status");
            Slab.Hairline(new Rect(r.x, r.yMax - 1f, r.width, 1f), UiTheme.Edge);
        }

        void DrawRows(Rect view, float y, float viewportHeight)
        {
            float rowH = BinaryRowHeight(view.width);
            float scrollY = _scroll.Position.y;
            string focused = GUI.GetNameOfFocusedControl();
            string group = null;
            // Inventory order is already grouped. Do not allocate GroupBy iterators for
            // every IMGUI pass, and keep offscreen native TextEditors out of the hot path.
            foreach (var result in _discovery.Results)
            {
                if (group != result.Spec.Group)
                {
                    if (group != null) y += UiTheme.GapS;
                    group = result.Spec.Group;
                    if (VisibleRows.Intersects(y, UiTheme.RowH, scrollY, viewportHeight))
                        UiLayout.SectionHeading(new Rect(0f, y, view.width, UiTheme.RowH), group);
                    y += UiTheme.RowH + UiTheme.GapS;
                }
                if (VisibleRows.Intersects(y, rowH, scrollY, viewportHeight) ||
                    focused == "binaries.path." + result.Spec.Name)
                    DrawRow(new Rect(0f, y, view.width, rowH), result);
                y += rowH;
            }
        }

        void DrawRow(Rect r, BinaryResult result)
        {
            RowChrome.Hover(r, false, true, RowHoverPolicy.OverlayAware);
            float nameW, pathW, useX, useW;
            Columns(r.width, out nameW, out pathW, out useX, out useW);

            bool stacked = r.width < 520f;
            float lineH = UiTheme.RowH;
            if (stacked) { nameW = r.width; pathW = r.width; useX = 0f; useW = r.width; }
            var mark = new Rect(r.x + UiTheme.GapS, r.y + (lineH - 16f) / 2f, 16f, 16f);
            if (result.Found)
            {
                GUI.color = UiTheme.Yes;
                GUI.DrawTexture(mark, Icons.Check);
            }

            GUI.color = result.Found ? UiTheme.Name : UiTheme.Dim;
            UiText.RowLabel(new Rect(r.x + 28f, r.y, Mathf.Max(0f, nameW - 28f), lineH), result.Spec.Name);
            GUI.color = UiTheme.Dim;
            UiText.RowLabel(new Rect(r.x + useX, stacked ? r.y + lineH : r.y, useW, lineH), result.Spec.Use);
            GUI.color = result.Found ? UiTheme.Lead : UiTheme.Bad;
            var path = new Rect(stacked ? r.x : r.x + nameW,
                stacked ? r.y + lineH * 2f : r.y, pathW, lineH);
            if (!result.Resolved)
            {
                GUI.color = UiTheme.Dim;
                UiText.RowLabel(path, result.Status);
            }
            else if (result.Found)
            {
                float fieldPad = Mathf.Min(UiTheme.GapXS, path.width / 2f);
                var field = new Rect(path.x + fieldPad,
                    path.y + (path.height - UiTheme.FieldH) / 2f,
                    Mathf.Max(0f, path.width - fieldPad * 2f), UiTheme.FieldH);
                UiText.ReadOnlyField(field, "binaries.path." + result.Spec.Name,
                    result.Path);
            }
            else
            {
                UiText.RowLabel(path, result.Status);
            }
            GUI.color = Color.white;
        }

        static float BinaryRowHeight(float width) => width < 520f ? UiTheme.RowH * 3f : UiTheme.RowH;

        float ContentHeight(float width)
        {
            float rows = BinaryDiscovery.Inventory.Length * BinaryRowHeight(width);
            string group = null;
            foreach (var spec in BinaryDiscovery.Inventory)
            {
                if (group == spec.Group) continue;
                group = spec.Group;
                rows += UiTheme.RowH + UiTheme.GapS * 2f;
            }
            return Mathf.Max(rows, UiTheme.LineH);
        }

        static void Columns(float width, out float nameW, out float pathW,
                            out float useX, out float useW)
        {
            nameW = Mathf.Min(170f, width * 0.28f);
            useW = Mathf.Min(235f, width * 0.38f);
            pathW = Mathf.Max(0f, width - nameW - useW);
            useX = nameW + pathW;
        }
    }
}
