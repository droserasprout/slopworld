using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Verse;
using Exception = System.Exception;

namespace SlopWorld
{
    // Unity's legacy X11 input reads the button events that XInput 2.1 emulates after a
    // smooth-scroll valuator crosses a whole increment. Querying that valuator preserves
    // the fractions XWayland received from the touchpad without owning the device, taking
    // events from Unity, or calling from native code back into managed code.
    static class X11ScrollInput
    {
        const int Success = 0;
        const int XIMasterPointer = 1;
        const int XIAllDevices = 0;
        const int XIValuatorClass = 2;
        const int XIScrollClass = 3;
        const int XIScrollTypeVertical = 1;
        const int XIScrollTypeHorizontal = 2;
        const int XIScrollFlagPreferred = 1 << 1;

        static bool _attempted;
        static IntPtr _display;
        static int _deviceId = -1;
        static Axis _horizontal;
        static Axis _vertical;

        static bool _baseline;
        static ScrollPoint _last;
        static int _lastFrame = -1;
        static int _sampleFrame = -1;
        static bool _sampleUsable;
        static Vector2 _sample;

        struct Axis
        {
            public bool Found;
            public int Number;
            public double Increment;
            public bool Preferred;
        }

        struct ScrollPoint
        {
            public double X;
            public double Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XIDeviceInfo
        {
            public int deviceid;
            public IntPtr name;
            public int use;
            public int attachment;
            public int enabled;
            public int numClasses;
            public IntPtr classes;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XIAnyClassInfo
        {
            public int type;
            public int sourceid;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XIValuatorClassInfo
        {
            public int type;
            public int sourceid;
            public int number;
            public IntPtr label;
            public double min;
            public double max;
            public double value;
            public int resolution;
            public int mode;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XIScrollClassInfo
        {
            public int type;
            public int sourceid;
            public int number;
            public int scrollType;
            public double increment;
            public int flags;
        }

        // True means this frame has a baseline from the immediately preceding frame and
        // Unity's logical wheel packet can be suppressed. A first sample, or the first
        // sample after no scroll view was drawn, deliberately falls back to Unity instead
        // of applying stale movement collected while there was nowhere to put it.
        public static bool TryRead(out Vector2 units)
        {
            units = Vector2.zero;
            int frame = Time.frameCount;
            if (_sampleFrame == frame)
            {
                units = _sample;
                return _sampleUsable;
            }

            _sampleFrame = frame;
            _sample = Vector2.zero;
            _sampleUsable = false;
            if (!Ready()) return false;

            try
            {
                ScrollPoint current;
                if (!ReadCurrent(out current))
                {
                    _baseline = false;
                    return false;
                }

                bool contiguous = _baseline && frame == _lastFrame + 1;
                _lastFrame = frame;
                if (!contiguous)
                {
                    _last = current;
                    _baseline = true;
                    return false;
                }

                var dx = current.X - _last.X;
                var dy = current.Y - _last.Y;
                _last = current;

                // Relative scroll valuators are cumulative and may be reset before their
                // fixed-point range overflows. Such a reset is not a finger crossing the
                // whole list in one frame; discard it and let the next sample continue.
                if (!Finite(dx) || !Finite(dy) || Math.Abs(dx) > 100.0 || Math.Abs(dy) > 100.0)
                    return false;

                _sample = new Vector2((float)dx, (float)dy);
                _sampleUsable = true;
                units = _sample;
                return true;
            }
            catch (DllNotFoundException)
            {
                Disable();
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                Disable();
                return false;
            }
            catch (BadImageFormatException)
            {
                Disable();
                return false;
            }
            catch (Exception e)
            {
                Disable();
                Log.Warning("[SlopWorld] precise X11 scrolling disabled: " + e.Message);
                return false;
            }
        }

        static bool Ready()
        {
            if (_attempted) return _display != IntPtr.Zero && _deviceId >= 0;
            _attempted = true;

            if (Application.platform != RuntimePlatform.LinuxPlayer) return false;

            try
            {
                _display = XOpenDisplay(null);
                if (_display == IntPtr.Zero) return false;

                int major = 2;
                int minor = 1;
                if (XIQueryVersion(_display, ref major, ref minor) != Success ||
                    major < 2 || (major == 2 && minor < 1))
                {
                    Disable();
                    return false;
                }

                if (!FindMasterPointer())
                {
                    Disable();
                    return false;
                }

                return true;
            }
            catch (DllNotFoundException)
            {
                Disable();
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                Disable();
                return false;
            }
            catch (BadImageFormatException)
            {
                Disable();
                return false;
            }
            catch (Exception e)
            {
                Disable();
                Log.Warning("[SlopWorld] precise X11 scrolling unavailable: " + e.Message);
                return false;
            }
        }

        static bool FindMasterPointer()
        {
            int count;
            IntPtr devices = XIQueryDevice(_display, XIAllDevices, out count);
            if (devices == IntPtr.Zero) return false;

            try
            {
                int size = Marshal.SizeOf(typeof(XIDeviceInfo));
                for (int i = 0; i < count; i++)
                {
                    var info = Read<XIDeviceInfo>(IntPtr.Add(devices, i * size));
                    if (info.use != XIMasterPointer || info.enabled == 0) continue;

                    Axis horizontal;
                    Axis vertical;
                    FindAxes(info, out horizontal, out vertical);
                    if (!horizontal.Found && !vertical.Found) continue;

                    _deviceId = info.deviceid;
                    _horizontal = horizontal;
                    _vertical = vertical;
                    return true;
                }
                return false;
            }
            finally
            {
                XIFreeDeviceInfo(devices);
            }
        }

        static void FindAxes(XIDeviceInfo info, out Axis horizontal, out Axis vertical)
        {
            horizontal = new Axis();
            vertical = new Axis();
            for (int i = 0; i < info.numClasses; i++)
            {
                IntPtr at = Marshal.ReadIntPtr(info.classes, i * IntPtr.Size);
                if (at == IntPtr.Zero) continue;
                var any = Read<XIAnyClassInfo>(at);
                if (any.type != XIScrollClass) continue;

                var scroll = Read<XIScrollClassInfo>(at);
                if (Math.Abs(scroll.increment) <= 0.000001) continue;
                var axis = new Axis
                {
                    Found = true,
                    Number = scroll.number,
                    Increment = scroll.increment,
                    Preferred = (scroll.flags & XIScrollFlagPreferred) != 0,
                };

                if (scroll.scrollType == XIScrollTypeHorizontal &&
                    (!horizontal.Found || axis.Preferred)) horizontal = axis;
                else if (scroll.scrollType == XIScrollTypeVertical &&
                    (!vertical.Found || axis.Preferred)) vertical = axis;
            }
        }

        static bool ReadCurrent(out ScrollPoint current)
        {
            current = new ScrollPoint();
            int count;
            IntPtr devices = XIQueryDevice(_display, _deviceId, out count);
            if (devices == IntPtr.Zero || count <= 0) return false;

            try
            {
                var info = Read<XIDeviceInfo>(devices);
                bool gotHorizontal = !_horizontal.Found;
                bool gotVertical = !_vertical.Found;
                double x = 0.0;
                double y = 0.0;

                for (int i = 0; i < info.numClasses; i++)
                {
                    IntPtr at = Marshal.ReadIntPtr(info.classes, i * IntPtr.Size);
                    if (at == IntPtr.Zero) continue;
                    var any = Read<XIAnyClassInfo>(at);
                    if (any.type != XIValuatorClass) continue;

                    var valuator = Read<XIValuatorClassInfo>(at);
                    if (_horizontal.Found && valuator.number == _horizontal.Number)
                    {
                        x = valuator.value / _horizontal.Increment;
                        gotHorizontal = true;
                    }
                    if (_vertical.Found && valuator.number == _vertical.Number)
                    {
                        y = valuator.value / _vertical.Increment;
                        gotVertical = true;
                    }
                }

                if (!gotHorizontal || !gotVertical) return false;
                current = new ScrollPoint { X = x, Y = y };
                return true;
            }
            finally
            {
                XIFreeDeviceInfo(devices);
            }
        }

        static T Read<T>(IntPtr pointer) where T : struct =>
            (T)Marshal.PtrToStructure(pointer, typeof(T));

        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        static void Disable()
        {
            if (_display != IntPtr.Zero) XCloseDisplay(_display);
            _display = IntPtr.Zero;
            _deviceId = -1;
            _baseline = false;
        }

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr XOpenDisplay(string displayName);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        static extern int XCloseDisplay(IntPtr display);

        [DllImport("libXi.so.6", CallingConvention = CallingConvention.Cdecl)]
        static extern int XIQueryVersion(IntPtr display, ref int major, ref int minor);

        [DllImport("libXi.so.6", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr XIQueryDevice(IntPtr display, int deviceId, out int count);

        [DllImport("libXi.so.6", CallingConvention = CallingConvention.Cdecl)]
        static extern void XIFreeDeviceInfo(IntPtr devices);
    }
}
