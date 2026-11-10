// The installed Slang emits this compute shader locally. Remap only its push
// constant resource; validate the actual module and dispatch in the browser.
window.__probeResult = { status: 'running' };
let device;
const buffers = [];
try {
  if (!navigator.gpu) throw new Error('WebGPU API unavailable');
  const adapter = await navigator.gpu.requestAdapter({ forceFallbackAdapter: true });
  if (!adapter) throw new Error('No WebGPU fallback adapter');
  device = await adapter.requestDevice();
  const response = await fetch('./GpuProbe.comp.hlsl.wgsl');
  if (!response.ok) throw new Error(`Shader HTTP ${response.status}`);
  const original = await response.text();
  const pattern = 'var<uniform> pc_0 : PushConstants_std430_0;';
  if (original.split(pattern).length !== 2) throw new Error('Installed shader push constant contract changed');
  const code = original.replace(pattern, '@group(0) @binding(2) ' + pattern);
  const module = device.createShaderModule({ code });
  const messages = [...(await module.getCompilationInfo()).messages].map(m => ({ type: m.type, message: m.message }));
  if (messages.some(m => m.type === 'error')) throw new Error(JSON.stringify(messages));
  device.pushErrorScope('validation');
  const pipeline = await device.createComputePipelineAsync({ layout: 'auto', compute: { module, entryPoint: 'BandwidthProbeCS' } });
  const buffer = (size, usage) => {
    const result = device.createBuffer({ size, usage }); buffers.push(result); return result;
  };
  const input = buffer(128 * 4, GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST);
  const output = buffer(64 * 4, GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC);
  const uniforms = buffer(16, GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST);
  const readback = buffer(64 * 4, GPUBufferUsage.MAP_READ | GPUBufferUsage.COPY_DST);
  device.queue.writeBuffer(input, 0, Float32Array.from({ length: 128 }, (_, i) => i));
  device.queue.writeBuffer(uniforms, 0, new Uint32Array([1, 128, 64, 0]));
  const bindings = device.createBindGroup({ layout: pipeline.getBindGroupLayout(0), entries: [
    { binding: 0, resource: { buffer: output } }, { binding: 1, resource: { buffer: input } },
    { binding: 2, resource: { buffer: uniforms } },
  ] });
  const encoder = device.createCommandEncoder();
  const pass = encoder.beginComputePass();
  pass.setPipeline(pipeline); pass.setBindGroup(0, bindings); pass.dispatchWorkgroups(1); pass.end();
  encoder.copyBufferToBuffer(output, 0, readback, 0, 64 * 4);
  device.queue.submit([encoder.finish()]);
  await readback.mapAsync(GPUMapMode.READ);
  const actual = Array.from(new Float32Array(readback.getMappedRange()));
  readback.unmap();
  const validationError = await device.popErrorScope();
  if (validationError) throw new Error(validationError.message);
  const expected = Array.from({ length: 64 }, (_, i) => i + (i + 64));
  if (actual.some((value, i) => value !== expected[i])) throw new Error(`Readback mismatch: ${actual}`);
  window.__probeResult = { status: 'pass', source: 'Installed GpuProbe.comp.hlsl / BandwidthProbeCS',
    adaptation: 'Push constants remapped to group 0 binding 2 uniform buffer', messages,
    adapter: { vendor: adapter.info.vendor, architecture: adapter.info.architecture, description: adapter.info.description },
    actual, expected, scope: 'One compute shader; not a Waylith WebGPU backend' };
} catch (error) {
  window.__probeResult = { status: 'fail', error: String(error), stack: error.stack };
} finally {
  for (const buffer of buffers) buffer.destroy();
  device?.destroy();
  document.querySelector('#result').textContent = JSON.stringify(window.__probeResult, null, 2);
}
