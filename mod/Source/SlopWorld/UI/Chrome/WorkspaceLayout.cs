using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public static class NavigationSide
    {
        public const string Left = "left";
        public const string Right = "right";

        public static string Normalize(string value) =>
            string.Equals(value, Right, StringComparison.OrdinalIgnoreCase) ? Right : Left;

        public static string Label(string value) => Normalize(value) == Right ? "Right" : "Left";
    }

    public static class UiDensityPreset
    {
        public const string Default = "default";
        public const string Compact = "compact";

        public static string Normalize(string value) =>
            string.Equals(value, Compact, StringComparison.OrdinalIgnoreCase)
                ? Compact : Default;

        public static string Label(string value) =>
            Normalize(value) == Compact ? "Compact" : "Default";
    }

    // A single immutable geometry result is shared by drawing, hit testing, terminal sizing,
    // and the Harmony chrome patches. It deliberately knows nothing about the content inside
    // a panel: moving the navigation or changing density must not recreate the panel state.
    public readonly struct WorkspaceGeometry
    {
        public readonly Rect Viewport;
        public readonly Rect Navigation;
        public readonly Rect TopBar;
        public readonly Rect Content;
        public readonly string Side;
        public readonly bool NavigationVisible;
        public readonly int Revision;

        public WorkspaceGeometry(Rect viewport, Rect navigation, Rect topBar, Rect content,
                                 string side, bool navigationVisible, int revision)
        {
            Viewport = viewport;
            Navigation = navigation;
            TopBar = topBar;
            Content = content;
            Side = NavigationSide.Normalize(side);
            NavigationVisible = navigationVisible;
            Revision = revision;
        }

        public bool IsRight => Side == NavigationSide.Right;
        public float LeftInset => NavigationVisible && !IsRight ? Navigation.width : 0f;
        public float RightInset => NavigationVisible && IsRight ? Navigation.width : 0f;
        public float TopInset => TopBar.height;
    }

    public static class WorkspaceLayout
    {
        public const float DefaultNavigationWidth = 210f;
        public const float MinNavigationWidth = 150f;
        public const float MaxNavigationWidth = 460f;
        public const float MinContentWidth = 1f;

        static LayoutKey _lastKey;
        static int _revision;
        static int _currentFrame = -1;
        static WorkspaceGeometry _current;
        static bool _hasCurrent;

        // This is the only code that reads screen size and placement settings for workspace
        // geometry. Hidden screenshot chrome is intentionally not part of this decision: it
        // suppresses drawing/input while keeping content bounds stable.
        public static WorkspaceGeometry Current
        {
            get
            {
                // IMGUI can visit the same screen through several event passes. Hold one
                // snapshot for the frame so a setting click cannot leave drawing and input
                // using different rectangles; the new choice takes effect next frame.
                if (_hasCurrent && _currentFrame == Time.frameCount) return _current;

                float width = Mathf.Max(0f, UI.screenWidth);
                float height = Mathf.Max(0f, UI.screenHeight);
                bool shown = !Cutscene.Playing;
                bool visible = shown && !Settings.SidebarHidden;
                float requested = Settings.SidebarWidth;
                string side = NavigationSide.Normalize(Settings.SidebarSide);
                float top = shown ? TopBar.H : 0f;
                _current = Compute(width, height, shown, visible, side, requested, top,
                    UiMetrics.Revision);
                _currentFrame = Time.frameCount;
                _hasCurrent = true;
                return _current;
            }
        }

        public static int Revision => Current.Revision;

        // Pure geometry entry point used by tests and by callers that already have resolved
        // metrics. `shown` is separate from screenshot filtering so cutscenes can reclaim the
        // whole viewport without changing the meaning of screenshot mode.
        public static WorkspaceGeometry Compute(float viewportWidth, float viewportHeight,
            bool shown, bool navigationVisible, string side, float requestedNavigationWidth,
            float topBarHeight, int metricsRevision = 0)
        {
            float width = Mathf.Max(0f, viewportWidth);
            float height = Mathf.Max(0f, viewportHeight);
            string normalizedSide = NavigationSide.Normalize(side);
            bool visible = shown && navigationVisible;

            float navigationWidth = visible
                ? NavigationWidth(width, requestedNavigationWidth)
                : 0f;
            float top = shown ? Mathf.Min(height, Mathf.Max(0f, topBarHeight)) : 0f;
            var viewport = new Rect(0f, 0f, width, height);
            var navigation = normalizedSide == NavigationSide.Right
                ? new Rect(Mathf.Max(0f, width - navigationWidth), 0f,
                    navigationWidth, height)
                : new Rect(0f, 0f, navigationWidth, height);

            float contentWidth = Mathf.Max(0f, width - navigationWidth);
            float contentX = normalizedSide == NavigationSide.Left ? navigationWidth : 0f;
            var topBar = new Rect(contentX, 0f, contentWidth, top);
            var content = new Rect(contentX, top, contentWidth,
                Mathf.Max(0f, height - top));

            var key = new LayoutKey(width, height, shown, visible, normalizedSide,
                navigationWidth, top, metricsRevision);
            if (!_lastKey.Equals(key))
            {
                _lastKey = key;
                unchecked { _revision++; }
                if (_revision == 0) _revision = 1;
            }

            return new WorkspaceGeometry(viewport, navigation, topBar, content,
                normalizedSide, visible, _revision);
        }

        public static float NavigationWidth(float viewportWidth, float requested)
        {
            float width = Mathf.Max(0f, viewportWidth);
            float maximum = Mathf.Min(MaxNavigationWidth,
                Mathf.Max(MinNavigationWidth, width * 0.4f));
            float preferred = Mathf.Clamp(requested, MinNavigationWidth, maximum);
            // Keep at least a pixel for active content at tiny resolutions. The sidebar can
            // still be narrower than its normal minimum there, but no rect becomes negative.
            return Mathf.Min(preferred, Mathf.Max(0f, width - MinContentWidth));
        }

        struct LayoutKey : IEquatable<LayoutKey>
        {
            readonly float _width, _height, _navigationWidth, _top;
            readonly bool _shown, _visible;
            readonly string _side;
            readonly int _metricsRevision;

            public LayoutKey(float width, float height, bool shown, bool visible, string side,
                             float navigationWidth, float top, int metricsRevision)
            {
                _width = width;
                _height = height;
                _shown = shown;
                _visible = visible;
                _side = side;
                _navigationWidth = navigationWidth;
                _top = top;
                _metricsRevision = metricsRevision;
            }

            public bool Equals(LayoutKey other) =>
                _width == other._width && _height == other._height
                && _shown == other._shown && _visible == other._visible
                && _navigationWidth == other._navigationWidth && _top == other._top
                && _metricsRevision == other._metricsRevision
                && string.Equals(_side, other._side, StringComparison.Ordinal);

            public override bool Equals(object obj) => obj is LayoutKey && Equals((LayoutKey)obj);

            public override int GetHashCode() =>
                (_width, _height, _shown, _visible, _side, _navigationWidth, _top,
                    _metricsRevision).GetHashCode();
        }
    }
}
