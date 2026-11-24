using System.Net.WebSockets;
using SlopWorld;

namespace BrowserAdapters;

internal sealed class BrowserSocket : IHubSocket
{
    readonly ClientWebSocket Socket = new();
    readonly CancellationTokenSource Stop = new();
    readonly Queue<byte[]> Outgoing = new();
    int OutgoingBytes;
    bool Writing;
    bool Closed;
    public bool Connected { get; private set; }
    public string LastError { get; private set; } = "";
    public IncomingMessageQueue Incoming { get; } = new();

    public async Task<bool> ConnectAsync(ConnectionInfo connection, CancellationToken cancellation)
    {
        Socket.Options.AddSubProtocol("slopworld.protobuf.v2");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            Stop.Token,
            cancellation
        );
        deadline.CancelAfter(5000);
        await Socket.ConnectAsync(
            BrowserTransport.ResolveSocket(connection.BaseUrl),
            deadline.Token
        );
        Connected = true;
        _ = Receive();
        return true;
    }

    async Task Receive()
    {
        var buffer = new byte[65536];
        try
        {
            while (!Closed)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult part;
                do
                {
                    part = await Socket.ReceiveAsync(new ArraySegment<byte>(buffer), Stop.Token);
                    if (part.MessageType != WebSocketMessageType.Binary)
                        throw new IOException("Socket closed or non-binary message");
                    if (message.Length + part.Count > IncomingMessageQueue.MaxMessageBytes)
                        throw new IOException("Oversized socket message");
                    message.Write(buffer, 0, part.Count);
                } while (!part.EndOfMessage);
                if (Closed)
                    break;
                var admitted = Incoming.TryEnqueue(message.ToArray());
                if (admitted != IncomingEnqueueResult.Accepted)
                    throw new IOException("Browser event queue rejected message: " + admitted);
                BrowserActivity.Notify();
            }
        }
        catch (Exception e)
        {
            if (!Closed)
                LastError = e.Message;
        }
        finally
        {
            Dispose();
        }
    }

    public bool SendBinary(byte[] payload)
    {
        if (Closed || Socket.State != WebSocketState.Open || payload.Length == 0)
            return false;
        if (!BrowserSocketLimits.FitsOutgoing(OutgoingBytes, payload.Length))
        {
            LastError = "Browser send queue overloaded";
            Dispose();
            return false;
        }
        Outgoing.Enqueue((byte[])payload.Clone());
        OutgoingBytes += payload.Length;
        if (!Writing)
            _ = Write();
        return true;
    }

    async Task Write()
    {
        Writing = true;
        try
        {
            while (!Closed && Outgoing.TryDequeue(out var payload))
            {
                await Socket.SendAsync(
                    new ArraySegment<byte>(payload),
                    WebSocketMessageType.Binary,
                    true,
                    Stop.Token
                );
                OutgoingBytes -= payload.Length;
            }
        }
        catch (Exception e)
        {
            if (!Closed)
                LastError = e.Message;
            Dispose();
        }
        finally
        {
            Writing = false;
        }
    }

    public bool FlushOutgoing(int timeoutMs) => Connected && !Writing && OutgoingBytes == 0;

    public void Dispose()
    {
        if (Closed)
            return;
        Connected = false;
        Closed = true;
        Stop.Cancel();
        Socket.Abort();
        Socket.Dispose();
        Incoming.Close();
        Outgoing.Clear();
        Stop.Dispose();
        BrowserActivity.Notify();
    }
}
