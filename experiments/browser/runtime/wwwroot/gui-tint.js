// Preserve Uint8ClampedArray rounding and alpha exactly while avoiding per-channel
// modulo/branch work. Source bytes remain immutable across cached tint variants.
export function tintPixels(source, color) {
  const pixels = new Uint8ClampedArray(source.length);
  const [red, green, blue] = color;
  for (let i = 0; i < source.length; i += 4) {
    pixels[i] = source[i] * red;
    pixels[i + 1] = source[i + 1] * green;
    pixels[i + 2] = source[i + 2] * blue;
    pixels[i + 3] = source[i + 3];
  }
  return pixels;
}
