import {test} from 'node:test';
import assert from 'node:assert/strict';
import {renderingFlags} from './browser-rendering.mjs';
test('normal probes leave backend selection to Chrome; software rendering is explicit',()=>{
  assert.deepEqual(renderingFlags(),[]);
  assert.deepEqual(renderingFlags('swiftshader'),['--use-angle=swiftshader','--enable-unsafe-swiftshader']);
  assert.throws(()=>renderingFlags('--arbitrary-flag'),/PROBE_RENDERING/);
});
test('ANGLE GL is explicit and leaves driver safety and fallback policy to Chrome',()=>{
  assert.deepEqual(renderingFlags('angle-gl'),['--use-angle=gl']);
});
