(async () => {
  const {tintPixels} = await import('./gui-tint.js');
  const source = Uint8ClampedArray.from({length: 512 * 512 * 4}, (_, i) => (i * 73 + (i >> 2)) % 256);
  const color = [.72, .48, .91];
  const reference = () => {
    const pixels = source.slice();
    for (let i = 0; i < pixels.length; i++) if (i % 4 !== 3) pixels[i] *= color[i % 4];
    return pixels;
  };
  const optimized = () => tintPixels(source, color);
  const expected = reference(), actual = optimized();
  if (actual.some((value, index) => value !== expected[index])) throw new Error('Tint bytes differ');
  const samples = [];
  let checksum = 0;
  for (let round = 0; round < 6; round++) {
    for (const name of round % 2 ? ['optimized', 'reference'] : ['reference', 'optimized']) {
      const run = name === 'reference' ? reference : optimized;
      const start = performance.now();
      for (let i = 0; i < 20; i++) checksum += run()[i * 4];
      samples.push({name, ms: (performance.now() - start) / 20});
    }
  }
  return {width: 512, height: 512, byteIdentical: true, samples, checksum,
    scope: 'Pixel tint loop only; excludes readback, Canvas upload and workspace rendering'};
})()
