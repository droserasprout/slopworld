// Compile the production DaemonClient on Mono. Stubs isolate transport scheduling
// and callback dispatch from Unity and Protobuf; wire correctness has separate tests.
using System;
using System.Text;
using System.Threading;
using System.Diagnostics;
using SlopWorld;

namespace Google.Protobuf
{
    public interface IMessage { byte[] ToByteArray(); }
    public interface IMessage<T> : IMessage { }
    public interface IData { byte[] Data { get; set; } }
    public class MessageParser<T> where T : IMessage<T>
    {
        readonly Func<T> _make;
        public MessageParser(Func<T> make) { _make = make; }
        public T ParseFrom(byte[] bytes) { var value = _make(); ((IData)value).Data = bytes; return value; }
    }
}
namespace Verse { public static class Log { public static void Error(string text) { throw new Exception(text); } } }
namespace SlopWorld
{
    public static class Settings { public static Connection Connection = new Connection(); }
    public class Connection { public string BaseUrl, Token = ""; }
    public static class PerfTrace
    {
        public static long Start() => 0;
        public static void End(string name, long start, int count, int backlog = 0) { }
    }
    public static class WireProtocol
    {
        public const string TokenHeader = "x-slop-token", SessionHeader = "x-slop-session";
        public static class Routes { public const string Browse = "/browse", Git = "/git", Search = "/search"; }
    }
}
namespace SlopWorld.Wire
{
    public class Result<T> : Google.Protobuf.IMessage<T>, Google.Protobuf.IData
    {
        public byte[] Data { get; set; } = new byte[0];
        public byte[] ToByteArray() => Data;
    }
    public class Empty : Result<Empty> { }
    public class Ack : Result<Ack> { }
    public class Error : Result<Error>
    {
        public static Google.Protobuf.MessageParser<Error> Parser = new Google.Protobuf.MessageParser<Error>(() => new Error());
        public string Error_ => Encoding.UTF8.GetString(Data);
    }
}
class HttpTransportProbe
{
    static int done, failed;
    static readonly int MainThread = Thread.CurrentThread.ManagedThreadId;
    static void Complete(bool success)
    {
        if (Thread.CurrentThread.ManagedThreadId != MainThread) throw new Exception("Callback left main-thread pump");
        done++;
        if (!success) failed++;
    }
    static void Wait(int expected, int timeout = 6000)
    {
        var watch = Stopwatch.StartNew();
        while (done < expected && watch.ElapsedMilliseconds < timeout)
        {
            DaemonClient.PumpCompletions();
            Thread.Sleep(5);
        }
        if (done != expected || failed != 0) throw new Exception("done=" + done + " expected=" + expected + " failed=" + failed);
        Console.WriteLine("completed=" + done + " elapsed_ms=" + watch.ElapsedMilliseconds);
    }
    static void ExpectTimeout(string path, int deadlineMs)
    {
        var watch = Stopwatch.StartNew();
        DaemonClient.Get<SlopWorld.Wire.Ack>(path, r => Complete(false),
            e => Complete(watch.ElapsedMilliseconds >= deadlineMs / 2 &&
                watch.ElapsedMilliseconds <= deadlineMs + Math.Max(200, deadlineMs / 2)), timeoutMs: deadlineMs);
    }
    static int Main(string[] args)
    {
        try
        {
            Settings.Connection.BaseUrl = args[0];
            bool rejected = false;
            try { DaemonClient.Get<SlopWorld.Wire.Ack>("/ok", r => Complete(false), timeoutMs: -2); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            if (!rejected) throw new Exception("Invalid timeout was not rejected synchronously");
            ThreadPool.SetMinThreads(4, 4);
            if (!ThreadPool.SetMaxThreads(16, 16)) throw new Exception("Cannot bound Mono worker pool");
            for (int i = 0; i < 64; i++)
                DaemonClient.Get<SlopWorld.Wire.Ack>("/ok", r => Complete(Encoding.UTF8.GetString(r.Data) == "response"), e => Complete(false));
            Wait(64);
            DaemonClient.Post<SlopWorld.Wire.Ack>("/upload", new SlopWorld.Wire.Ack { Data = new byte[131072] },
                r => Complete(Encoding.UTF8.GetString(r.Data) == "131072"), e => Complete(false));
            Wait(65);
            DaemonClient.Get<SlopWorld.Wire.Ack>("/error", r => Complete(false), e => Complete(e == "test error"));
            Wait(66);
            DaemonClient.Get<SlopWorld.Wire.Ack>("/wrong-type", r => Complete(false), e => Complete(e.Contains("Protobuf")));
            Wait(67);
            DaemonClient.Get<SlopWorld.Wire.Ack>("/oversize", r => Complete(false), e => Complete(e.Contains("exceeds limit")));
            Wait(68);
            ExpectTimeout("/slow-body", 100);
            Wait(69, 2000);
            ExpectTimeout("/slow-error", 100);
            Wait(70, 2000);
            // Saturate transport slots, then expire a queued request before admission.
            for (int i = 0; i < 8; i++)
                ExpectTimeout("/slow-body", 1000);
            Thread.Sleep(100);
            ExpectTimeout("/ok", 50);
            Wait(79, 2000);
            // Match the benchmark's highest typing rate, and check recovery after aborts.
            var paced = Stopwatch.StartNew();
            for (int i = 0; i < 100; i++)
            {
                DaemonClient.Get<SlopWorld.Wire.Ack>("/ok", r => Complete(Encoding.UTF8.GetString(r.Data) == "response"), e => Complete(false));
                DaemonClient.PumpCompletions();
                int delay = (i + 1) * 10 - (int)paced.ElapsedMilliseconds;
                if (delay > 0) Thread.Sleep(delay);
            }
            Wait(179);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
