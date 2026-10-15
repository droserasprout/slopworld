using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace SlopWorld
{
    // Desktop protocol policy; Tmds owns transport, authentication and message dispatch.
    // Each attempt has a private connection, closed on timeout before trying the fallback.
    internal sealed class LinuxIdleInhibitor : IIdleInhibitionLease
    {
        const string Portal = "org.freedesktop.portal.Desktop";
        const string PortalPath = "/org/freedesktop/portal/desktop";
        const string Request = "org.freedesktop.portal.Request";
        const string Saver = "org.freedesktop.ScreenSaver";
        const string SaverPath = "/org/freedesktop/ScreenSaver";
        const string Reason = "SlopWorld is running";
        readonly DBusConnection _bus;
        readonly string _owner;
        readonly string _requestPath;
        readonly uint _cookie;
        bool _disposed;

        LinuxIdleInhibitor(DBusConnection bus, string owner, string path, uint cookie)
        {
            _bus = bus;
            _owner = owner;
            _requestPath = path;
            _cookie = cookie;
        }

        internal static LinuxIdleInhibitor Acquire(string address = null, int timeout = 1000)
        {
            address ??= Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
            if (string.IsNullOrEmpty(address))
                throw new InvalidOperationException("desktop session bus address is unavailable");
            var failures = new List<Exception>();
            foreach (bool portal in new[] { true, false })
            {
                var bus = new DBusConnection(address);
                try
                {
                    Wait(bus.ConnectAsync().AsTask(), timeout);
                    if (portal)
                        return AcquirePortal(bus, timeout);
                    using var writer = bus.GetMessageWriter();
                    writer.WriteMethodCallHeader(
                        destination: Saver,
                        path: SaverPath,
                        @interface: Saver,
                        member: "Inhibit",
                        signature: "ss"
                    );
                    writer.WriteString("SlopWorld");
                    writer.WriteString(Reason);
                    var grant = Wait(
                        bus.CallMethodAsync(
                            writer.CreateMessage(),
                            (message, _) =>
                            {
                                RequireSignature(message, "u");
                                return (
                                    Owner: message.SenderAsString,
                                    Cookie: message.GetBodyReader().ReadUInt32()
                                );
                            }
                        ),
                        timeout
                    );
                    return new LinuxIdleInhibitor(bus, grant.Owner, null, grant.Cookie);
                }
                catch (Exception error)
                {
                    bus.Dispose();
                    failures.Add(error);
                }
            }
            throw new AggregateException("desktop idle inhibition is unavailable", failures);
        }

        static LinuxIdleInhibitor AcquirePortal(DBusConnection bus, int timeout)
        {
            var clock = Stopwatch.StartNew();
            int Remaining() => Math.Max(1, timeout - (int)clock.ElapsedMilliseconds);
            string token = "slopworld_" + Guid.NewGuid().ToString("N");
            string path =
                PortalPath
                + "/request/"
                + bus.UniqueName.TrimStart(':').Replace('.', '_')
                + "/"
                + token;
            var response = new TaskCompletionSource<(uint Status, string Owner)>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            // Subscribe before Inhibit: the Response can arrive before the method reply.
            using var match = Wait(
                bus.AddMatchAsync(
                        new MatchRule
                        {
                            Type = MessageType.Signal,
                            Sender = Portal,
                            Path = path,
                            Interface = Request,
                            Member = "Response",
                        },
                        (message, _) =>
                        {
                            RequireSignature(message, "ua{sv}");
                            var reader = message.GetBodyReader();
                            uint status = reader.ReadUInt32();
                            _ = reader.ReadDictionaryOfStringToVariantValue();
                            return (status, message.SenderAsString);
                        },
                        notification =>
                        {
                            if (notification.HasValue)
                                response.TrySetResult(notification.Value);
                            else if (notification.IsCompletion)
                                response.TrySetException(
                                    notification.Exception
                                        ?? new InvalidOperationException(
                                            "portal response subscription closed"
                                        )
                                );
                        },
                        emitOnCapturedContext: false
                    )
                    .AsTask(),
                Remaining()
            );
            using var writer = bus.GetMessageWriter();
            writer.WriteMethodCallHeader(
                destination: Portal,
                path: PortalPath,
                @interface: "org.freedesktop.portal.Inhibit",
                member: "Inhibit",
                signature: "sua{sv}"
            );
            writer.WriteString("");
            writer.WriteUInt32(8);
            writer.WriteDictionary(
                new Dictionary<string, VariantValue>
                {
                    ["handle_token"] = token,
                    ["reason"] = Reason,
                }
            );
            var grant = Wait(
                bus.CallMethodAsync(
                    writer.CreateMessage(),
                    (message, _) =>
                    {
                        RequireSignature(message, "o");
                        return (
                            Owner: message.SenderAsString,
                            Path: message.GetBodyReader().ReadObjectPathAsString()
                        );
                    }
                ),
                Remaining()
            );
            if (grant.Path != path)
                throw new InvalidOperationException("portal returned an unexpected request handle");
            var result = Wait(response.Task, Remaining());
            if (result.Status != 0 || result.Owner != grant.Owner)
                throw new InvalidOperationException("portal denied idle inhibition");
            return new LinuxIdleInhibitor(bus, grant.Owner, path, 0);
        }

        public void Refresh()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // A connection can survive the service that granted its lease. Compare owners,
            // rather than treating a live bus socket as proof the inhibitor still exists.
            using var writer = _bus.GetMessageWriter();
            writer.WriteMethodCallHeader(
                destination: "org.freedesktop.DBus",
                path: "/org/freedesktop/DBus",
                @interface: "org.freedesktop.DBus",
                member: "GetNameOwner",
                signature: "s"
            );
            writer.WriteString(_requestPath == null ? Saver : Portal);
            string owner = Wait(
                _bus.CallMethodAsync(
                    writer.CreateMessage(),
                    (message, _) => message.GetBodyReader().ReadString()
                ),
                200
            );
            if (owner != _owner)
                throw new InvalidOperationException("desktop idle inhibitor service restarted");
        }

        internal static void Tickle(string address = null)
        {
            address ??= Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
            if (string.IsNullOrEmpty(address))
                return;
            using var bus = new DBusConnection(address);
            try
            {
                Wait(bus.ConnectAsync().AsTask(), 200);
                foreach (string service in new[] { "org.gnome.ScreenSaver", Saver })
                {
                    try
                    {
                        using var writer = bus.GetMessageWriter();
                        writer.WriteMethodCallHeader(
                            destination: service,
                            path: service == Saver ? SaverPath : "/org/gnome/ScreenSaver",
                            @interface: service,
                            member: "SimulateUserActivity"
                        );
                        Wait(bus.CallMethodAsync(writer.CreateMessage()), 200);
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception)
            {
                // Legacy notifications are best effort; X11 remains independent.
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                using var writer = _bus.GetMessageWriter();
                // Address the original owner: a restarted service may reuse the same cookie.
                writer.WriteMethodCallHeader(
                    destination: _owner,
                    path: _requestPath ?? SaverPath,
                    @interface: _requestPath != null ? Request : Saver,
                    member: _requestPath != null ? "Close" : "UnInhibit",
                    signature: _requestPath != null ? "" : "u"
                );
                if (_requestPath == null)
                    writer.WriteUInt32(_cookie);
                Wait(_bus.CallMethodAsync(writer.CreateMessage()), 200);
            }
            catch (Exception) { }
            finally
            {
                _bus.Dispose();
            }
        }

        static void RequireSignature(Message message, string signature)
        {
            if (message.SignatureAsString != signature)
                throw new InvalidOperationException("unexpected D-Bus reply signature");
        }

        // A timed-out attempt is always followed by connection disposal by its owner.
        static T Wait<T>(Task<T> task, int timeout) =>
            task.WaitAsync(TimeSpan.FromMilliseconds(timeout)).GetAwaiter().GetResult();

        static void Wait(Task task, int timeout) =>
            task.WaitAsync(TimeSpan.FromMilliseconds(timeout)).GetAwaiter().GetResult();
    }
}
