using BrowserAdapters;
using UnityEngine;

void Check(bool value, string message)
{
    if (!value)
        throw new Exception(message);
}

// Synthetic 1x2 PNG: red above half-transparent blue. No installed image is read.
var bytes = Convert.FromBase64String(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAACCAYAAACZgbYnAAAAEklEQVR4nGP4z8Dwn4GB4X8DABF5A36/Zxn6AAAAAElFTkSuQmCC"
);
var texture = new Texture2D();
Check(BrowserImages.Load(texture, bytes), "PNG decodes");
Check(texture.Width == 1 && texture.Height == 2, "decoded dimensions replace placeholder");
Check(
    texture.Pixels.SequenceEqual(
        new[] { new Color32(0, 0, 255, 128), new Color32(255, 0, 0, 255) }
    ),
    "bottom-up pixels preserve color and alpha"
);
Check(
    texture.Readable && texture.Updates == 1,
    "CPU pixels remain available and upload is invalidated"
);
Check(
    !BrowserImages.Load(new Texture2D { FailResize = true }, bytes),
    "failed allocation rejects decode"
);
Check(!BrowserImages.Load(new Texture2D(), new byte[] { 0, 1, 2 }), "invalid header is rejected");
Check(!BrowserImages.Load(new Texture2D(), bytes[..41]), "truncated PNG payload is rejected");
Console.WriteLine("Browser image contracts passed");
