using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public static class LinuxGameWindow
    {
        static object _configuredWindow;
        static object _identifiedToplevel;
        internal static bool WindowReady => _configuredWindow != null;

        static class Native
        {
            static readonly Type Engine = AccessTools.TypeByName("Waylith.EngineApplication");
            internal static readonly PropertyInfo Instance = AccessTools.Property(
                Engine,
                "Instance"
            );
            internal static readonly PropertyInfo Window = AccessTools.Property(Engine, "Window");
            internal static readonly PropertyInfo Title = AccessTools.Property(
                Window.PropertyType,
                "Title"
            );
            internal static readonly PropertyInfo Decorated = AccessTools.Property(
                Window.PropertyType,
                "Decorated"
            );
            internal static readonly MethodInfo Maximize = AccessTools.Method(
                Window.PropertyType,
                "Maximize"
            );
            internal static readonly PropertyInfo Platform = AccessTools.Property(
                Engine,
                "WindowSystem"
            );

            // Waylith exposes title on IWindow, but sends app ID on the xdg_toplevel.
            internal static readonly FieldInfo Toplevel = AccessTools.Field(
                AccessTools.TypeByName("Waylith.Windowing.Backends.Wayland.WaylandWindow"),
                "_toplevel"
            );
            internal static readonly MethodInfo SetAppId = AccessTools.Method(
                Toplevel.FieldType,
                "SetAppId",
                new[] { typeof(string) }
            );

            internal static object MainWindow() =>
                Instance.GetValue(null) is object app ? Window.GetValue(app) : null;
        }

        internal static object Platform => Native.Platform.GetValue(Native.Instance.GetValue(null));
        internal static Type PlatformType => Native.Platform.PropertyType;

        internal static void ApplyNativeMode(bool fullscreen)
        {
            var mode = fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (Prefs.FullScreenMode != mode)
            {
                Prefs.FullScreenMode = mode;
                Prefs.Save();
            }
            if (Screen.fullScreenMode != mode)
                Screen.fullScreenMode = mode;
        }

        public static void Follow()
        {
            if (Application.platform != RuntimePlatform.LinuxPlayer)
                return;

            var window = Native.MainWindow();
            if (window == null)
                return;
            if (!ReferenceEquals(window, _configuredWindow))
            {
                Native.Decorated.SetValue(window, false);
                if (!Settings.Fullscreen && Screen.fullScreenMode == FullScreenMode.Windowed)
                    Native.Maximize.Invoke(window, null);
                _configuredWindow = window;
            }
            var toplevel = Native.Toplevel.GetValue(window);
            if (toplevel != null && !ReferenceEquals(toplevel, _identifiedToplevel))
            {
                Native.SetAppId.Invoke(toplevel, new object[] { "slopworld" });
                _identifiedToplevel = toplevel;
            }
            ApplyNativeMode(Settings.Fullscreen);
        }

        public static void Set(bool fullscreen)
        {
            if (Settings.S.fullscreen == fullscreen)
                return;

            Settings.S.fullscreen = fullscreen;
            Settings.S.MarkDirty();
            Follow();
        }

        public static void Toggle() => Set(!Settings.Fullscreen);

        public static bool TrySetTitle(string title)
        {
            var window = Native.MainWindow();
            if (window == null)
                return false;
            Native.Title.SetValue(window, title);
            return true;
        }
    }
}
