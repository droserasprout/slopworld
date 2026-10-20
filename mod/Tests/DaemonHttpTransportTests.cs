using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    [NonParallelizable]
    public sealed class DaemonHttpTransportTests
    {
        [TestCase("GET", false)]
        [TestCase("POST", true)]
        [TestCase("PUT", true)]
        [TestCase("DELETE", false)]
        [TestCase("DELETE", true)]
        public async Task SendsProtocolHeadersAndBodies(string method, bool hasBody)
        {
            using var peer = new Peer();
            var body = new Wire.Error { Error_ = "request payload" };
            var server = peer.Serve(new Wire.Error { Error_ = "reply" }.ToByteArray());
            using var transport = new ClientEndpoint(peer.Connection);
            var reply = await transport.Send<Wire.Error>(
                method,
                "/test?q=one",
                hasBody ? body : null,
                "worker",
                5000
            );
            await server;
            Assert.That(reply.Error_, Is.EqualTo("reply"));
            Assert.That(peer.Header, Does.StartWith(method + " /test?q=one HTTP/1.1\r\n"));
            Assert.That(
                peer.Header,
                Does.Contain(WireProtocol.TokenHeader + ": secret").IgnoreCase
            );
            Assert.That(
                peer.Header,
                Does.Contain(WireProtocol.SessionHeader + ": worker").IgnoreCase
            );
            Assert.That(peer.Header, Does.Contain("Accept: application/x-protobuf").IgnoreCase);
            Assert.That(peer.Body, Is.EqualTo(hasBody ? body.ToByteArray() : Array.Empty<byte>()));
            if (hasBody)
                Assert.That(
                    peer.Header,
                    Does.Contain("Content-Type: application/x-protobuf").IgnoreCase
                );
        }

        [Test]
        public async Task ResolvesEachRequestAndOmitsEmptyCredentials()
        {
            using var first = new Peer();
            using var second = new Peer();
            var connection = first.Connection;
            using var transport = new ClientEndpoint(connection);
            var firstServer = first.Serve(Array.Empty<byte>());
            await transport.Send<Wire.Empty>("GET", "/first", null, null, 5000);
            await firstServer;
            connection = new ConnectionInfo("127.0.0.1", second.Connection.Port, "");
            transport.Set(connection);
            var secondServer = second.Serve(Array.Empty<byte>());
            await transport.Send<Wire.Empty>("GET", "/second", null, "", 5000);
            await secondServer;
            Assert.That(second.Header, Does.Not.Contain(WireProtocol.TokenHeader).IgnoreCase);
            Assert.That(second.Header, Does.Not.Contain(WireProtocol.SessionHeader).IgnoreCase);
        }

        [TestCase(
            "application/json",
            new byte[] { 123, 125 },
            "Expected a Protobuf protocol 2 response"
        )]
        [TestCase("application/x-protobuf", new byte[] { 255 }, null)]
        public async Task RejectsIncompatibleOrMalformedSuccess(
            string contentType,
            byte[] body,
            string message
        )
        {
            using var peer = new Peer();
            var server = peer.Serve(body, contentType: contentType);
            using var transport = new ClientEndpoint(peer.Connection);
            var error = Assert.CatchAsync(async () =>
                await transport.Send<Wire.Empty>("GET", "/", null, null, 5000)
            );
            if (message != null)
                Assert.That(error.Message, Does.Contain(message));
            await server;
        }

        [TestCase("explanation")]
        [TestCase("malformed")]
        [TestCase("empty")]
        public async Task ErrorRepliesPreferDaemonExplanationAndOtherwiseKeepHttpFailure(
            string kind
        )
        {
            using var peer = new Peer();
            var body =
                kind == "explanation"
                    ? new Wire.Error { Error_ = "permission denied by daemon" }.ToByteArray()
                : kind == "malformed" ? new byte[] { 255 }
                : Array.Empty<byte>();
            var server = peer.Serve(body, status: "403 Forbidden");
            using var transport = new ClientEndpoint(peer.Connection);
            var error = Assert.CatchAsync(async () =>
                await transport.Send<Wire.Empty>("GET", "/", null, null, 5000)
            );
            Assert.That(
                error.Message,
                Does.Contain(kind == "explanation" ? "permission denied by daemon" : "403")
            );
            await server;
        }

        [Test]
        public async Task QueuedDeadlineDoesNotConsumeASlotAndPoolRecovers()
        {
            using var peer = new Peer();
            using var transport = new ClientEndpoint(peer.Connection);
            await WithFullPool(
                peer,
                transport,
                () =>
                    Assert.CatchAsync<IOException>(async () =>
                        await transport.Send<Wire.Empty>("GET", "/queued", null, null, 100)
                    )
            );
            await WithFullPool(peer, transport);
        }

        // Admission is observed at the peer; held responses keep every permit occupied.
        // Repeating this after failure proves all eight permits can be acquired again.
        static async Task WithFullPool(Peer peer, ClientEndpoint transport, Action whileHeld = null)
        {
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var requests = new Task<Wire.Empty>[8];
            var servers = new Task[8];
            var accepted = new Task[8];
            for (int i = 0; i < requests.Length; i++)
            {
                var ready = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                accepted[i] = ready.Task;
                servers[i] = peer.Serve(
                    new byte[] { 10, 0 },
                    release: release.Task,
                    accepted: ready
                );
                requests[i] = transport.Send<Wire.Empty>("GET", "/held", null, null, 10000);
            }
            try
            {
                await Task.WhenAll(accepted).WaitAsync(TimeSpan.FromSeconds(5));
                whileHeld?.Invoke();
                Assert.That(
                    Array.TrueForAll(requests, request => !request.IsCompleted),
                    Is.True,
                    "held responses retain their requests"
                );
            }
            finally
            {
                release.TrySetResult(true);
                await Task.WhenAll(servers);
                await Task.WhenAll(requests);
            }
        }

        [Test]
        public async Task DeadlineAbortsStalledResponseAndReleasesSlot()
        {
            using var peer = new Peer();
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            using var transport = new ClientEndpoint(peer.Connection);
            var server = peer.Serve(new byte[] { 0 }, release: release.Task);
            try
            {
                // Allow the same connection budget as the other loopback tests.
                // Coverage on a busy runner can consume a one-second deadline
                // before request headers reach the peer; that never tests a stalled body.
                var request = transport.Send<Wire.Empty>("GET", "/stalled", null, null, 5000);
                await peer.Accepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.CatchAsync<IOException>(async () =>
                    await request.WaitAsync(TimeSpan.FromSeconds(10))
                );
            }
            finally
            {
                release.TrySetResult(true);
                await server;
            }
            await WithFullPool(peer, transport);
        }

        [Test]
        public async Task ResponseLimitAcceptsBoundaryAndRejectsOverflow()
        {
            using var boundary = new MemoryStream(new byte[32 * 1024 * 1024]);
            Assert.That(
                (await ReadBounded(boundary, CancellationToken.None)).Length,
                Is.EqualTo(boundary.Length)
            );
            using var overflow = new MemoryStream(new byte[32 * 1024 * 1024 + 1]);
            Assert.ThrowsAsync<IOException>(async () =>
                await ReadBounded(overflow, CancellationToken.None)
            );
        }

        [Test]
        public void ResponseReadHonorsCancellation()
        {
            using var stream = new MemoryStream(new byte[] { 1 });
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await ReadBounded(stream, cancellation.Token)
            );
        }

        // Real HTTP peer with bounded waits and socket cleanup, independent of game substitutes.
        [TestCase(false)]
        [TestCase(true)]
        public async Task DispatcherDefersSuccessAndFailureUntilHostPump(bool failure)
        {
            using var peer = new Peer();
            using var endpoint = new ClientEndpoint(peer.Connection);
            var server = peer.Serve(
                new Wire.Error { Error_ = "reply" }.ToByteArray(),
                status: failure ? "400 Bad Request" : "200 OK"
            );
            string result = null;
            string error = null;
            int callbackThread = 0;
            endpoint.Client.Send<Wire.Error>(
                "GET",
                "/test",
                null,
                reply =>
                {
                    result = reply.Error_;
                    callbackThread = Environment.CurrentManagedThreadId;
                },
                message =>
                {
                    error = message;
                    callbackThread = Environment.CurrentManagedThreadId;
                }
            );
            await server;
            using var deadline = new CancellationTokenSource(5000);
            while (endpoint.Client.PendingCompletions == 0)
                await Task.Delay(1, deadline.Token);
            Assert.That(result, Is.Null);
            Assert.That(error, Is.Null);
            int pumpThread = Environment.CurrentManagedThreadId;
            Assert.That(endpoint.Client.PumpCompletions(e => throw e), Is.EqualTo(1));
            Assert.That(callbackThread, Is.EqualTo(pumpThread));
            Assert.That(result, failure ? Is.Null : Is.EqualTo("reply"));
            Assert.That(error, failure ? Is.EqualTo("reply") : Is.Null);
            Assert.That(endpoint.Client.PendingCompletions, Is.Zero);
            Assert.That(endpoint.Client.PumpCompletions(e => throw e), Is.Zero);
        }

        static Task<byte[]> ReadBounded(Stream stream, CancellationToken token) =>
            (Task<byte[]>)
                typeof(DaemonHttpTransport)
                    .GetMethod(
                        "ReadBounded",
                        System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.Static
                    )
                    .Invoke(null, new object[] { stream, token });

        // Keep real endpoint resolution and HTTP dispatch; only adapt callbacks to awaitable tests.
        sealed class ClientEndpoint : IDisposable
        {
            public readonly DaemonHttpTransport Client = new DaemonHttpTransport();
            readonly object pumpGate = new object();
            readonly string descriptor = Path.GetTempFileName();
            readonly string previous = Environment.GetEnvironmentVariable("SLOPD_ENDPOINT");

            public ClientEndpoint(ConnectionInfo connection)
            {
                Set(connection);
                Environment.SetEnvironmentVariable("SLOPD_ENDPOINT", descriptor);
            }

            public void Set(ConnectionInfo connection) =>
                File.WriteAllText(
                    descriptor,
                    $"url = \"{connection.BaseUrl}\"\ntoken = \"{connection.Token}\"\n"
                );

            public async Task<T> Send<T>(
                string method,
                string path,
                IMessage body,
                string session,
                int timeout
            )
                where T : IMessage<T>, new()
            {
                var completion = new TaskCompletionSource<T>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                Client.Send<T>(
                    method,
                    path,
                    body,
                    value => completion.SetResult(value),
                    error => completion.SetException(new IOException(error)),
                    session,
                    timeout
                );
                using var deadline = new CancellationTokenSource(10000);
                while (!completion.Task.IsCompleted)
                {
                    // Concurrent requests share the host's serialized completion pump.
                    lock (pumpGate)
                        Client.PumpCompletions(e => throw e);
                    if (!completion.Task.IsCompleted)
                        await Task.Delay(1, deadline.Token);
                }
                return await completion.Task;
            }

            public void Dispose()
            {
                Environment.SetEnvironmentVariable("SLOPD_ENDPOINT", previous);
                File.Delete(descriptor);
                Client.Dispose();
            }
        }

        sealed class Peer : IDisposable
        {
            readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            readonly CancellationTokenSource deadline = new CancellationTokenSource(10000);
            public readonly TaskCompletionSource<bool> Accepted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            public string Header;
            public byte[] Body;
            public ConnectionInfo Connection =>
                new ConnectionInfo(
                    "127.0.0.1",
                    ((IPEndPoint)listener.LocalEndpoint).Port,
                    "secret"
                );

            public Peer() => listener.Start();

            public async Task Serve(
                byte[] body,
                string status = "200 OK",
                string contentType = "application/x-protobuf",
                Task release = null,
                TaskCompletionSource<bool> accepted = null
            )
            {
                using var client = await listener.AcceptTcpClientAsync(deadline.Token);
                using var stream = client.GetStream();
                var header = new StringBuilder();
                var one = new byte[1];
                while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                {
                    Assert.That(await stream.ReadAsync(one, deadline.Token), Is.EqualTo(1));
                    header.Append((char)one[0]);
                }
                Header = header.ToString();
                int length = 0;
                foreach (string line in Header.Split('\n'))
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        length = int.Parse(
                            line.AsSpan("Content-Length:".Length),
                            System.Globalization.CultureInfo.InvariantCulture
                        );
                Body = new byte[length];
                await stream.ReadExactlyAsync(Body, deadline.Token);
                var response = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"
                );
                await stream.WriteAsync(response, deadline.Token);
                Accepted.TrySetResult(true);
                accepted?.TrySetResult(true);
                if (release != null)
                    await release.WaitAsync(deadline.Token);
                try
                {
                    await stream.WriteAsync(body, deadline.Token);
                }
                catch (IOException) when (release != null)
                { /* Deadline may have closed the client. */
                }
            }

            public void Dispose()
            {
                deadline.Cancel();
                listener.Stop();
                deadline.Dispose();
            }
        }
    }
}
