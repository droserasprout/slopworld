// Decode immutable RGBA uploads; Canvas owns pixel conversion and tinting.
export function decodeGuiPixels(value) {
  if (typeof Uint8Array.fromBase64 === 'function') {
    const bytes = Uint8Array.fromBase64(value);
    // ImageData needs clamped bytes; retain exactly the returned view, without a copy.
    return new Uint8ClampedArray(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  }
  const binary = atob(value), pixels = new Uint8ClampedArray(binary.length);
  for (let i = 0; i < binary.length; i++) pixels[i] = binary.charCodeAt(i);
  return pixels;
}
