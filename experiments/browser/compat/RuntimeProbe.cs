// Link this file and the repository's bundled 0Harmony.dll into a browser runner.
// This does not load the game, contact slopd, or require a window.
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;

namespace SlopWorld.BrowserCompat;

public static class RuntimeProbe
{
    public static string Run() => JsonSerializer.Serialize(Capture());

    public static async Task<string> RunAsync(string readOnlyUrl)
    {
        var results = Capture();
        await HttpProbe(results, "socketsHttpSend", readOnlyUrl, true);
        await HttpProbe(results, "browserHttpSendControl", readOnlyUrl, false);
        return JsonSerializer.Serialize(results);
    }

    static async Task HttpProbe(
        Dictionary<string, object> results,
        string name,
        string url,
        bool sockets
    )
    {
        try
        {
            // The caller selects a read-only static page on the same local harness.
#pragma warning disable CA1416 // Deliberate comparison with browser-backed default handler.
            using var handler = sockets ? new SocketsHttpHandler() : null;
            using var client =
                handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
#pragma warning restore CA1416
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await client.GetAsync(url, deadline.Token);
            var data = await response.Content.ReadAsByteArrayAsync(deadline.Token);
            results[name] = new
            {
                ok = response.IsSuccessStatusCode,
                status = (int)response.StatusCode,
                bytes = data.Length,
            };
        }
        catch (Exception e)
        {
            results[name] = new { ok = false, exception = e.ToString() };
        }
    }

    static Dictionary<string, object> Capture()
    {
        var results = new Dictionary<string, object>
        {
            ["framework"] = RuntimeInformation.FrameworkDescription,
            ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["browser"] = OperatingSystem.IsBrowser(),
            ["dynamicCodeSupported"] = RuntimeFeature.IsDynamicCodeSupported,
            ["dynamicCodeCompiled"] = RuntimeFeature.IsDynamicCodeCompiled,
        };
        Probe(
            results,
            "dynamicMethod",
            () =>
            {
                var method = new DynamicMethod("CompatConstant", typeof(int), Type.EmptyTypes);
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldc_I4, 42);
                il.Emit(OpCodes.Ret);
                return ((Func<int>)method.CreateDelegate(typeof(Func<int>)))();
            }
        );
        Probe(results, "harmony", Patch);
        // These intentionally exercise browser-unsupported APIs to capture actual failures.
#pragma warning disable CA1416
        Probe(
            results,
            "tcpClient",
            () =>
            {
                using var tcp = new TcpClient();
                return "constructed";
            }
        );
        Probe(
            results,
            "socketsHttpHandler",
            () =>
            {
                using var handler = new SocketsHttpHandler();
                return "constructed";
            }
        );
        Probe(
            results,
            "socketsHttpModOptions",
            () =>
            {
                using var handler = new SocketsHttpHandler();
                handler.UseProxy = false;
                handler.MaxConnectionsPerServer = 8;
                return "configured";
            }
        );
        // Do not Join: blocking the browser main thread can deadlock worker startup.
        Probe(
            results,
            "threadStart",
            () =>
            {
                var thread = new Thread(() => { }) { IsBackground = true };
                thread.Start();
                return "Start returned; completion not observed";
            }
        );
#pragma warning restore CA1416
        return results;
    }

    static void Probe(Dictionary<string, object> results, string name, Func<object> action)
    {
        try
        {
            results[name] = new { ok = true, value = action() };
        }
        catch (Exception e)
        {
            results[name] = new { ok = false, exception = e.ToString() };
        }
    }

    static object Patch()
    {
        var harmony = new Harmony("slopworld.browser.compat.probe");
        try
        {
            int before = Target(2);
            harmony.Patch(
                AccessTools.Method(typeof(RuntimeProbe), nameof(Target)),
                postfix: new HarmonyMethod(typeof(RuntimeProbe), nameof(Postfix))
            );
            int patched = Target(2);
            harmony.UnpatchAll(harmony.Id);
            int restored = Target(2);
            if (before != 3 || patched != 6 || restored != 3)
                throw new InvalidOperationException(
                    $"Unexpected patch behavior: {before}, {patched}, {restored}"
                );
            return new
            {
                before,
                patched,
                restored,
            };
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Target(int value) => value + 1;

    static void Postfix(ref int __result) => __result *= 2;
}
