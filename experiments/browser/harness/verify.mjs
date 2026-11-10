// Separate a successful diagnostic reproduction from successful game startup.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const [file, mode = 'proof', priorFile] = process.argv.slice(2);
const report = JSON.parse(await readFile(file, 'utf8'));
assert.equal(report.completed, true, 'Browser did not complete the probe');
const result = report.result;
assert.notEqual(result.status, 'fail', result.error);
if (mode === 'workspace') {
  assert.equal(result.error, undefined);
  assert.equal(result.loadErrors.length, 0);
  assert.equal(result.entry.loaded, true);
  assert.equal(result.entry.activeScene, 'Entry');
  assert.ok(result.eco.checks.every(check => check.ok));
  assert.ok(result.workspace.commands > 0);
  assert.ok(result.workspace.visiblePixels > 100);
  assert.equal(result.workspace.frames, 30);
  assert.ok(result.workspace.textures > 0);
  assert.equal(result.workspace.state.windowOpen, true);
  const game = result.workspace.game;
  assert.equal(game.scene, 'Entry');
  assert.equal(game.programState, 'Entry');
  assert.equal(game.hasGame, false);
  assert.equal(game.hasWorld, false);
  assert.equal(game.maps, null);
  assert.equal(game.ticks, 0);
  assert.equal(result.gameSession, null);
  console.log('Verified actual workspace drawing from Entry with no game, world, map, or simulation ticks. Agent interaction remains a separate check.');
  process.exit(0);
}
const managed = result.managed ?? result;
assert.equal(managed.browser, true);
assert.equal(managed.architecture, 'Wasm');
if (mode === 'compat') {
  assert.equal(result.dynamicMethod.value, 42);
  assert.equal(result.modAssemblyLoad.ok, true);
  assert.match(result.modAssemblyLoad.value, /^SlopWorld,/);
  assert.equal(result.browserHttpSendControl.ok, true);
  for (const name of ['harmony', 'tcpClient', 'socketsHttpModOptions', 'socketsHttpSend']) {
    assert.equal(result[name].ok, false, name);
    assert.match(result[name].exception, /PlatformNotSupportedException/, name);
  }
  assert.match(result.harmony.exception, /OS kind Unknown not supported/);
  console.log('Verified mod assembly load, working browser HTTP control, and reproduced Harmony/socket failures.');
  process.exit(0);
}
const checks = new Map(managed.checks.map(check => [check.name, check]));
for (const [name, value] of [['game.positive-mod', '3'], ['game.cell-length', '25'],
  ['unity.math', '9'], ['runtime.dynamic-method', '42']]) {
  assert.equal(checks.get(name)?.ok, true, name);
  assert.equal(checks.get(name)?.value, value, name);
}
assert.equal(result.loadErrors?.length, 0, 'An installed dependency failed to load');
assert.match(checks.get('game.version')?.value ?? '', /^1\.7\./);
if (mode === 'mod-http') {
  assert.equal(result.modSocket.connected, true);
  assert.equal(result.modSocket.disconnected, true);
  assert.equal(result.modSocket.receivedSessions, true);
  assert.ok(result.modSocket.messages > 0);
  assert.equal(result.modHub.online, true);
  assert.ok(result.modHub.sessions.length >= 1);
  for (const screen of result.modHub.sessions) {
    assert.ok(screen.cols > 0 && screen.rows > 0 && screen.nonemptyLines > 0);
  }
  assert.equal(result.modHttp.ok, true);
  assert.equal(result.modHttp.protocolVersion, 2);
  console.log('Verified actual mod HTTP request, protobuf parsing, and pumped completion through the host bridge.');
  process.exit(0);
}
if (mode === 'gui') {
  assert.deepEqual(result.gui.targets, { retained: true, recreated: true, released: true });
  assert.equal(result.gui.input.clicks, 1);
  assert.equal(result.gui.input.text, 'agen');
  assert.equal(result.gui.input.syntheticDomEvents, true);
  assert.ok(result.gui.commands >= 3);
  assert.ok(result.gui.visiblePixels > 100);
  assert.equal(result.gui.source, 'UnityEngine.GUI.Label/Box → IGuiBackend → Canvas2D');
  console.log('Verified installed Unity controls drawing through the browser GUI backend; workspace integration remains incomplete.');
  process.exit(0);
}
if (mode === 'entry') {
  const entry = result.entry;
  assert.ok(entry.checks.every(check => check.ok), JSON.stringify(entry.checks));
  for (const name of ['entry.serial-crossref-work', 'entry.reject-mod-assemblies', 'entry.cache-write', 'entry.bootstrap'])
    assert.ok(entry.checks.some(check => check.name === name && check.ok), name);
  assert.equal(entry.activeScene, 'Entry');
  assert.equal(entry.loaded, true);
  assert.equal(entry.humanBody, 'Human');
  assert.ok(entry.definitions['Verse.ThingDef'] > 1000);
  assert.ok(entry.definitions['Verse.TerrainDef'] > 100);
  assert.ok(entry.definitions['Verse.RecipeDef'] > 100);
  assert.equal(entry.unityFrame, 5);
  assert.equal(entry.world.PendingStartCount, 0);
  assert.equal(entry.omittedErrorCount, 0, 'Distinct startup errors were lost');
  assert.ok(entry.engineErrors.some(error => error.includes('Browser audio decoding is not implemented')));
  assert.ok(entry.engineErrors.some(error => error.includes('MethodBodyTransformer')));
  assert.equal(entry.customClrInjection, false);
  assert.equal(entry.rendered, false);
  assert.equal(entry.colony, false);
  console.log('Verified real Entry/Core loading, resolved Human body, serial failure recovery, and explicit remaining startup errors. No colony rendered.');
  process.exit(0);
}
if (['play', 'load', 'terrain', 'objects'].includes(mode)) {
  const frames = result.gameSession;
  assert.ok(result.entry.checks.every(check => check.ok), JSON.stringify(result.entry.checks.filter(check => !check.ok)));
  assert.ok(Array.isArray(frames) && frames.length > 1, 'No Play snapshots');
  const playing = frames.filter(frame => frame.programState === 'Playing' && frame.maps?.length);
  assert.ok(playing.length > 1, 'A colony did not reach Playing state');
  const first = playing[0], last = playing.at(-1);
  assert.equal(last.hasWorld, true);
  assert.ok(last.worldTiles > 100000, 'Default quick-test world was not generated');
  assert.ok(last.ticks > first.ticks, 'Simulation did not advance');
  assert.equal(last.forcePaused, false);
  assert.equal(last.maps[0].sizeX, 250);
  assert.equal(last.maps[0].sizeZ, 250);
  assert.ok(last.maps[0].colonists >= 3, 'Crashlanded colonists did not arrive');
  assert.ok(last.maps[0].things > last.maps[0].colonists);
  assert.ok(last.maps[0].centerTerrain);
  assert.ok(last.maps[0].pawns.some(pawn => pawn.job), 'No colonist job observed');
  assert.ok(last.maps[0].pawns.some(pawn => {
    const before = first.maps[0].pawns.find(other => other.id === pawn.id);
    return before && (before.x !== pawn.x || before.z !== pawn.z);
  }), 'No colonist movement observed');
  assert.equal(last.rendered, false);
  if (priorFile) {
    const prior = JSON.parse(await readFile(priorFile, 'utf8')).result.gameSession.at(-1);
    assert.equal(first.seed, prior.seed, 'Saved world identity changed');
    assert.equal(first.ticks, prior.ticks, 'Saved tick count was not restored');
    assert.deepEqual(first.maps[0].pawns.map(p => p.id).sort(), prior.maps[0].pawns.map(p => p.id).sort());
  }
  if (mode === 'terrain' || mode === 'objects') {
    if (mode === 'objects') {
      assert.ok(result.terrain.objectDraws > 50, 'No meaningful object submissions rendered');
      assert.ok(result.terrain.textures > 8, 'Object textures were not captured');
      const supported = ['Cutout', 'CutoutComplex', 'Transparent', 'CutoutPlant', 'CutoutSkin', 'CutoutHair'];
      for (const shader of supported)
        assert.ok(!Object.keys(result.terrain.omitted).some(key => key === shader || key.startsWith(`${shader}:`)), `${shader} submissions omitted`);
      assert.equal(result.terrain.objectDraws, result.gameDraws.groups
        .filter(group => supported.includes(group.state.shader)).reduce((sum, group) => sum + group.count, 0));
      for (const shader of [...supported, 'LightOverlay', 'EdgeShadow', 'FogOfWar']) {
        const expected = result.gameDraws.groups.filter(group => group.state.shader === shader)
          .reduce((sum, group) => sum + group.count, 0);
        assert.equal(result.terrain.shaders[shader] ?? 0, expected, `${shader} draw count changed`);
      }
    }
    assert.equal(result.entry.omittedErrorCount, 0, 'Distinct engine errors were lost');
    assert.ok(result.terrain.draws > 0, 'No actual terrain draws');
    assert.ok(result.terrain.textures > 0, 'No actual terrain textures');
    assert.ok(result.terrain.distinctColors > 20, 'Terrain output is blank or flat');
    assert.equal(result.terrain.fullColonyFrame, false);
    console.log(`Verified saved-colony identity, advancing simulation and partial ${mode === 'objects' ? 'terrain/object' : 'terrain'} rendering. Omitted layers and UI remain incomplete.`);
    process.exit(0);
  }
  console.log('Verified a generated quick-test colony and advancing simulation in WASM. Browser rendering remains unimplemented for this path.');
  process.exit(0);
}
if (mode === 'scene') {
  const scene = result.engineScene;
  assert.equal(scene.status, 'pass');
  assert.equal(scene.rejectedAfterStop, true);
  assert.equal(scene.rejectedRestart, true);
  assert.equal(scene.colony, false);
  assert.equal(scene.nativeRenderPasses, false);
  assert.deepEqual(scene.frames.map(frame => frame.commands), [1, 0, 1]);
  assert.deepEqual(scene.frames.map(frame => frame.unityFrame), [1, 2, 3]);
  assert.deepEqual(scene.frames.map(frame => frame.engineFrame), [0, 1, 2]);
  assert.deepEqual(scene.frames.map(frame => frame.left), [[0,255,0,255],[0,0,0,255],[0,0,0,255]]);
  assert.deepEqual(scene.frames.map(frame => frame.right), [[0,0,0,255],[0,0,0,255],[0,255,0,255]]);
  assert.ok(scene.frames.every(frame => Number.isFinite(frame.delta) && frame.delta >= 0));
  console.log('Verified browser-paced engine scene updates, renderer state changes, and shutdown. No colony loaded.');
  process.exit(0);
}
if (mode === 'draws') {
  assert.equal(result.draws.status, 'pass');
  assert.equal(result.draws.commandCount, 2);
  assert.deepEqual(result.draws.vertexCounts, [6, 6]);
  assert.deepEqual(result.draws.left, [255, 0, 0, 255]);
  assert.deepEqual(result.draws.right, [0, 255, 0, 255]);
  assert.deepEqual(result.draws.middle, [0, 0, 0, 255]);
  assert.equal(result.draws.fullEngineFrame, false);
  console.log('Verified real game mesh submissions rendered through WebGL; no full engine frame established.');
  process.exit(0);
}
if (mode === 'textures') {
  assert.equal(result.draws.status, 'pass');
  assert.equal(result.draws.commandCount, 2);
  assert.equal(result.draws.texture.name, 'Resources/Textures/Terrain/Surfaces/Concrete.psd.dds.split');
  assert.equal(result.draws.texture.format, 'Bc7');
  assert.equal(result.draws.texture.width, 512);
  assert.equal(result.draws.texture.height, 512);
  assert.equal(result.draws.samples.length, 3);
  for (const sample of result.draws.samples) assert.deepEqual(sample.actual, sample.expected);
  assert.ok(new Set(result.draws.samples.map(sample => sample.actual.join())).size > 1);
  assert.deepEqual(result.draws.middle, [0, 0, 0, 255]);
  assert.deepEqual(result.draws.left, result.draws.right);
  assert.equal(result.draws.fullEngineFrame, false);
  console.log('Verified installed BC7 terrain texture decoded in WASM and sampled through real mesh submissions in WebGL.');
  process.exit(0);
}
if (mode === 'offline-baseline' || mode === 'offline-patched') {
  const check = result.offline.checks.find(check => check.name === 'offline.actual-setter');
  assert.equal(check?.ok, true, check?.stack);
  assert.equal(result.offline.modInitialized, false);
  assert.equal(check.value, mode === 'offline-baseline' ? 'False,True,False' : 'True,True,True');
  if (mode === 'offline-patched') {
    assert.equal(result.offlineManifest.dynamicDetours, false);
    assert.equal(result.offlineManifest.prefix, 'SlopWorld.Patch_RunInBackground.Prefix(ref bool)');
    assert.notEqual(result.offlineManifest.originalMod, result.offlineManifest.stagedMod);
    const game = result.manifest.find(item => item.file === 'Assembly-CSharp.dll');
    assert.equal(game.stagedSha256, result.offlineManifest.stagedTarget);
    assert.equal(game.sha256, result.offlineManifest.originalTarget);
    assert.notEqual(game.sha256, game.stagedSha256);
  }
  console.log(`Verified ${mode}: actual game setter behavior, without mod initialization.`);
  process.exit(0);
}
if (mode === 'jobs') {
  assert.equal(result.jobs.checks.length, 4);
  for (const check of result.jobs.checks) assert.equal(check.ok, true, check.stack ?? check.name);
  const stages = new Map(result.lifecycle.checks.map(check => [check.name, check]));
  assert.equal(stages.get('assets.open')?.ok, true);
  assert.equal(stages.get('engine.headless-constructor')?.ok, true);
  assert.equal(stages.get('engine.headless-dispose')?.ok, true);
  assert.equal(stages.get('game.bootstrap')?.ok, false);
  assert.match(stages.get('game.bootstrap').stack, /WaylithClr.FieldInjectionBatch/);
  assert.match(stages.get('game.bootstrap').stack, /PrepatcherCompat.Install/);
  assert.equal(result.lifecycle.rendered, false);
  console.log('Verified serial job execution and real bootstrap reaching the custom CLR injection boundary.');
  process.exit(0);
}
if (['lifecycle', 'lifecycle-paths', 'lifecycle-assets'].includes(mode)) {
  const stages = new Map(result.lifecycle.checks.map(check => [check.name, check]));
  assert.equal(result.lifecycle.rendered, false);
  assert.equal(stages.get('engine.headless-constructor')?.ok, true);
  assert.equal(stages.get('engine.headless-dispose')?.ok, true);
  assert.equal(stages.get('game.bootstrap')?.ok, false);
  const stack = stages.get('game.bootstrap').stack;
  if (mode === 'lifecycle') assert.match(stack, /ArgumentNullException/);
  if (mode === 'lifecycle-paths') assert.match(stack, /asset pack \(\/Assets.zip\) is missing/);
  if (mode === 'lifecycle-assets') {
    assert.ok(result.assetTransfer.bytes > 0);
    assert.equal(stages.get('assets.open')?.ok, true);
    assert.ok(Number(stages.get('assets.open').value) > 0);
    assert.match(stack, /WaylithJobRuntime/);
    assert.match(stack, /Thread.Start/);
    assert.match(stack, /PlatformNotSupportedException/);
  }
  console.log(`Verified ${mode} checkpoint; game bootstrap remains incomplete.`);
  process.exit(0);
}
if (mode === 'input') {
  assert.equal(result.input.status, 'pass');
  assert.equal(result.input.checks.length, 6);
  console.log('Verified browser event mapping through installed Waylith and Unity input owners.');
  process.exit(0);
}
if (mode === 'adapted') {
  assert.match(checks.get('game.entrypoint')?.stack ?? '', /PosixSignalRegistration/);
  const deep = new Map(result.deep.checks.map(check => [check.name, check]));
  for (const [name, pattern] of [['engine.constructor', /DllNotFoundException: glfw/],
    ['native.vulkan-initializer', /Failed to load Vulkan library/],
    ['native.audio-initialize', /DllNotFoundException: waylithaudio/]]) {
    assert.equal(deep.get(name)?.ok, false, name);
    assert.match(deep.get(name)?.stack ?? '', pattern, name);
  }
  console.log('Reproduced POSIX lifecycle, GLFW, Vulkan, and audio boundaries after bounded startup adaptations.');
  process.exit(0);
}
if (mode === 'startup') {
  const entry = checks.get('game.entrypoint');
  if (!entry?.ok) {
    console.error('Colony startup blocked:\n' + (entry?.stack ?? 'No game entry-point result'));
    process.exitCode = 1;
  }
} else {
  assert.equal(result.graphics.vertexCount, 1152);
  assert.match(result.graphics.backend, /WebGL 2/);
  const [r, g, b, a] = result.graphics.centerPixel;
  assert.ok(g > 50 && g > r && g > b && a === 255, 'Expected actual green grid pixel');
  if (process.env.REQUIRE_SLOPD === '1') assert.equal(result.host.ok, true);
  console.log('Verified real game IL in WASM, emitted IL execution, and WebGL grid pixels.');
  console.log(`Game startup: ${checks.get('game.entrypoint')?.ok ? 'returned (colony not verified)' : 'blocked; run just browser-startup for the failure'}`);
}
