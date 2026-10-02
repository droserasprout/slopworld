using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SlopWorld
{
    // Owns the X11 connection and device discovery on the dedicated sampling thread.
    // Master-pointer classes follow the active physical device, so no axis map survives
    // a query. X11ScrollInput owns IMGUI delivery and motion baselines.
    internal sealed class X11ScrollSampler
    {
        const int XIMasterPointer = 1;
        const int XIAllMasterDevices = 1;
        const int XIValuatorClass = 2;
        const int XIScrollClass = 3;
        const int XIScrollTypeVertical = 1;
        const int XIScrollTypeHorizontal = 2;
        const int XIScrollFlagPreferred = 1 << 1;
        const double RetryDelay = 1;

        internal interface IApi
        {
            IntPtr Open();
            void Close(IntPtr display);
            bool SupportsScrolling(IntPtr display);
            IntPtr Query(IntPtr display, int device, out int count);
            void Free(IntPtr devices);
        }

        internal struct Sample
        {
            public bool Available;
            public double X, Y, Time;
            public int Generation;
            public string UnavailableReason;
        }

        readonly IApi _api;
        readonly Func<double> _now;
        IntPtr _display;
        bool _unsupported, _hasSource;
        double _retryAt;
        int _deviceId, _generation;
        Axis _horizontal, _vertical;

        public X11ScrollSampler() : this(new NativeApi(), () => Now) { }
        internal X11ScrollSampler(IApi api, Func<double> now) { _api = api; _now = now; }
        internal static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

        struct Axis
        {
            public bool Found;
            public int Source, Number;
            public double Increment;
            public bool Preferred;

            public bool Same(Axis other) => Found == other.Found &&
                (!Found || (Source == other.Source && Number == other.Number &&
                    Increment == other.Increment));
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XIDeviceInfo
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
        internal struct XIAnyClassInfo
        {
            public int type;
            public int sourceid;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XIValuatorClassInfo
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
        internal struct XIScrollClassInfo
        {
            public int type;
            public int sourceid;
            public int number;
            public int scrollType;
            public double increment;
            public int flags;
        }

        public Sample Read()
        {
            double started = _now();
            if (_unsupported || started < _retryAt) return default;
            try
            {
                if (_display == IntPtr.Zero)
                {
                    _display = _api.Open();
                    if (_display == IntPtr.Zero) return Retry("X11 display is unavailable");
                    if (!_api.SupportsScrolling(_display))
                    {
                        _unsupported = true;
                        Close();
                        return Retry("XInput 2.1 is unavailable");
                    }
                }
                if (!Query(out var sample)) return Retry("no smooth-scroll axes are available");
                sample.Time = started;
                return sample;
            }
            catch (DllNotFoundException error) { return Unsupported(error); }
            catch (EntryPointNotFoundException error) { return Unsupported(error); }
            catch (BadImageFormatException error) { return Unsupported(error); }
            catch (Exception error)
            {
                Close();
                return Retry(error.Message);
            }
        }

        Sample Unsupported(Exception error)
        {
            _unsupported = true;
            Close();
            return Retry(error.Message);
        }

        Sample Retry(string reason)
        {
            // Back off without sleeping or creating another worker. Unavailable snapshots
            // invalidate the consumer baseline even if the device returns with identical axes.
            _hasSource = false;
            _retryAt = _now() + RetryDelay;
            return new Sample { UnavailableReason = reason };
        }

        void Close()
        {
            var display = _display;
            _display = IntPtr.Zero;
            if (display != IntPtr.Zero) _api.Close(display);
        }

        bool Query(out Sample sample)
        {
            sample = default;
            // Query the master set instead of a cached id: removed devices must not produce
            // BadDevice requests, and one reply carries the current scroll classes and values.
            IntPtr devices = _api.Query(_display, XIAllMasterDevices, out int count);
            if (devices == IntPtr.Zero) return false;
            try
            {
                int size = Marshal.SizeOf(typeof(XIDeviceInfo));
                for (int i = 0; i < count; i++)
                {
                    var info = Read<XIDeviceInfo>(IntPtr.Add(devices, i * size));
                    if (info.use != XIMasterPointer || info.enabled == 0) continue;
                    FindAxes(info, out var horizontal, out var vertical);
                    if (!horizontal.Found && !vertical.Found) continue;
                    if (!ReadPoint(info, horizontal, vertical, out double x, out double y)) continue;
                    if (!_hasSource || info.deviceid != _deviceId ||
                        !horizontal.Same(_horizontal) || !vertical.Same(_vertical)) _generation++;
                    _deviceId = info.deviceid;
                    _horizontal = horizontal;
                    _vertical = vertical;
                    _hasSource = true;
                    sample = new Sample { Available = true, X = x, Y = y, Generation = _generation };
                    return true;
                }
                return false;
            }
            finally { _api.Free(devices); }
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
                if (!Finite(scroll.increment) || Math.Abs(scroll.increment) <= 0.000001) continue;
                var axis = new Axis
                {
                    Found = true,
                    Number = scroll.number,
                    Source = scroll.sourceid,
                    Increment = scroll.increment,
                    Preferred = (scroll.flags & XIScrollFlagPreferred) != 0,
                };

                if (scroll.scrollType == XIScrollTypeHorizontal &&
                    (!horizontal.Found || axis.Preferred)) horizontal = axis;
                else if (scroll.scrollType == XIScrollTypeVertical &&
                    (!vertical.Found || axis.Preferred)) vertical = axis;
            }
        }

        static bool ReadPoint(XIDeviceInfo info, Axis horizontal, Axis vertical,
                              out double x, out double y)
        {
            x = y = 0;
            bool gotHorizontal = !horizontal.Found;
            bool gotVertical = !vertical.Found;
            for (int i = 0; i < info.numClasses; i++)
            {
                IntPtr at = Marshal.ReadIntPtr(info.classes, i * IntPtr.Size);
                if (at == IntPtr.Zero || Read<XIAnyClassInfo>(at).type != XIValuatorClass) continue;
                var valuator = Read<XIValuatorClassInfo>(at);
                if (horizontal.Found && valuator.sourceid == horizontal.Source &&
                    valuator.number == horizontal.Number)
                {
                    x = valuator.value / horizontal.Increment;
                    gotHorizontal = true;
                }
                if (vertical.Found && valuator.sourceid == vertical.Source &&
                    valuator.number == vertical.Number)
                {
                    y = valuator.value / vertical.Increment;
                    gotVertical = true;
                }
            }
            return gotHorizontal && gotVertical && Finite(x) && Finite(y);
        }

        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static T Read<T>(IntPtr pointer) where T : struct => Marshal.PtrToStructure<T>(pointer);

        sealed class NativeApi : IApi
        {
            public IntPtr Open() => XOpenDisplay(IntPtr.Zero);
            // Close has no synchronous success status; it always releases the display.
            public void Close(IntPtr display) => _ = XCloseDisplay(display);
            public bool SupportsScrolling(IntPtr display)
            {
                int major = 2, minor = 1;
                return XIQueryVersion(display, ref major, ref minor) == 0 &&
                    (major > 2 || (major == 2 && minor >= 1));
            }
            public IntPtr Query(IntPtr display, int device, out int count) =>
                XIQueryDevice(display, device, out count);
            public void Free(IntPtr devices) => XIFreeDeviceInfo(devices);
        }

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr XOpenDisplay(IntPtr displayName);

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
