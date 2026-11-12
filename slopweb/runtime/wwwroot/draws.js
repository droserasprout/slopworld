import { createSceneDefaultRenderer, loadSceneShaders } from './graphics/renderer.js';

export async function renderSubmissions(managed) {
    const originals = await Promise.all(['vert', 'frag'].map(async stage => {
        const response = await fetch(`./graphics/SceneDefault.${stage}.glsl`);
        if (!response.ok) throw new Error(`Original shader HTTP ${response.status}`);
        return response.text();
    }));
    managed.InstallSceneShaders(...originals);
    const snapshot = JSON.parse(new URLSearchParams(location.search).has('textures') ? managed.SubmitPackedGameMeshes() : managed.SubmitGameMeshes());
    if (snapshot.commandCount !== 2) throw new Error('Expected two actual engine submissions');
    const canvas = document.createElement('canvas');
    canvas.width = 400; canvas.height = 240;
    document.body.prepend(canvas);
    const renderer = createSceneDefaultRenderer(canvas, await loadSceneShaders(new URL('./graphics/', location.href)));
    try {
        // Top-down projection of the game's X/Z plane, Vulkan clip depth 0.5.
        const viewProjection = new Float32Array([1,0,0,0, 0,0,1,0, 0,1,0,0, 0,0,.5,1]);
        const textureBytes = [];
        snapshot.draws.forEach((draw, index) => {
            if (draw.texture) {
                const bytes = Uint8Array.from(atob(draw.texture.rgba), c => c.charCodeAt(0));
                textureBytes.push(bytes);
                renderer.setTexture(bytes, draw.texture.width, draw.texture.height);
            }
            renderer.draw({
            ...draw, vertices: new Float32Array(draw.vertices), viewProjection,
            clear: index === 0 ? [0,0,0,1] : null, textured: !!draw.texture,
        }); });
        const left = renderer.readPixel(100,120), right = renderer.readPixel(300,120);
        const middle = renderer.readPixel(200,120);
        const samples = [];
        if (!textureBytes.length && (left.join() !== '255,0,0,255' || right.join() !== '0,255,0,255' || middle.join() !== '0,0,0,255'))
            throw new Error(`Engine draw pixels mismatch: ${JSON.stringify({left,right,middle})}`);
        if (textureBytes.length) {
            const {width,height} = snapshot.draws[0].texture;
            for (const [x,y] of [[70,100],[100,120],[130,140]]) {
                const u = (x + .5 - 30) / 140, v = (y + .5 - 78) / 84;
                const offset = (Math.floor(v * height) * width + Math.floor(u * width)) * 4;
                const expected = Array.from(textureBytes[0].slice(offset, offset + 4));
                const actual = renderer.readPixel(x,y);
                if (expected.join() !== actual.join()) throw new Error(`Texture sample mismatch: ${JSON.stringify({x,y,expected,actual})}`);
                samples.push({x,y,expected,actual});
            }
            if (middle.join() !== '0,0,0,255') throw new Error('Texture drew outside submitted geometry');
        }
        return { status: 'pass', backend: renderer.backend, commandCount: snapshot.commandCount,
            vertexCounts: snapshot.draws.map(draw => draw.vertices.length / 14),
            samples, texture: snapshot.draws[0].texture ? { name: snapshot.draws[0].texture.name, width: snapshot.draws[0].texture.width, height: snapshot.draws[0].texture.height, format: snapshot.draws[0].texture.format } : null,
            left, right, middle, source: 'Verse.MeshMakerPlanes → UnityEngine.Graphics.DrawMesh → Waylith.MeshDrawSubmission',
            fullEngineFrame: false };
    } finally { renderer.dispose(); }
}
