using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Tmds.DBus.Protocol;

namespace SlopWorld.Tests
{
    // Real isolated bus and managed fake desktop services; never touches the host desktop.
    sealed class IdleDesktop : IDisposable, IPathMethodHandler
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
        readonly string _root;
        readonly Process _process;
        DBusConnection _service;
        readonly DBusConnection _observer;
        readonly Mode _mode;
        readonly bool _saver,
            _gnome,
            _stallRelease,
            _badSaver;
        readonly ConcurrentQueue<Exception> _errors = new();
        public string Address { get; }
        public string Path => "/";
        public bool HandlesChildPaths => true;
        public readonly ConcurrentQueue<string> PortalSenders = new();
        public readonly ConcurrentQueue<string> SaverSenders = new();
        public readonly ConcurrentQueue<string> ActivityPaths = new();
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
                Assert.Ignore("Linux private D-Bus integration");
            _mode = mode;
            _saver = saver;
            _gnome = gnome;
            _stallRelease = stallRelease;
            _badSaver = badSaver;
            _root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "slopworld-idle-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(_root);
            string config = System.IO.Path.Combine(_root, "bus.conf");
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
                Address = Wait(_process.StandardOutput.ReadLineAsync());
                _observer = Connect();
                StartService();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        DBusConnection Connect()
        {
            var bus = new DBusConnection(Address);
            try
            {
                Wait(bus.ConnectAsync().AsTask());
                return bus;
            }
            catch
            {
                bus.Dispose();
                throw;
            }
        }

        public void StopService()
        {
            _service?.Dispose();
            _service = null;
        }

        public void StartService()
        {
            _service = Connect();
            _service.AddMethodHandler(this);
            foreach (
                string name in new[]
                {
                    _mode != Mode.Missing ? Portal : null,
                    _saver ? Saver : null,
                    _gnome ? "org.gnome.ScreenSaver" : null,
                }
            )
                if (name != null)
                    Wait(_service.RequestNameAsync(name));
        }

        public bool Owns(string sender)
        {
            using var writer = _observer.GetMessageWriter();
            writer.WriteMethodCallHeader(
                destination: "org.freedesktop.DBus",
                path: "/org/freedesktop/DBus",
                @interface: "org.freedesktop.DBus",
                member: "NameHasOwner",
                signature: "s"
            );
            writer.WriteString(sender);
            return Wait(
                _observer.CallMethodAsync(
                    writer.CreateMessage(),
                    (message, _) => message.GetBodyReader().ReadBool()
                )
            );
        }

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            try
            {
                var call = context.Request;
                string method = call.MemberAsString;
                if (method == "Inhibit" && call.PathAsString == "/org/freedesktop/portal/desktop")
                    InhibitPortal(context);
                else if (method == "SimulateUserActivity")
                {
                    AssertEx.Equal("", call.SignatureAsString ?? "", "activity has no arguments");
                    ActivityPaths.Enqueue(call.PathAsString);
                    using var reply = context.CreateReplyWriter("");
                    context.Reply(reply.CreateMessage());
                }
                else if (method == "Inhibit" && call.PathAsString == "/org/freedesktop/ScreenSaver")
                {
                    AssertEx.Equal("ss", call.SignatureAsString, "ScreenSaver arguments");
                    var args = call.GetBodyReader();
                    AssertEx.Equal("SlopWorld", args.ReadString(), "application");
                    AssertEx.Equal("SlopWorld is running", args.ReadString(), "reason");
                    SaverSenders.Enqueue(call.SenderAsString);
                    using var reply = context.CreateReplyWriter(_badSaver ? "s" : "u");
                    if (_badSaver)
                        reply.WriteString("invalid cookie");
                    else
                        reply.WriteUInt32(42);
                    context.Reply(reply.CreateMessage());
                }
                else if (method == "Close" || method == "UnInhibit")
                {
                    if (method == "Close")
                        Interlocked.Increment(ref Closes);
                    else
                    {
                        AssertEx.Equal(42u, call.GetBodyReader().ReadUInt32(), "granted cookie");
                        Interlocked.Increment(ref Uninhibits);
                    }
                    if (_stallRelease)
                        return Stall(context);
                    using var reply = context.CreateReplyWriter("");
                    context.Reply(reply.CreateMessage());
                }
            }
            catch (Exception error)
            {
                _errors.Enqueue(error);
                context.ReplyError("org.slopworld.TestFailure", error.Message);
            }
            return default;
        }

        static async ValueTask Stall(MethodContext context)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, context.RequestAborted);
            }
            catch (OperationCanceledException) { }
        }

        void InhibitPortal(MethodContext context)
        {
            var call = context.Request;
            PortalSenders.Enqueue(call.SenderAsString);
            AssertEx.Equal("sua{sv}", call.SignatureAsString, "portal arguments");
            var args = call.GetBodyReader();
            AssertEx.Equal("", args.ReadString(), "unparented request");
            AssertEx.Equal(8u, args.ReadUInt32(), "idle inhibition only");
            var options = args.ReadDictionaryOfStringToVariantValue();
            AssertEx.Equal("SlopWorld is running", options["reason"].GetString(), "reason");
            string path =
                "/org/freedesktop/portal/desktop/request/"
                + call.SenderAsString.TrimStart(':').Replace('.', '_')
                + "/"
                + options["handle_token"].GetString();
            if (_mode != Mode.Silent)
            {
                // Response deliberately precedes the method reply.
                using var signal = context.DBusConnection.GetMessageWriter();
                signal.WriteSignalHeader(
                    path: path,
                    @interface: "org.freedesktop.portal.Request",
                    member: "Response",
                    signature: _mode == Mode.Malformed ? "s" : "ua{sv}"
                );
                if (_mode == Mode.Malformed)
                    signal.WriteString("not a status");
                else
                {
                    signal.WriteUInt32(_mode == Mode.Reject ? 2u : 0u);
                    signal.WriteDictionary(new Dictionary<string, VariantValue>());
                }
                AssertEx.True(
                    context.DBusConnection.TrySendMessage(signal.CreateMessage()),
                    "Response sent"
                );
            }
            using var reply = context.CreateReplyWriter("o");
            reply.WriteObjectPath(_mode == Mode.WrongHandle ? path + "_wrong" : path);
            context.Reply(reply.CreateMessage());
        }

        public void Dispose()
        {
            StopService();
            _observer?.Dispose();
            _process.Kill();
            _process.WaitForExit();
            _process.Dispose();
            Directory.Delete(_root, true);
            Assert.That(_errors, Is.Empty, string.Join("\n", _errors));
        }

        static T Wait<T>(Task<T> task) =>
            task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

        static void Wait(Task task) =>
            task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    }
}
