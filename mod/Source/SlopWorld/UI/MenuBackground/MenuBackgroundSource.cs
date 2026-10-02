using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using UnityEngine;

namespace SlopWorld
{
    // Content-pack source textures are immutable. Cache their pixel identity without rooting
    // them across unloads; Bake owns the disk key, readback owns temporary texture cleanup.
    internal static class MenuBackgroundSource
    {
        sealed class Fingerprint
        {
            public readonly string Hash;
            public Fingerprint(string hash) { Hash = hash; }
        }

        static readonly ConditionalWeakTable<Texture2D, Fingerprint> Identities =
            new ConditionalWeakTable<Texture2D, Fingerprint>();

        internal static string Identity(Texture2D source) =>
            Identities.GetValue(source, ReadIdentity).Hash;

        static Fingerprint ReadIdentity(Texture2D source)
        {
            Color[] pixels = TextureReadback.ReadBack(source);
            // Feed bounded RGBA8 blocks rather than allocating another full-size pixel array.
            var block = new byte[4096];
            int used = 0;
            using (var hash = SHA256.Create())
            {
                foreach (Color pixel in pixels)
                {
                    Color32 rgba = pixel;
                    block[used++] = rgba.r;
                    block[used++] = rgba.g;
                    block[used++] = rgba.b;
                    block[used++] = rgba.a;
                    if (used != block.Length) continue;
                    hash.TransformBlock(block, 0, used, block, 0);
                    used = 0;
                }
                hash.TransformFinalBlock(block, 0, used);
                return new Fingerprint(BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant());
            }
        }
    }
}
