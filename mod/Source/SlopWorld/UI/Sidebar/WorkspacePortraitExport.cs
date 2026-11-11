using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using LudeonTK;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Explicit local appearance snapshot. Native pawn rendering owns the pixels;
    // this export never serializes pawns, saves, host session state or credentials.
    public static class WorkspacePortraitExport
    {
        static PortraitExportRequests requests;
        static float nextPoll;

        static string CacheDirectory =>
            Path.Combine(
                ApplicationPaths.CacheRoot,
                "workspace-portraits",
                Convert.ToHexStringLower(
                    SHA256.HashData(
                        Encoding.UTF8.GetBytes(Path.GetFullPath(GenFilePaths.SaveDataFolderPath))
                    )
                )
            );

        // Readback remains in the native GUI thread and only happens on demand.
        // Entry/WASM never advertises or consumes this mailbox.
        [HarmonyPatch(typeof(Root), nameof(Root.OnGUI))]
        static class RequestPump
        {
            static void Postfix()
            {
                if (
                    OperatingSystem.IsBrowser()
                    || Current.ProgramState != ProgramState.Playing
                    || LongEventHandler.AnyEventNowOrWaiting
                    || AgentColony.Current == null
                    || Event.current?.type != EventType.Repaint
                    || Time.realtimeSinceStartup < nextPoll
                )
                    return;
                nextPoll = Time.realtimeSinceStartup + 1f;
                try
                {
                    requests ??= new PortraitExportRequests(CacheDirectory, Environment.ProcessId);
                    requests.Poll(Capture);
                }
                catch (Exception e)
                {
                    // A private cache I/O failure must not take down native drawing.
                    nextPoll = Time.realtimeSinceStartup + 30f;
                    Log.Warning("[SlopWorld] Portrait export mailbox: " + e.Message);
                }
            }
        }

        [DebugAction(
            "SlopWorld",
            "Export workspace portraits",
            allowedGameStates = AllowedGameStates.Playing
        )]
        public static void Capture()
        {
            // A GameComponent exists before Scribe has populated its pawn map.
            // Never replace a valid snapshot with that transient empty inventory.
            if (
                OperatingSystem.IsBrowser()
                || Current.ProgramState != ProgramState.Playing
                || LongEventHandler.AnyEventNowOrWaiting
            )
                throw new InvalidOperationException(
                    "Portrait export requires a fully loaded native colony"
                );
            var colony = AgentColony.Current;
            if (colony == null)
                throw new InvalidOperationException(
                    "A native colony is required to export its portraits"
                );
            string root = CacheDirectory;
            Directory.CreateDirectory(root);
            var entries = new List<object>();
            long totalBytes = 0;
            // Export the colony's identity inventory, including folded/filtered
            // rows. Browser filters must not require a new native snapshot.
            foreach (var entry in colony.All.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                string session = entry.Key;
                var pawn = entry.Value;
                if (pawn == null)
                    continue;
                if (entries.Count >= 1024)
                    throw new InvalidOperationException(
                        "Portrait snapshot exceeds its bounded inventory"
                    );
                var source = Patch_SidebarPortraitDraw.PortraitFor(pawn) as RenderTexture;
                if (source == null)
                    throw new InvalidOperationException(
                        "Native portrait is unavailable for " + session
                    );
                byte[] pixels = Read(source);
                totalBytes += pixels.Length;
                if (totalBytes > 64 * 1024 * 1024)
                    throw new InvalidOperationException(
                        "Portrait snapshot exceeds its pixel bound"
                    );
                string file = Convert.ToHexStringLower(SHA256.HashData(pixels)) + ".rgba";
                string destination = Path.Combine(root, file);
                if (!File.Exists(destination))
                {
                    string pending = destination + ".partial";
                    File.WriteAllBytes(pending, pixels);
                    File.Move(pending, destination, true);
                }
                entries.Add(
                    new
                    {
                        session,
                        file,
                        width = source.width,
                        height = source.height,
                    }
                );
            }
            // Publish last so readers see either a complete previous snapshot or this one.
            string manifest = Path.Combine(root, "manifest.json");
            File.WriteAllText(
                manifest + ".partial",
                JsonSerializer.Serialize(
                    new
                    {
                        version = 1,
                        uiScale = Prefs.UIScale,
                        entries,
                    }
                )
            );
            File.Move(manifest + ".partial", manifest, true);
            Log.Message(
                "[SlopWorld] Exported " + entries.Count + " workspace portraits to " + root
            );
        }

        static byte[] Read(RenderTexture source)
        {
            if (
                source.width <= 0
                || source.height <= 0
                || source.width > 2048
                || source.height > 2048
            )
                throw new InvalidOperationException("Unsupported native portrait dimensions");
            var previous = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                RenderTexture.active = source;
#pragma warning disable CA2000 // Unity Object.Destroy in finally owns the native allocation.
                copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
#pragma warning restore CA2000
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                var colors = copy.GetPixels32();
                var bytes = new byte[checked(8 + source.width * source.height * 4)];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes, source.width);
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                    bytes.AsSpan(4),
                    source.height
                );
                for (int y = 0; y < source.height; y++)
                for (int x = 0; x < source.width; x++)
                {
                    var color = colors[(source.height - y - 1) * source.width + x];
                    int offset = 8 + (y * source.width + x) * 4;
                    bytes[offset] = color.r;
                    bytes[offset + 1] = color.g;
                    bytes[offset + 2] = color.b;
                    bytes[offset + 3] = color.a;
                }
                return bytes;
            }
            finally
            {
                RenderTexture.active = previous;
                if (copy != null)
                    UnityEngine.Object.Destroy(copy);
            }
        }
    }
}
