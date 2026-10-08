using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SlopWorld.Tests
{
    static class X11ScrollSamplerTests
    {
        sealed class DeviceApi : X11ScrollSampler.IApi
        {
            public int Queries, Opens, Closes, Frees;
            public bool Axes = true, Display = true, Supported = true;
            public int Device = 2, Source = 10, Number = 2;
            public double Value, Increment = 1;
            public Exception Error;
            readonly List<IntPtr> _allocations = new List<IntPtr>();

            public IntPtr Open() { Opens++; return Display ? new IntPtr(1) : IntPtr.Zero; }
            public void Close(IntPtr display) { Closes++; }
            public bool SupportsScrolling(IntPtr display) => Supported;
            public IntPtr Query(IntPtr display, int device, out int count)
            {
                Queries++;
                AssertEx.Equal(1, device, "query current master set rather than cached device id");
                if (Error != null) throw Error;
                count = 1;
                IntPtr classes = IntPtr.Zero;
                if (Axes)
                {
                    var scroll = Allocate(new X11ScrollSampler.XIScrollClassInfo
                    {
                        type = 3,
                        sourceid = Source,
                        number = Number,
                        scrollType = 1,
                        increment = Increment,
                        flags = 2
                    });
                    var valuator = Allocate(new X11ScrollSampler.XIValuatorClassInfo
                    {
                        type = 2,
                        sourceid = Source,
                        number = Number,
                        value = Value
                    });
                    classes = Marshal.AllocHGlobal(2 * IntPtr.Size);
                    _allocations.Add(classes);
                    Marshal.WriteIntPtr(classes, 0, scroll);
                    Marshal.WriteIntPtr(classes, IntPtr.Size, valuator);
                }
                return Allocate(new X11ScrollSampler.XIDeviceInfo
                {
                    deviceid = Device,
                    use = 1,
                    enabled = 1,
                    numClasses = Axes ? 2 : 0,
                    classes = classes
                });
            }

            IntPtr Allocate<T>(T value) where T : struct
            {
                var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
                _allocations.Add(pointer);
                Marshal.StructureToPtr(value, pointer, false);
                return pointer;
            }

            public void Free(IntPtr devices)
            {
                Frees++;
                foreach (var pointer in _allocations) Marshal.FreeHGlobal(pointer);
                _allocations.Clear();
            }
        }

        public static void MissingAxesRecoverWithoutRestartAndRetriesAreBounded()
        {
            double now = 1;
            var api = new DeviceApi { Axes = false };
            var sampler = new X11ScrollSampler(api, () => now);
            AssertEx.Equal(false, sampler.Read().Available, "startup without scroll axes uses fallback");
            api.Axes = true;
            now = 1.5;
            for (int i = 0; i < 1000; i++) sampler.Read();
            AssertEx.Equal(1, api.Queries, "input bursts do not bypass retry backoff");
            now = 2;
            var first = sampler.Read();
            AssertEx.Equal(true, first.Available, "axes discovered after startup");
            api.Axes = false;
            now = 2.1;
            AssertEx.Equal(false, sampler.Read().Available, "temporary axis loss uses fallback");
            api.Axes = true;
            now = 3.1;
            var recovered = sampler.Read();
            AssertEx.Equal(true, recovered.Available, "axes return without game restart");
            AssertEx.True(recovered.Generation != first.Generation, "recovery invalidates previous source baseline");
            AssertEx.Equal(1, api.Opens, "same worker connection survives temporary capability changes");
            AssertEx.Equal(api.Queries, api.Frees, "all successful device replies released");
        }

        public static void AxisAndSourceChangesCannotBecomeViewportMovement()
        {
            double now = 1;
            var api = new DeviceApi { Value = 5 };
            var sampler = new X11ScrollSampler(api, () => now);
            var motion = new ScrollSampleDelta();
            var sample = sampler.Read();
            AssertEx.Equal(false, Read(motion, sample, out _), "first sample establishes baseline");
            api.Value = 5.25;
            now = 1.01;
            sample = sampler.Read();
            AssertEx.Equal(true, Read(motion, sample, out var y), "fractional motion delivered");
            AssertEx.Equal(0.25, y, "fractional distance preserved");

            // Each change is small enough to evade the delta jump limit. Identity must
            // invalidate it even without an intervening unavailable snapshot.
            api.Source++;
            api.Value = 6;
            now = 1.02;
            AssertEx.Equal(false, Read(motion, sampler.Read(), out _), "new physical source resets baseline");
            api.Number++;
            now = 1.03;
            AssertEx.Equal(false, Read(motion, sampler.Read(), out _), "new axis number resets baseline");
            api.Increment = 2;
            now = 1.04;
            AssertEx.Equal(false, Read(motion, sampler.Read(), out _), "new axis increment resets baseline");
            api.Device++;
            now = 1.05;
            AssertEx.Equal(false, Read(motion, sampler.Read(), out _), "replacement master resets baseline");
            api.Value = 6.5;
            now = 1.06;
            AssertEx.Equal(true, Read(motion, sampler.Read(), out y), "motion resumes on replacement source");
            AssertEx.Equal(0.25, y, "current increment used");
            AssertEx.Equal(api.Queries, api.Frees, "axis changes release each query");
        }

        static bool Read(ScrollSampleDelta motion, X11ScrollSampler.Sample sample, out double y) =>
            motion.Read(sample.X, sample.Y, sample.Time, sample.Time,
                sample.Generation, out _, out y);

        public static void DisplayAndQueryFailuresRetryWithFreshConnection()
        {
            double now = 1;
            var api = new DeviceApi { Display = false };
            var sampler = new X11ScrollSampler(api, () => now);
            AssertEx.Equal(false, sampler.Read().Available, "missing display uses fallback");
            api.Display = true;
            now = 2;
            AssertEx.Equal(true, sampler.Read().Available, "display opening retried");
            api.Error = new InvalidOperationException("temporary query failure");
            now = 2.01;
            var failed = sampler.Read();
            AssertEx.Equal(false, failed.Available, "query failure stays in background sampler");
            AssertEx.Equal("temporary query failure", failed.UnavailableReason, "reason available to main-thread logger");
            AssertEx.Equal(1, api.Closes, "failed connection released once");
            for (int i = 0; i < 1000; i++) sampler.Read();
            AssertEx.Equal(2, api.Opens, "retry does not open connections during backoff");
            api.Error = null;
            now = 3.02;
            AssertEx.Equal(true, sampler.Read().Available, "query recovery needs no replacement worker");
            AssertEx.Equal(3, api.Opens, "fresh connection opened after failure");
        }

        public static void UnsupportedNativeRuntimeDoesNotRetryEveryFrame()
        {
            double now = 1;
            var api = new DeviceApi { Supported = false };
            var sampler = new X11ScrollSampler(api, () => now);
            AssertEx.Equal(false, sampler.Read().Available, "old XInput uses fallback");
            now = 100;
            for (int i = 0; i < 1000; i++) sampler.Read();
            AssertEx.Equal(1, api.Opens, "unsupported runtime is not repeatedly probed");
            AssertEx.Equal(1, api.Closes, "unsupported connection released once");
        }

        public static void NonFiniteAxisValuesAndIncrementsUseFallbackAndRecover()
        {
            double now = 1;
            var api = new DeviceApi { Increment = double.NaN };
            var sampler = new X11ScrollSampler(api, () => now);
            AssertEx.Equal(false, sampler.Read().Available, "invalid increment rejected");
            api.Increment = 1;
            api.Value = double.PositiveInfinity;
            now = 2;
            AssertEx.Equal(false, sampler.Read().Available, "invalid coordinate rejected");
            api.Value = 0;
            now = 3;
            AssertEx.Equal(true, sampler.Read().Available, "valid axes recover");
            AssertEx.Equal(api.Queries, api.Frees, "invalid data replies released");
        }
    }
}
