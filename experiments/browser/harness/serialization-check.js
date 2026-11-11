(async () => {
  const managed = window.__managedProbe;
  if (window.__workspace?.status !== 'running') throw new Error('Workspace is not running');
  managed.RequestSerializationBenchmark();
  const until = performance.now() + 30000;
  while (performance.now() < until) {
    await new Promise(resolve => setTimeout(resolve, 100));
    const result = JSON.parse(managed.SerializationBenchmark());
    if (!result) continue;
    const mean = generated => {
      const samples = result.samples.filter(s => s.generated === generated);
      return { ms: samples.reduce((sum,s) => sum+s.ms,0)/samples.length,
        allocatedBytes: samples.reduce((sum,s) => sum+s.allocatedBytes,0)/samples.length };
    };
    const numericSamples = result.numericSamples ?? [];
    const numeric = numericSamples.length ? {
      ms:numericSamples.reduce((sum,s) => sum+s.ms,0)/numericSamples.length,
      allocatedBytes:numericSamples.reduce((sum,s) => sum+s.allocatedBytes,0)/numericSamples.length,
    } : null;
    const directSamples = result.directSamples ?? [];
    const direct = directSamples.length ? {
      ms:directSamples.reduce((sum,s) => sum+s.ms,0)/directSamples.length,
      allocatedBytes:directSamples.reduce((sum,s) => sum+s.allocatedBytes,0)/directSamples.length,
    } : null;
    return {...result, reference:mean(false), generated:mean(true), numeric, direct,
      generatedToDirectRatio:direct ? mean(true).ms / direct.ms : null,
      numericToPackedRatio:numeric ? numeric.ms / mean(true).ms : null};
  }
  throw new Error('Serialization benchmark has not completed; inspect the live runtime');
})()
