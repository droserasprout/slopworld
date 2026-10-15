using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    // A real isolated bus with fake desktop services. No calls reach the host desktop.
    sealed class IdleDesktop : IDisposable
    {
        internal enum Mode
        {
            Grant,
            Reject,
            Silent,
            Missing,
            WrongHandle,
            Malformed,
        }

        const string Portal = "org.freedesktop.portal.Desktop";
        const string Saver = "org.freedesktop.ScreenSaver";
        const string Request = "org.freedesktop.portal.Request";
        readonly string _root;
        readonly Process _process;
        readonly NativeSessionBus _service,
            _observer;
        readonly CancellationTokenSource _stop = new CancellationTokenSource();
        readonly Task _server;
        readonly Mode _mode;
        readonly bool _stallRelease,
            _badSaver;
        public string Address { get; }
        public readonly ConcurrentQueue<string> PortalSenders = new ConcurrentQueue<string>();
        public readonly ConcurrentQueue<string> SaverSenders = new ConcurrentQueue<string>();
        public readonly ConcurrentQueue<string> ActivityPaths = new ConcurrentQueue<string>();
        public int Closes,
            Uninhibits;

        public IdleDesktop(
            Mode mode,
            bool saver = true,
            bool stallRelease = false,
            bool badSaver = false,
            bool gnome = false
        )
        {
            if (!OperatingSystem.IsLinux())
                Assert.Ignore("Linux libdbus integration");
            _mode = mode;
            _stallRelease = stallRelease;
            _badSaver = badSaver;
            _root = Path.Combine(
                Path.GetTempPath(),
                "slopworld-idle-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(_root);
            string config = Path.Combine(_root, "bus.conf");
            File.WriteAllText(
                config,
                "<busconfig><type>session</type><listen>unix:tmpdir=/tmp</listen><auth>EXTERNAL</auth><policy context='default'><allow send_destination='*'/><allow receive_sender='*'/><allow own='*'/></policy></busconfig>"
            );
            _process = Process.Start(
                new ProcessStartInfo("dbus-daemon")
                {
                    ArgumentList = { "--config-file=" + config, "--nofork", "--print-address=1" },
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                }
            );
            try
            {
                Address = _process
                    .StandardOutput.ReadLineAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .GetAwaiter()
                    .GetResult();
                _service = new NativeSessionBus(Address, 1000);
                _observer = new NativeSessionBus(Address, 1000);
                foreach (
                    var name in new[]
                    {
                        mode != Mode.Missing ? Portal : null,
                        saver ? Saver : null,
                        gnome ? "org.gnome.ScreenSaver" : null,
                    }
                )
                {
                    if (name == null)
                        continue;
                    using var reply = _service.Call(
                        "org.freedesktop.DBus",
                        "/org/freedesktop/DBus",
                        "org.freedesktop.DBus",
                        "RequestName",
                        1000,
                        name,
                        0u
                    );
                    AssertEx.Equal(1u, reply.FirstUInt(), "fake desktop owns its service name");
                }
                _server = Task.Run(Serve);
            }
            catch
            {
                _service?.Dispose();
                _observer?.Dispose();
                _process.Kill();
                _process.WaitForExit();
                _process.Dispose();
                Directory.Delete(_root, true);
                throw;
            }
        }

        public bool Owns(string sender)
        {
            using var reply = _observer.Call(
                "org.freedesktop.DBus",
                "/org/freedesktop/DBus",
                "org.freedesktop.DBus",
                "NameHasOwner",
                1000,
                sender
            );
            using var iter = new Iter(reply.Handle);
            return iter.UInt() != 0;
        }

        void Serve()
        {
            while (!_stop.IsCancellationRequested)
            {
                using var call = _service.Read(20);
                if (call == null)
                    continue;
                string method = Marshal.PtrToStringUTF8(dbus_message_get_member(call.Handle));
                if (method == "Inhibit" && call.Path == "/org/freedesktop/portal/desktop")
                    InhibitPortal(call);
                else if (method == "SimulateUserActivity")
                {
                    AssertEx.Equal("", call.Signature, "activity notifications have no arguments");
                    ActivityPaths.Enqueue(call.Path);
                    using var reply = new NativeSessionBus.Message(
                        dbus_message_new_method_return(call.Handle)
                    );
                    Send(reply);
                }
                else if (method == "Inhibit" && call.Path == "/org/freedesktop/ScreenSaver")
                {
                    AssertEx.Equal("ss", call.Signature, "ScreenSaver argument signature");
                    using var args = new Iter(call.Handle);
                    AssertEx.Equal("SlopWorld", args.String(), "application name");
                    args.Next();
                    AssertEx.Equal("SlopWorld is running", args.String(), "reason");
                    SaverSenders.Enqueue(call.Sender);
                    using var reply = new NativeSessionBus.Message(
                        dbus_message_new_method_return(call.Handle)
                    );
                    if (_badSaver)
                        reply.Append("invalid cookie");
                    else
                        reply.Append(42u);
                    Send(reply);
                }
                else if (method == "Close" || method == "UnInhibit")
                {
                    if (method == "Close")
                        Interlocked.Increment(ref Closes);
                    else
                    {
                        AssertEx.Equal(42u, call.FirstUInt(), "release uses granted cookie");
                        Interlocked.Increment(ref Uninhibits);
                    }
                    if (_stallRelease)
                        continue;
                    using var reply = new NativeSessionBus.Message(
                        dbus_message_new_method_return(call.Handle)
                    );
                    Send(reply);
                }
            }
        }

        void InhibitPortal(NativeSessionBus.Message call)
        {
            PortalSenders.Enqueue(call.Sender);
            AssertEx.Equal("sua{sv}", call.Signature, "portal argument signature");
            using var args = new Iter(call.Handle);
            AssertEx.Equal("", args.String(), "unparented request");
            args.Next();
            AssertEx.Equal(8u, args.UInt(), "idle inhibition only");
            args.Next();
            using var dictionary = args.Recurse();
            var options = new Dictionary<string, string>();
            do
            {
                using var entry = dictionary.Recurse();
                string key = entry.String();
                entry.Next();
                using var variant = entry.Recurse();
                options.Add(key, variant.String());
            } while (dictionary.Next());
            AssertEx.Equal("SlopWorld is running", options["reason"], "portal reason");
            string path =
                "/org/freedesktop/portal/desktop/request/"
                + call.Sender.TrimStart(':').Replace('.', '_')
                + "/"
                + options["handle_token"];
            if (_mode != Mode.Silent)
            {
                // Grant before method reply: exercises subscription ordering and queued signals.
                using var signal = new NativeSessionBus.Message(
                    dbus_message_new_signal(
                        Encoding.UTF8.GetBytes(path + "\0"),
                        Encoding.UTF8.GetBytes(Request + "\0"),
                        Encoding.UTF8.GetBytes("Response\0")
                    )
                );
                if (_mode == Mode.Malformed)
                    signal.Append("not a status");
                else
                    signal.Append(_mode == Mode.Reject ? 2u : 0u, new Dictionary<string, string>());
                Send(signal);
            }
            using var reply = new NativeSessionBus.Message(
                dbus_message_new_method_return(call.Handle)
            );
            using var writer = new Iter(reply.Handle, append: true);
            writer.AppendPath(_mode == Mode.WrongHandle ? path + "_wrong" : path);
            Send(reply);
        }

        void Send(NativeSessionBus.Message message)
        {
            AssertEx.True(
                dbus_connection_send(_service.Handle, message.Handle, out _) != 0,
                "fake service sent its message"
            );
        }

        public void Dispose()
        {
            _stop.Cancel();
            try
            {
                AssertEx.True(_server.Wait(3000), "fake desktop stopped");
            }
            finally
            {
                _service.Dispose();
                _observer.Dispose();
                _process.Kill();
                _process.WaitForExit();
                _process.Dispose();
                _stop.Dispose();
                Directory.Delete(_root, true);
            }
        }

        // Independent test-side iterator storage: enough space/alignment for the
        // public DBusMessageIter ABI, without relying on production's struct layout.
        sealed class Iter : IDisposable
        {
            readonly IntPtr _value = Marshal.AllocHGlobal(128);

            Iter() { }

            public Iter(IntPtr message, bool append = false)
            {
                if (append)
                    dbus_message_iter_init_append(message, _value);
                else
                    AssertEx.True(
                        dbus_message_iter_init(message, _value) != 0,
                        "message has arguments"
                    );
            }

            public bool Next() => dbus_message_iter_next(_value) != 0;

            public string String()
            {
                dbus_message_iter_get_pointer(_value, out var value);
                return Marshal.PtrToStringUTF8(value);
            }

            public uint UInt()
            {
                dbus_message_iter_get_uint(_value, out uint value);
                return value;
            }

            public Iter Recurse()
            {
                var child = new Iter();
                dbus_message_iter_recurse(_value, child._value);
                return child;
            }

            public void AppendPath(string path)
            {
                var value = Marshal.StringToCoTaskMemUTF8(path);
                try
                {
                    AssertEx.True(
                        dbus_message_iter_append_basic(_value, 'o', ref value) != 0,
                        "object path encoded"
                    );
                }
                finally
                {
                    Marshal.FreeCoTaskMem(value);
                }
            }

            public void Dispose() => Marshal.FreeHGlobal(_value);
        }

        const string Library = "libdbus-1.so.3";

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr dbus_message_get_member(IntPtr message);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr dbus_message_new_method_return(IntPtr message);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr dbus_message_new_signal(byte[] path, byte[] iface, byte[] member);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern int dbus_connection_send(IntPtr connection, IntPtr message, out uint serial);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern int dbus_message_iter_init(IntPtr message, IntPtr iter);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern void dbus_message_iter_init_append(IntPtr message, IntPtr iter);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern int dbus_message_iter_next(IntPtr iter);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern void dbus_message_iter_recurse(IntPtr iter, IntPtr child);

        [DllImport(
            Library,
            EntryPoint = "dbus_message_iter_get_basic",
            CallingConvention = CallingConvention.Cdecl
        )]
        static extern void dbus_message_iter_get_pointer(IntPtr iter, out IntPtr value);

        [DllImport(
            Library,
            EntryPoint = "dbus_message_iter_get_basic",
            CallingConvention = CallingConvention.Cdecl
        )]
        static extern void dbus_message_iter_get_uint(IntPtr iter, out uint value);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern int dbus_message_iter_append_basic(IntPtr iter, int type, ref IntPtr value);
    }
}
