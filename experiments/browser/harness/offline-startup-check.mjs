// Assert the actual browser report, not merely successful WASM initialization.
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';

const report=JSON.parse(await readFile(process.argv[2],'utf8'));
assert.equal(report.completed,true);
assert.equal(report.result?.error,undefined);
const workspace=report.result.workspace;
assert.ok(workspace.visiblePixels>0,'Offline workspace has no visible UI');
assert.equal(workspace.state.game.programState,'Entry');
assert.equal(workspace.state.game.hasGame,false);
assert.equal(workspace.state.windowOpen,false,'Offline startup opened a terminal');
assert.equal(workspace.state.session,null);
assert.equal(workspace.state.underlying.chromeVisible,true,'Native chrome is hidden');
assert.equal(workspace.state.underlying.interactive,true,'Native chrome cannot receive input');
assert.ok(workspace.frames>=30,'Normal update pump did not continue after startup');
console.log('Offline browser startup: visible, interactive native chrome; no terminal or colony; update pump continued.');
