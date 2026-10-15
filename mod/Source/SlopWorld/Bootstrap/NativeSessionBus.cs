using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace SlopWorld
{
    // Narrow libdbus transport for idle inhibition, owned exclusively by one worker.
    // The system library is loaded lazily on Linux; no managed package or subprocess is needed.
    internal sealed class NativeSessionBus : IDisposable
    {
        const string Library = "libdbus-1.so.3";
        static readonly Lazy<bool> Threading = new Lazy<bool>(() =>
            dbus_threads_init_default() != 0
        );
        IntPtr _connection;
        internal IntPtr Handle => _connection;
        public string UniqueName { get; }

        public NativeSessionBus(string address, int timeout)
        {
            if (!Threading.Value)
                throw new InvalidOperationException("could not initialize D-Bus threading");
            _connection = dbus_connection_open_private(Utf8(address), IntPtr.Zero);
            if (_connection == IntPtr.Zero)
                throw new InvalidOperationException("could not connect to the desktop session bus");
            // Losing the desktop bus must never terminate the game.
            dbus_connection_set_exit_on_disconnect(_connection, 0);
            try
            {
                // Send Hello explicitly to keep registration's reply wait bounded, rather
                // than using dbus_bus_register's implicit default timeout.
                using var hello = Call(
                    "org.freedesktop.DBus",
                    "/org/freedesktop/DBus",
                    "org.freedesktop.DBus",
                    "Hello",
                    timeout
                );
                UniqueName = hello.FirstString('s');
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public Message Call(
            string service,
            string path,
            string iface,
            string method,
            int timeout,
            params object[] args
        )
        {
            using var request = new Message(
                dbus_message_new_method_call(Utf8(service), Utf8(path), Utf8(iface), Utf8(method))
            );
            request.Append(args);
            var reply = dbus_connection_send_with_reply_and_block(
                _connection,
                request.Handle,
                timeout,
                IntPtr.Zero
            );
            if (reply == IntPtr.Zero)
                throw new InvalidOperationException(
                    service + "." + method + " failed or timed out"
                );
            return new Message(reply);
        }

        public Message Read(int timeout)
        {
            var message = dbus_connection_pop_message(_connection);
            if (message == IntPtr.Zero)
            {
                if (dbus_connection_read_write(_connection, timeout) == 0)
                    throw new InvalidOperationException("desktop session bus disconnected");
                message = dbus_connection_pop_message(_connection);
            }
            return message == IntPtr.Zero ? null : new Message(message);
        }

        public void Dispose()
        {
            if (_connection == IntPtr.Zero)
                return;
            dbus_connection_close(_connection);
            dbus_connection_unref(_connection);
            _connection = IntPtr.Zero;
        }

        internal sealed class Message : IDisposable
        {
            public IntPtr Handle { get; private set; }
            public string Sender => Marshal.PtrToStringUTF8(dbus_message_get_sender(Handle));
            public string Path => Marshal.PtrToStringUTF8(dbus_message_get_path(Handle));
            public string Signature => Marshal.PtrToStringUTF8(dbus_message_get_signature(Handle));

            public Message(IntPtr handle)
            {
                if (handle == IntPtr.Zero)
                    throw new InvalidOperationException("could not allocate a D-Bus message");
                Handle = handle;
            }

            public bool IsSignal(string iface, string member) =>
                dbus_message_is_signal(Handle, Utf8(iface), Utf8(member)) != 0;

            public uint FirstUInt()
            {
                var iter = First('u');
                dbus_message_iter_get_basic_uint(ref iter, out uint value);
                return value;
            }

            public string FirstString(char type)
            {
                var iter = First(type);
                dbus_message_iter_get_basic_pointer(ref iter, out IntPtr value);
                return Marshal.PtrToStringUTF8(value);
            }

            Iterator First(char type)
            {
                if (
                    dbus_message_iter_init(Handle, out var iter) == 0
                    || dbus_message_iter_get_arg_type(ref iter) != type
                )
                    throw new InvalidOperationException("unexpected D-Bus reply signature");
                return iter;
            }

            public void Append(params object[] args)
            {
                dbus_message_iter_init_append(Handle, out var iter);
                foreach (object arg in args)
                {
                    if (arg is string text)
                        AppendString(ref iter, text);
                    else if (arg is uint number)
                        Check(dbus_message_iter_append_uint(ref iter, 'u', ref number));
                    else if (arg is Dictionary<string, string> options)
                    {
                        // Once containers are open, any failure discards the entire message.
                        // No partially built iterator is reused by a subsequent call.
                        Check(
                            dbus_message_iter_open_container(
                                ref iter,
                                'a',
                                Utf8("{sv}"),
                                out var dict
                            )
                        );
                        foreach (var option in options)
                        {
                            Check(
                                dbus_message_iter_open_container(ref dict, 'e', null, out var entry)
                            );
                            AppendString(ref entry, option.Key);
                            Check(
                                dbus_message_iter_open_container(
                                    ref entry,
                                    'v',
                                    Utf8("s"),
                                    out var variant
                                )
                            );
                            AppendString(ref variant, option.Value);
                            Check(dbus_message_iter_close_container(ref entry, ref variant));
                            Check(dbus_message_iter_close_container(ref dict, ref entry));
                        }
                        Check(dbus_message_iter_close_container(ref iter, ref dict));
                    }
                    else
                        throw new ArgumentException("unsupported D-Bus argument");
                }
            }

            static void AppendString(ref Iterator iter, string text)
            {
                IntPtr value = Marshal.StringToCoTaskMemUTF8(text);
                try
                {
                    Check(dbus_message_iter_append_pointer(ref iter, 's', ref value));
                }
                finally
                {
                    Marshal.FreeCoTaskMem(value);
                }
            }

            static void Check(int success)
            {
                if (success == 0)
                    throw new InvalidOperationException("could not encode D-Bus arguments");
            }

            public void Dispose()
            {
                if (Handle == IntPtr.Zero)
                    return;
                dbus_message_unref(Handle);
                Handle = IntPtr.Zero;
            }
        }

        // Public libdbus DBusMessageIter ABI: opaque fields, including native pointer
        // alignment. Do not read these fields; all access goes through libdbus.
        [StructLayout(LayoutKind.Sequential)]
        struct Iterator
        {
            IntPtr dummy1,
                dummy2;
            uint dummy3;
            int dummy4,
                dummy5,
                dummy6,
                dummy7,
                dummy8,
                dummy9,
                dummy10,
                dummy11,
                pad1;
            IntPtr pad2,
                pad3;
        }

        // Header strings use terminated UTF-8 buffers. Payload strings use char **.
        static byte[] Utf8(string value) =>
            value == null ? null : Encoding.UTF8.GetBytes(value + "\0");

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern int dbus_threads_init_default();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr dbus_connection_open_private(byte[] address, IntPtr error);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern void dbus_connection_set_exit_on_disconnect(IntPtr connection, int value);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern void dbus_connection_close(IntPtr connection);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern void dbus_connection_unref(IntPtr connection);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr dbus_connection_send_with_reply_and_block(
            IntPtr connection,
            IntPtr message,
            int timeout,
            IntPtr error
        );

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern int dbus_connection_read_write(IntPtr connection, int timeout);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr dbus_connection_pop_message(IntPtr connection);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr dbus_message_new_method_call(
            byte[] service,
            byte[] path,
            byte[] iface,
            byte[] method
        );

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern void dbus_message_unref(IntPtr message);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr dbus_message_get_sender(IntPtr message);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr dbus_message_get_path(IntPtr message);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr dbus_message_get_signature(IntPtr message);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern int dbus_message_is_signal(IntPtr message, byte[] iface, byte[] member);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern int dbus_message_iter_init(IntPtr message, out Iterator iter);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern void dbus_message_iter_init_append(IntPtr message, out Iterator iter);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern int dbus_message_iter_get_arg_type(ref Iterator iter);

        [DllImport(
            Library,
            EntryPoint = "dbus_message_iter_get_basic",
            CallingConvention = CallingConvention.Cdecl
        )]
        static extern void dbus_message_iter_get_basic_uint(ref Iterator iter, out uint value);

        [DllImport(
            Library,
            EntryPoint = "dbus_message_iter_get_basic",
            CallingConvention = CallingConvention.Cdecl
        )]
        static extern void dbus_message_iter_get_basic_pointer(ref Iterator iter, out IntPtr value);

        [DllImport(
            Library,
            EntryPoint = "dbus_message_iter_append_basic",
            CallingConvention = CallingConvention.Cdecl
        )]
        static extern int dbus_message_iter_append_uint(
            ref Iterator iter,
            int type,
            ref uint value
        );

        [DllImport(
            Library,
            EntryPoint = "dbus_message_iter_append_basic",
            CallingConvention = CallingConvention.Cdecl
        )]
        static extern int dbus_message_iter_append_pointer(
            ref Iterator iter,
            int type,
            ref IntPtr value
        );

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern int dbus_message_iter_open_container(
            ref Iterator iter,
            int type,
            byte[] signature,
            out Iterator sub
        );

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern int dbus_message_iter_close_container(ref Iterator iter, ref Iterator sub);
    }
}
