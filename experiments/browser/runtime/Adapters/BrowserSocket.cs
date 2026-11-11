// Browser socket transport for the staged mod. HubTransport retains connection
// generations, retries, event parsing and dispatch; its original queue owns data.
using System.Net.WebSockets;
using System.Reflection;

namespace BrowserAdapters;

public static class BrowserSocket
{
    const BindingFlags Fields =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    sealed class State
    {
        public readonly ClientWebSocket Socket = new();
        public readonly CancellationTokenSource Stop = new();
        public readonly Queue<byte[]> Outgoing = new();
        public int OutgoingBytes;
        public bool Writing;
        public bool Closed;
        public required object Owner;
        public required object Incoming;
    }

    static readonly Dictionary<object, State> sockets = new();

    static object? Field(object owner, string name) =>
        owner.GetType().GetField(name, Fields)!.GetValue(owner);

    static void Connected(object owner, bool value) =>
        owner.GetType().GetField("_connected", Fields)!.SetValue(owner, value);

    static void Error(object owner, string value) =>
        owner.GetType().GetProperty("LastError")!.SetValue(owner, value);

    // Async completion is published only after the handshake, avoiding a synchronous
    // wait on the browser thread. The mod rejects stale serials in PumpConnectResults.
    public static async void Open(object hub, object connection, int serial)
    {
        var assembly = hub.GetType().Assembly;
        var result = Activator.CreateInstance(
            assembly.GetType("SlopWorld.HubTransport+ConnectResult")!,
            true
        )!;
        result.GetType().GetField("Serial")!.SetValue(result, serial);
        object? owner = null;
        try
        {
            owner = Activator.CreateInstance(assembly.GetType("SlopWorld.MiniWebSocket")!)!;
            result.GetType().GetField("Socket")!.SetValue(result, owner);
            var state = new State { Owner = owner, Incoming = Field(owner, "Incoming")! };
            sockets.Add(owner, state);
            state.Socket.Options.AddSubProtocol("slopworld.protobuf.v2");
            var uri = new Uri(BrowserTransport.BaseUrl.Replace("http://", "ws://") + "/ws");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(state.Stop.Token);
            deadline.CancelAfter(5000);
            await state.Socket.ConnectAsync(uri, deadline.Token);
            Connected(owner, true);
            _ = Receive(state);
        }
        catch (Exception e)
        {
            result.GetType().GetField("Error")!.SetValue(result, e.Message);
            if (owner != null)
            {
                Error(owner, e.Message);
                Dispose(owner);
            }
        }
        var results = Field(hub, "_connectResults")!;
        results.GetType().GetMethod("Enqueue")!.Invoke(results, new[] { result });
        BrowserActivity.Notify();
    }

    static async Task Receive(State state)
    {
        var buffer = new byte[65536];
        try
        {
            while (!state.Closed)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult part;
                do
                {
                    part = await state.Socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        state.Stop.Token
                    );
                    if (part.MessageType != WebSocketMessageType.Binary)
                        throw new IOException("Socket closed or non-binary message");
                    if (message.Length + part.Count > 8 * 1024 * 1024)
                        throw new IOException("Oversized socket message");
                    message.Write(buffer, 0, part.Count);
                } while (!part.EndOfMessage);
                if (state.Closed)
                    break;
                var type = state.Incoming.GetType();
                var payload = message.ToArray();
                // The native queue blocks when full. A browser cannot block its UI
                // thread; close instead so hub reconnect restores authoritative state.
                if (
                    (int)type.GetProperty("Count")!.GetValue(state.Incoming)! >= 256
                    || (int)type.GetProperty("Bytes")!.GetValue(state.Incoming)! + payload.Length
                        > 16 * 1024 * 1024
                )
                    throw new IOException("Browser event queue overloaded");
                var accepted = type.GetMethod("Enqueue")!
                    .Invoke(state.Incoming, new object[] { payload });
                if (accepted?.ToString() != "Accepted")
                    throw new IOException("Event queue closed");
                BrowserActivity.Notify();
            }
        }
        catch (Exception e)
        {
            if (!state.Closed)
                Error(state.Owner, e.Message);
        }
        finally
        {
            Dispose(state.Owner);
        }
    }

    public static bool Send(object owner, byte[] payload)
    {
        if (
            !sockets.TryGetValue(owner, out var state)
            || state.Closed
            || state.Socket.State != WebSocketState.Open
            || payload.Length == 0
        )
            return false;
        if (state.OutgoingBytes + payload.Length > 16 * 1024 * 1024)
        {
            Error(owner, "Browser send queue overloaded");
            Dispose(owner);
            return false;
        }
        state.Outgoing.Enqueue((byte[])payload.Clone());
        state.OutgoingBytes += payload.Length;
        if (!state.Writing)
            _ = Write(state);
        return true;
    }

    static async Task Write(State state)
    {
        state.Writing = true;
        try
        {
            while (!state.Closed && state.Outgoing.TryDequeue(out var payload))
            {
                await state.Socket.SendAsync(
                    new ArraySegment<byte>(payload),
                    WebSocketMessageType.Binary,
                    true,
                    state.Stop.Token
                );
                state.OutgoingBytes -= payload.Length;
            }
        }
        catch (Exception e)
        {
            if (!state.Closed)
                Error(state.Owner, e.Message);
            Dispose(state.Owner);
        }
        finally
        {
            state.Writing = false;
        }
    }

    public static bool Flush(object owner, int timeoutMs) =>
        sockets.TryGetValue(owner, out var state)
        && !state.Closed
        && !state.Writing
        && state.OutgoingBytes == 0;

    public static void Dispose(object owner)
    {
        Connected(owner, false);
        if (!sockets.Remove(owner, out var state))
            return;
        state.Closed = true;
        state.Stop.Cancel();
        state.Socket.Abort();
        state.Socket.Dispose();
        state.Incoming.GetType().GetMethod("Close")!.Invoke(state.Incoming, null);
        state.Outgoing.Clear();
        (Field(owner, "_sendSignal") as IDisposable)?.Dispose();
        (Field(owner, "_rng") as IDisposable)?.Dispose();
        state.Stop.Dispose();
        BrowserActivity.Notify();
    }
}
