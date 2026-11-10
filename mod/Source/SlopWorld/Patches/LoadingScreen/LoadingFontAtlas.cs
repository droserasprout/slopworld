using System;
using System.IO;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The loading renderer owns this process-lifetime texture, independently of RimWorld's
    // mod content holders. Decode only from drawing on the game thread, never ModEntry's
    // loader thread. The early and ordinary screens share the same texture and stream.
    internal static class LoadingFontAtlas
    {
        static Texture2D atlas;
        static bool attempted;

        internal static Texture2D Get()
        {
            if (attempted)
                return atlas;
            attempted = true;
            Texture2D pending = null;
            try
            {
                using var source = typeof(LoadingFontAtlas).Assembly.GetManifestResourceStream(
                    "SlopWorld.LoadingFont"
                );
                if (source == null)
                    throw new InvalidDataException("Missing embedded loading font.");
                using var bytes = new MemoryStream();
                source.CopyTo(bytes);
#pragma warning disable CA2000 // Ownership transfers to atlas on success; failure destroys the pending texture.
                pending = new Texture2D(2, 2, TextureFormat.RGBA32, false);
#pragma warning restore CA2000
                if (!pending.LoadImage(bytes.ToArray(), true))
                    throw new InvalidDataException("Invalid embedded loading font.");
                pending.name = "SlopWorld loading font";
                atlas = pending;
                pending = null;
                Log.Message("[SlopWorld] loading font ready from embedded atlas");
            }
            catch (Exception e)
            {
                // Keep the static background usable if an asset or native decoder fails.
                Log.Warning("[SlopWorld] loading font unavailable: " + e.Message);
            }
            finally
            {
                if (pending != null)
                    UnityEngine.Object.Destroy(pending);
            }
            return atlas;
        }
    }
}
