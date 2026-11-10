using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using Verse;
using Verse.Sound;
using Waylith.Assets;
using Waylith.Audio.MiniAudio;

namespace SlopWorld
{
    // Only metadata is persisted. Native sound objects and grain resolution stay game-owned,
    // on the main thread, using the original callbacks and their original lazy asset holders.
    internal static class StartupAudio
    {
        static readonly DeferredStartupActions<SubSoundDef> Sounds = new();
        static readonly string RuntimeIdentity =
            typeof(MiniAudioBackend).Module.ModuleVersionId.ToString("N");
        static AudioSizeCache cache;
        static int flushing;
        static bool warned;
        static int mainThread;
        static long nextFlush;

        internal static void Install(Harmony harmony)
        {
            cache = new AudioSizeCache(
                Path.Combine(ApplicationPaths.CacheRoot, "startup", "audio-sizes-v1.json"),
                ReportCacheError
            );
            harmony.Patch(
                SizeTarget(),
                prefix: Hook(nameof(SizePrefix)),
                postfix: Hook(nameof(SizePostfix))
            );

            // Validate the scheduling rewrite before installing any lazy consumers. A game
            // update must fail this feature closed, not leave partially resolved sound defs.
            var resolve = ResolveTarget();
            Rewrite(PatchProcessor.GetOriginalInstructions(resolve));
            foreach (MethodBase target in ConsumerTargets())
                harmony.Patch(target, prefix: Hook(nameof(EnsureSound)));
            harmony.Patch(resolve, transpiler: Hook(nameof(Rewrite)));
        }

        static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(StartupAudio), name);

        internal static MethodBase SizeTarget() =>
            AccessTools.Method(
                typeof(MiniAudioBackend),
                nameof(MiniAudioBackend.TryGetDecodedSize)
            );

        internal static MethodBase ResolveTarget() =>
            AccessTools.Method(typeof(SubSoundDef), nameof(SubSoundDef.ResolveReferences));

        internal static IEnumerable<MethodBase> ConsumerTargets()
        {
            yield return AccessTools.PropertyGetter(
                typeof(SubSoundDef),
                nameof(SubSoundDef.Duration)
            );
            yield return AccessTools.Method(typeof(SubSoundDef), nameof(SubSoundDef.TryPlay));
            yield return AccessTools.Method(
                typeof(SubSoundDef),
                nameof(SubSoundDef.RandomizedResolvedGrain)
            );
            yield return AccessTools.Method(
                typeof(SubSoundDef),
                nameof(SubSoundDef.Notify_GrainPlayed)
            );
        }

        internal static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>();
            var original = AccessTools.Method(
                typeof(LongEventHandler),
                nameof(LongEventHandler.ExecuteWhenFinished)
            );
            var replacement = AccessTools.Method(typeof(StartupAudio), nameof(Defer));
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(original))
                {
                    // The original callback is already on the stack. Keep it intact and add
                    // its owner, so all reads can demand resolution before observing grains.
                    var owner = new CodeInstruction(OpCodes.Ldarg_0);
                    owner.MoveLabelsFrom(instruction);
                    owner.MoveBlocksFrom(instruction);
                    result.Add(owner);
                    result.Add(new CodeInstruction(OpCodes.Call, replacement));
                    count++;
                }
                else
                    result.Add(instruction);
            }
            if (count != 1)
                throw new InvalidOperationException(
                    "Sound resolution scheduling changed; startup deferral disabled."
                );
            return result;
        }

        static void Defer(Action action, SubSoundDef sound) =>
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                mainThread = Environment.CurrentManagedThreadId;
                Sounds.Schedule(sound, action);
            });

        static void EnsureSound(SubSoundDef __instance)
        {
            // Vanilla resolves these callbacks on the game thread. Do not turn a metadata
            // query on a loader thread into native audio work.
            if (Environment.CurrentManagedThreadId == mainThread)
                Sounds.Ensure(__instance);
        }

        internal static void Update()
        {
            mainThread = Environment.CurrentManagedThreadId;
            if (!EarlyBootstrap.Ready || LongEventHandler.AnyEventNowOrWaiting)
                return;
            try
            {
                // One definition per frame, never a whole folder/batch on a worker thread.
                // First use is still synchronous and cannot observe an unresolved definition.
                Sounds.WarmOne();
            }
            catch (Exception e)
            {
                Log.Error("[SlopWorld] deferred sound initialization: " + e);
            }
            if (
                Environment.TickCount64 < nextFlush
                || Interlocked.CompareExchange(ref flushing, 1, 0) != 0
            )
                return;
            nextFlush = Environment.TickCount64 + 5000;
            _ = Task.Run(() =>
            {
                try
                {
                    cache?.Flush();
                }
                finally
                {
                    Volatile.Write(ref flushing, 0);
                }
            });
        }

        internal static void Flush() => cache?.Flush();

        static bool SizePrefix(
            MiniAudioBackend __instance,
            string path,
            ref long pcm16Bytes,
            ref bool __result,
            out string __state
        )
        {
            __state = null;
            if (!__instance.IsInitialized || cache == null)
                return true;
            string key = SourceIdentity(path);
            if (cache.TryGet(key, out long size))
            {
                pcm16Bytes = size;
                __result = true;
                return false;
            }
            __state = key;
            return true;
        }

        static void SizePostfix(string path, bool __result, long pcm16Bytes, string __state)
        {
            // Do not publish a result under an identity that changed during native I/O.
            if (__result && __state != null && SourceIdentity(path) == __state)
                cache?.Record(__state, pcm16Bytes);
        }

        static string SourceIdentity(string path)
        {
            try
            {
                // Match the backend's packed-before-loose lookup. The pack API checks source
                // freshness; its content hash also covers repacking and changed game assets.
                if (ContentPack.TryGetCurrentMount(path, "", out var mount, out string name))
                    return mount.Pack.ContentHashes.TryGetValue(name, out string hash)
                        ? RuntimeIdentity + ":pack:" + hash
                        : null;
                var file = new FileInfo(path);
                return file.Exists
                    ? RuntimeIdentity
                        + ":file:"
                        + file.FullName
                        + ":"
                        + file.Length
                        + ":"
                        + file.LastWriteTimeUtc.Ticks
                    : null;
            }
            catch (Exception e)
                when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return null;
            }
        }

        static void ReportCacheError(Exception e)
        {
            if (warned)
                return;
            warned = true;
            Log.Warning(
                "[SlopWorld] audio metadata cache unavailable; using native probes: " + e.Message
            );
        }
    }
}
