import {test} from 'node:test';
import assert from 'node:assert/strict';
import {collectWorkspaceDiagnostics} from '../../runtime/wwwroot/workspace-diagnostics.js';

test('ordinary frames never inspect private managed state', () => {
  let reads=0;
  const managed={WorkspaceState(){reads++;throw Error('must not run');}};
  collectWorkspaceDiagnostics(managed, {frames:0}, false);
  collectWorkspaceDiagnostics(managed, {frames:1}, true);
  assert.equal(reads,0);
});
test('diagnostic failure preserves presentation and can recover', () => {
  const state={frames:60,status:'running'};
  collectWorkspaceDiagnostics({WorkspaceState(){throw Error('renamed field');}},state,true);
  assert.equal(state.status,'running');
  assert.match(state.diagnosticError,/renamed field/);
  let reads=0;
  collectWorkspaceDiagnostics({WorkspaceState(){reads++;return '{"game":{"hasGame":false}}';}},state,true);
  assert.equal(reads,1);
  assert.equal(state.game.hasGame,false);
  assert.equal(state.diagnosticError,undefined);
});
