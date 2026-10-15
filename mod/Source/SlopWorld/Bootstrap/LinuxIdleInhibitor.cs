using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SlopWorld
{
    // Desktop protocol policy. Each attempt gets its own private connection so a
    // timeout/rejection cannot leave an outstanding portal request behind in fallback.
    internal sealed class LinuxIdleInhibitor : IDisposable
    {
        const string Portal = "org.freedesktop.portal.Desktop";
        const string PortalPath = "/org/freedesktop/portal/desktop";
        const string Request = "org.freedesktop.portal.Request";
        const string Saver = "org.freedesktop.ScreenSaver";
        const string SaverPath = "/org/freedesktop/ScreenSaver";
        const string Reason = "SlopWorld is running";
        readonly NativeSessionBus _bus;
        readonly string _requestPath;
        readonly uint _cookie;
        bool _disposed;

        LinuxIdleInhibitor(NativeSessionBus bus, string path, uint cookie)
        {
            _bus = bus;
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
                NativeSessionBus bus = null;
                try
                {
                    bus = new NativeSessionBus(address, timeout);
                    if (portal)
                        return AcquirePortal(bus, timeout);
                    using var reply = bus.Call(
                        Saver,
                        SaverPath,
                        Saver,
                        "Inhibit",
                        timeout,
                        "SlopWorld",
                        Reason
                    );
                    return new LinuxIdleInhibitor(bus, null, reply.FirstUInt());
                }
                catch (Exception error)
                {
                    bus?.Dispose();
                    failures.Add(error);
                }
            }
            throw new AggregateException("desktop idle inhibition is unavailable", failures);
        }

        static LinuxIdleInhibitor AcquirePortal(NativeSessionBus bus, int timeout)
        {
            var clock = Stopwatch.StartNew();
            int Remaining() => Math.Max(1, timeout - (int)clock.ElapsedMilliseconds);
            string token = "slopworld_" + Guid.NewGuid().ToString("N");
            string sender = bus.UniqueName.TrimStart(':').Replace('.', '_');
            string path = PortalPath + "/request/" + sender + "/" + token;
            // Install the signal match before Inhibit: Response can precede the method reply.
            using (
                var match = bus.Call(
                    "org.freedesktop.DBus",
                    "/org/freedesktop/DBus",
                    "org.freedesktop.DBus",
                    "AddMatch",
                    Remaining(),
                    "type='signal',sender='"
                        + Portal
                        + "',interface='"
                        + Request
                        + "',member='Response',path='"
                        + path
                        + "'"
                )
            ) { }
            using var reply = bus.Call(
                Portal,
                PortalPath,
                "org.freedesktop.portal.Inhibit",
                "Inhibit",
                Remaining(),
                "",
                8u,
                new Dictionary<string, string> { ["handle_token"] = token, ["reason"] = Reason }
            );
            if (reply.FirstString('o') != path)
                throw new InvalidOperationException("portal returned an unexpected request handle");
            string owner = reply.Sender;
            while (clock.ElapsedMilliseconds < timeout)
            {
                using var response = bus.Read(Math.Min(50, Remaining()));
                if (
                    response == null
                    || !response.IsSignal(Request, "Response")
                    || response.Path != path
                    || response.Sender != owner
                )
                    continue;
                if (response.Signature != "ua{sv}" || response.FirstUInt() != 0)
                    throw new InvalidOperationException("portal denied idle inhibition");
                return new LinuxIdleInhibitor(bus, path, 0);
            }
            throw new TimeoutException("portal idle inhibition response timed out");
        }

        // Legacy activity notifications complement the X11 fallback when neither
        // inhibition protocol is available. A fresh connection allows later ticks
        // to recover from a missing/restarted session bus without retaining replies.
        internal static void Tickle(string address = null)
        {
            address ??= Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
            if (string.IsNullOrEmpty(address))
                return;
            try
            {
                using var bus = new NativeSessionBus(address, 200);
                foreach (string service in new[] { "org.gnome.ScreenSaver", Saver })
                {
                    try
                    {
                        using var reply = bus.Call(
                            service,
                            service == Saver ? SaverPath : "/org/gnome/ScreenSaver",
                            service,
                            "SimulateUserActivity",
                            200
                        );
                    }
                    catch (Exception)
                    {
                        // One missing legacy service must not prevent the other tickle.
                    }
                }
            }
            catch (Exception)
            {
                // Keep the independent X11 protection if D-Bus remains unavailable.
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                using var reply =
                    _requestPath != null
                        ? _bus.Call(Portal, _requestPath, Request, "Close", 200)
                        : _bus.Call(Saver, SaverPath, Saver, "UnInhibit", 200, _cookie);
            }
            catch (Exception)
            {
                // Service disappearance or a stalled release must not retain the connection.
            }
            finally
            {
                _bus.Dispose();
            }
        }
    }
}
