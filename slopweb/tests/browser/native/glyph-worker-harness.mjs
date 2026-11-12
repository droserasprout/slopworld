// Exercise the authored worker with Node's message transport, without a browser.
import {parentPort} from 'node:worker_threads';
globalThis.self=globalThis;
self.postMessage=value=>parentPort.postMessage(value);
await import('../../../runtime/wwwroot/glyph-worker.js');
parentPort.on('message',data=>self.onmessage({data}));
