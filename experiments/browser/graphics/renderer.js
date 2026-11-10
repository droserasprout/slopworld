// Small SceneDefault adapter: caller owns game simulation, asset loading and frame pacing.
// All arrays cross this boundary by value; no native Vulkan objects are accepted.
export const identity = () => new Float32Array([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);

export async function loadSceneShaders(base = new URL("./", import.meta.url)) {
  const read = async name => {
    const response = await fetch(new URL(name, base));
    if (!response.ok) throw new Error(`${name}: HTTP ${response.status}`);
    return response.text();
  };
  return { vertex: await read("scene.vert.glsl"), fragment: await read("scene.frag.glsl") };
}

export function createSceneDefaultRenderer(canvas, sources) {
  const gl = canvas.getContext("webgl2", { antialias: false, alpha: false, preserveDrawingBuffer: true });
  if (!gl) throw new Error("WebGL2 unavailable");
  const resources = [];
  const own = (value, remove) => { resources.push(() => remove.call(gl, value)); return value; };
  function compile(type, source) {
    const shader = own(gl.createShader(type), gl.deleteShader);
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(shader));
    return shader;
  }
  function program(textured) {
    const value = own(gl.createProgram(), gl.deleteProgram);
    const fragment = textured ? sources.fragment.replace("#version 300 es", "#version 300 es\n#define USE_TEXTURE") : sources.fragment;
    gl.attachShader(value, compile(gl.VERTEX_SHADER, sources.vertex));
    gl.attachShader(value, compile(gl.FRAGMENT_SHADER, fragment));
    gl.linkProgram(value);
    if (!gl.getProgramParameter(value, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(value));
    ["Push", "VertexUniforms", "MaterialUniforms"].forEach((name, binding) => {
      const index = gl.getUniformBlockIndex(value, name);
      if (index === gl.INVALID_INDEX) throw new Error(`Missing block ${name}`);
      gl.uniformBlockBinding(value, index, binding);
    });
    return value;
  }
  let programs;
  try { programs = [program(false), program(true)]; }
  catch (error) { resources.reverse().forEach(remove => remove()); throw error; }
  const vao = own(gl.createVertexArray(), gl.deleteVertexArray);
  const vertices = own(gl.createBuffer(), gl.deleteBuffer);
  const uniforms = [0, 1, 2].map(() => own(gl.createBuffer(), gl.deleteBuffer));
  const texture = own(gl.createTexture(), gl.deleteTexture);
  let disposed = false;
  let hasTexture = false;
  function requireLive() { if (disposed) throw new Error("Renderer is disposed"); }
  function bindUniform(binding, data) {
    gl.bindBuffer(gl.UNIFORM_BUFFER, uniforms[binding]);
    gl.bufferData(gl.UNIFORM_BUFFER, data, gl.DYNAMIC_DRAW);
    gl.bindBufferBase(gl.UNIFORM_BUFFER, binding, uniforms[binding]);
  }
  return {
    backend: gl.getParameter(gl.VERSION),
    clear(color = [0, 0, 0, 1]) {
      requireLive();
      if (color.length !== 4) throw new Error("Expected RGBA clear color");
      gl.viewport(0, 0, canvas.width, canvas.height);
      gl.clearColor(...color);
      gl.clear(gl.COLOR_BUFFER_BIT);
    },
    // TexImageSource (e.g. ImageBitmap from a local game PNG), or explicit RGBA8 pixels.
    setTexture(source, width, height) {
      requireLive();
      gl.bindTexture(gl.TEXTURE_2D, texture);
      gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
      if (ArrayBuffer.isView(source)) {
        if (!Number.isInteger(width) || !Number.isInteger(height) || width < 1 || height < 1 || source.byteLength !== width * height * 4) throw new Error("Expected width * height RGBA8 pixels");
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, source);
      } else gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, source);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
      hasTexture = true;
    },
    // Packed Position3 Normal3 Color4 UV4, locations 0..3, matching SceneDefault.
    draw({ vertices: data, model = identity(), viewProjection = identity(), color = [1, 1, 1, 1], textureST = [0, 0, 1, 1], textured = false, clear = [0, 0, 0, 1] }) {
      requireLive();
      if (!(data instanceof Float32Array) || data.length % 42 !== 0 || data.length === 0) throw new Error("Expected complete triangles with 14 floats per vertex");
      if (model.length !== 16 || viewProjection.length !== 16 || color.length !== 4 || textureST.length !== 4 || (clear !== null && clear.length !== 4)) throw new Error("Invalid matrix or color dimensions");
      if (textured && !hasTexture) throw new Error("Upload texture before textured draw");
      gl.viewport(0, 0, canvas.width, canvas.height);
      gl.disable(gl.BLEND);
      gl.disable(gl.CULL_FACE);
      gl.disable(gl.DEPTH_TEST);
      gl.disable(gl.DITHER);
      if (clear !== null) {
        gl.clearColor(...clear);
        gl.clear(gl.COLOR_BUFFER_BIT);
      }
      const active = programs[Number(textured)];
      gl.useProgram(active);
      bindUniform(0, new Float32Array(model));
      bindUniform(1, new Float32Array(viewProjection));
      bindUniform(2, new Float32Array([...color, ...textureST]));
      gl.bindVertexArray(vao);
      gl.bindBuffer(gl.ARRAY_BUFFER, vertices);
      gl.bufferData(gl.ARRAY_BUFFER, data, gl.DYNAMIC_DRAW);
      [[3, 0], [3, 12], [4, 24], [4, 40]].forEach(([size, offset], index) => {
        gl.enableVertexAttribArray(index);
        gl.vertexAttribPointer(index, size, gl.FLOAT, false, 56, offset);
      });
      if (textured) {
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, texture);
        gl.uniform1i(gl.getUniformLocation(active, "_MainTex"), 0);
      }
      gl.drawArrays(gl.TRIANGLES, 0, data.length / 14);
      const error = gl.getError();
      if (error !== gl.NO_ERROR) throw new Error(`WebGL error 0x${error.toString(16)}`);
    },
    readPixel(x, y) {
      requireLive();
      const pixel = new Uint8Array(4);
      gl.readPixels(x, y, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, pixel);
      return Array.from(pixel);
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      resources.reverse().forEach(remove => remove());
    },
  };
}
