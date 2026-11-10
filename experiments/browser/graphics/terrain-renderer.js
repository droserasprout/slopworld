// Render the actual colony's terrain submissions. Other map layers remain explicit
// omissions in the managed capture report; this is not a complete game renderer.
export async function createTerrainRenderer(canvas, base, objects = false) {
  const sprites = ['Cutout', 'CutoutComplex', 'Transparent', 'CutoutPlant', 'CutoutSkin', 'CutoutHair'];
  const overlays = ['LightOverlay', 'EdgeShadow', 'FogOfWar'];
  const gl = canvas.getContext('webgl2', { antialias: false, alpha: false, preserveDrawingBuffer: true });
  if (!gl) throw new Error('WebGL2 unavailable');
  const resources = [];
  const own = (value, remove) => { resources.push(() => remove.call(gl, value)); return value; };
  const read = async name => {
    const response = await fetch(new URL(name, base));
    if (!response.ok) throw new Error(`${name}: HTTP ${response.status}`);
    return response.text();
  };
  function compile(type, source) {
    const shader = own(gl.createShader(type), gl.deleteShader);
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(shader));
    return shader;
  }
  try {
    const vertex = compile(gl.VERTEX_SHADER, await read('MapWorldUV.vert.glsl'));
    const objectVertex = objects ? compile(gl.VERTEX_SHADER, await read('Object.vert.glsl')) : null;
    const plantVertex = objects ? compile(gl.VERTEX_SHADER, await read('PlantWindSway.vert.glsl')) : null;
    const programs = new Map();
    for (const name of ['TerrainHard', 'TerrainFade', 'TerrainFadeRough', ...(objects ? [...sprites, ...overlays] : [])]) {
      const program = own(gl.createProgram(), gl.deleteProgram);
      gl.attachShader(program, !sprites.includes(name) ? vertex : name === 'CutoutPlant' ? plantVertex : objectVertex);
      gl.attachShader(program, compile(gl.FRAGMENT_SHADER, await read(`${name}.frag.glsl`)));
      gl.linkProgram(program);
      if (!gl.getProgramParameter(program, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(program));
      ['Push', 'VertexUniforms', 'MaterialUniforms'].forEach((block, binding) => {
        const index = gl.getUniformBlockIndex(program, block);
        if (index === gl.INVALID_INDEX) throw new Error(`Missing ${name}.${block}`);
        gl.uniformBlockBinding(program, index, binding);
      });
      programs.set(name, program);
    }
    const vao = own(gl.createVertexArray(), gl.deleteVertexArray);
    const buffer = own(gl.createBuffer(), gl.deleteBuffer);
    const uniforms = [0, 1, 2].map(() => own(gl.createBuffer(), gl.deleteBuffer));
    function uniform(binding, data) {
      gl.bindBuffer(gl.UNIFORM_BUFFER, uniforms[binding]);
      gl.bufferData(gl.UNIFORM_BUFFER, new Float32Array(data), gl.DYNAMIC_DRAW);
      gl.bindBufferBase(gl.UNIFORM_BUFFER, binding, uniforms[binding]);
    }
    const textures = new Map();
    function upload(id, data) {
      if (textures.has(id)) return textures.get(id);
      const texture = own(gl.createTexture(), gl.deleteTexture);
      gl.bindTexture(gl.TEXTURE_2D, texture);
      const rgba = Uint8Array.from(atob(data.rgba), c => c.charCodeAt(0));
      if (rgba.length !== data.width * data.height * 4) throw new Error('Incomplete terrain texture');
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, data.width, data.height, 0, gl.RGBA, gl.UNSIGNED_BYTE, rgba);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.REPEAT);
      textures.set(id, texture);
      return texture;
    }
    let disposed = false;
    return {
      draw(frame) {
        if (disposed) throw new Error('Terrain renderer is disposed');
        gl.viewport(0, 0, canvas.width, canvas.height);
        gl.disable(gl.CULL_FACE); gl.disable(gl.DITHER);
        gl.enable(gl.DEPTH_TEST); gl.depthFunc(gl.GEQUAL); gl.depthMask(true); gl.clearDepth(0);
        gl.clearColor(0.02, 0.03, 0.05, 1); gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
        gl.bindVertexArray(vao); gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
        gl.enableVertexAttribArray(0); gl.enableVertexAttribArray(2);
        gl.disableVertexAttribArray(1); gl.vertexAttrib3f(1, 0, 1, 0);
        uniform(1, frame.camera.viewProjection);
        for (const draw of frame.draws) {
          const program = programs.get(draw.shader);
          if (!program) throw new Error(`Unsupported terrain shader ${draw.shader}`);
          gl.useProgram(program);
          const sprite = sprites.includes(draw.shader);
          const stride = draw.stride ?? 7;
          gl.vertexAttribPointer(0, 3, gl.FLOAT, false, stride * 4, 0);
          gl.vertexAttribPointer(2, 4, gl.FLOAT, false, stride * 4, 12);
          if (sprite) { gl.enableVertexAttribArray(3); gl.vertexAttribPointer(3, 4, gl.FLOAT, false, stride * 4, 28); }
          else { gl.disableVertexAttribArray(3); gl.vertexAttrib4f(3, 0, 0, 0, 0); }
          gl.depthMask(draw.shader.startsWith('Cutout'));
          if (draw.shader === 'TerrainHard') gl.disable(gl.BLEND);
          else if (draw.shader === 'LightOverlay') { gl.enable(gl.BLEND); gl.blendFunc(gl.DST_COLOR, gl.SRC_COLOR); }
          else if (draw.shader === 'EdgeShadow') { gl.enable(gl.BLEND); gl.blendFunc(gl.ZERO, gl.SRC_COLOR); }
          else { gl.enable(gl.BLEND); gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA, gl.ONE, gl.ONE_MINUS_SRC_ALPHA); }
          uniform(0, draw.model);
          if (draw.uniforms) {
            gl.bindBuffer(gl.UNIFORM_BUFFER, uniforms[2]);
            gl.bufferData(gl.UNIFORM_BUFFER, Uint8Array.from(atob(draw.uniforms), c => c.charCodeAt(0)), gl.DYNAMIC_DRAW);
            gl.bindBufferBase(gl.UNIFORM_BUFFER, 2, uniforms[2]);
          } else uniform(2, sprite ? [...draw.color, ...draw.textureST, ...(draw.shader === 'CutoutComplex' ? draw.colorTwo : [])] : draw.color);
          for (const [unit, name, id] of [[0, '_MainTex', draw.texture], [1, '_AlphaAddTex', draw.noise], [2, '_MaskTex', draw.mask], [3, '_FallColorDestination', draw.fall], [4, '_DetailTex', draw.detail]]) {
            if (id == null) continue;
            gl.activeTexture(gl.TEXTURE0 + unit);
            gl.bindTexture(gl.TEXTURE_2D, upload(id, frame.textures[id]));
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, sprite ? gl.CLAMP_TO_EDGE : gl.REPEAT);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, sprite ? gl.CLAMP_TO_EDGE : gl.REPEAT);
            gl.uniform1i(gl.getUniformLocation(program, name), unit);
          }
          gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(draw.vertices), gl.DYNAMIC_DRAW);
          gl.drawArrays(gl.TRIANGLES, 0, draw.vertices.length / stride);
        }
        const error = gl.getError();
        if (error !== gl.NO_ERROR) throw new Error(`Terrain WebGL error 0x${error.toString(16)}`);
        const pixels = new Uint8Array(canvas.width * canvas.height * 4);
        gl.readPixels(0, 0, canvas.width, canvas.height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
        const colors = new Set();
        for (let i = 0; i < pixels.length; i += 4) colors.add(`${pixels[i]},${pixels[i+1]},${pixels[i+2]}`);
        const shaders = {};
        for (const draw of frame.draws) shaders[draw.shader] = (shaders[draw.shader] ?? 0) + 1;
        return { draws: frame.draws.length, objectDraws: frame.draws.filter(d => sprites.includes(d.shader)).length,
          overlayDraws: frame.draws.filter(d => overlays.includes(d.shader)).length, shaders,
          textures: textures.size, distinctColors: colors.size, fullColonyFrame: false };
      },
      readPixel(x, y) {
        const pixel = new Uint8Array(4);
        gl.readPixels(x, y, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, pixel);
        return Array.from(pixel);
      },
      dispose() { if (!disposed) { disposed = true; resources.reverse().forEach(remove => remove()); } },
    };
  } catch (error) { resources.reverse().forEach(remove => remove()); throw error; }
}
