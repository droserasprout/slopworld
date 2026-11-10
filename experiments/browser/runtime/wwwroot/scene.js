import { createSceneDefaultRenderer, loadSceneShaders } from './graphics/renderer.js';

// Browser owns pacing; installed runtime owns frame bookkeeping and scene updates.
export async function runScene(managed) {
    const originals = await Promise.all(['vert', 'frag'].map(async stage => {
        const response = await fetch(`./graphics/SceneDefault.${stage}.glsl`);
        if (!response.ok) throw new Error(`Original shader HTTP ${response.status}`);
        return response.text();
    }));
    managed.InstallSceneShaders(...originals);
    const canvas = document.createElement('canvas');
    canvas.width = 400; canvas.height = 240;
    document.body.prepend(canvas);
    const renderer = createSceneDefaultRenderer(canvas, await loadSceneShaders(new URL('./graphics/',location.href)));
    const frames = [];
    try {
        managed.StartScene();
        for (let phase = 0; phase < 3; phase++) {
            const timestamp = await new Promise(resolve => requestAnimationFrame(resolve));
            const frame = JSON.parse(managed.SceneFrame(timestamp, phase));
            renderer.clear();
            for (const draw of frame.snapshot.draws) renderer.draw({
                ...draw, vertices: new Float32Array(draw.vertices), clear: null,
                viewProjection: [1,0,0,0, 0,0,1,0, 0,1,0,0, 0,0,.5,1],
            });
            const left = renderer.readPixel(100,120), right = renderer.readPixel(300,120);
            const green = '0,255,0,255', black = '0,0,0,255';
            if (left.join() !== (phase === 0 ? green : black) || right.join() !== (phase === 2 ? green : black))
                throw new Error(`Scene phase ${phase}: ${JSON.stringify({left,right,frame})}`);
            if (frame.snapshot.commandCount !== (phase === 1 ? 0 : 1)) throw new Error('Unexpected scene draw count');
            frames.push({ phase, unityFrame: frame.unityFrame, engineFrame: frame.engineFrame,
                delta: frame.delta, elapsed: frame.elapsed, commands: frame.snapshot.commandCount, left, right });
        }
        managed.StopScene();
        managed.StopScene();
        let rejectedAfterStop = false;
        try { managed.SceneFrame(performance.now(),0); } catch { rejectedAfterStop = true; }
        if (!rejectedAfterStop) throw new Error('Stopped scene accepted a frame');
        let rejectedRestart = false;
        try { managed.StartScene(); } catch { rejectedRestart = true; }
        if (!rejectedRestart) throw new Error('Process-lifetime drivers allowed an unsupported restart');
        return { status: 'pass', frames, rejectedAfterStop, rejectedRestart,
            sceneOwner: 'UnityRuntime.Update → UnityWorld.PostLateUpdate → MeshRendererDriver',
            renderer: renderer.backend, colony: false, nativeRenderPasses: false };
    } finally { managed.StopScene(); renderer.dispose(); }
}
