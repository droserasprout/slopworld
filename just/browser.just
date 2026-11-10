# Experimental local WebAssembly probes; output may contain local game assemblies.
[group('browser prototype')]
browser-serve directory port='8787':
    node experiments/browser/harness/server.mjs {{ quote(directory) }} {{ quote(port) }}

# Run a probe in Chromium; preserve JSON and screenshot evidence locally.
[group('browser prototype')]
browser-validate directory page='/' report='experiments/browser/artifacts/browser.json':
    node experiments/browser/harness/validate.mjs {{ quote(directory) }} {{ quote(page) }} {{ quote(report) }}

[group('browser prototype')]
browser-test-harness:
    node --test experiments/browser/harness/server.test.mjs

# Check whether a long interpreter frame is working or waiting.
[group('browser prototype')]
browser-processes:
    ps -eo pid,ppid,etime,pcpu,pmem,stat,args | rg 'chromium.*(renderer|remote-debugging)|harness/validate' | head -20

[group('browser prototype')]
browser-console profile:
    node experiments/browser/harness/console.mjs {{quote(profile)}}

# Exercise DOM events through installed Waylith and Unity input in WASM.
[group('browser prototype')]
browser-input:
    just --justfile experiments/browser/runtime/justfile stage-adapted
    just browser-validate experiments/browser/runtime/out/wwwroot-adapted '/index.html?input' experiments/browser/artifacts/input.json
    node experiments/browser/harness/verify.mjs experiments/browser/artifacts/input.json input

# Run the actual engine constructor and RimWorld bootstrap without native rendering.
[group('browser prototype')]
browser-lifecycle:
    just --justfile experiments/browser/runtime/justfile stage-adapted
    just browser-validate experiments/browser/runtime/out/wwwroot-adapted '/index.html?lifecycle' experiments/browser/artifacts/lifecycle.json
    node experiments/browser/harness/verify.mjs experiments/browser/artifacts/lifecycle.json lifecycle
    just --justfile experiments/browser/runtime/justfile stage-lifecycle
    just browser-validate experiments/browser/runtime/out/wwwroot-lifecycle '/index.html?lifecycle' experiments/browser/artifacts/lifecycle-paths.json
    node experiments/browser/harness/verify.mjs experiments/browser/artifacts/lifecycle-paths.json lifecycle-paths

[group('browser prototype')]
browser-assets:
    just --justfile experiments/browser/runtime/justfile stage-assets
    just browser-validate experiments/browser/runtime/out/wwwroot-lifecycle '/index.html?lifecycle&assets' experiments/browser/artifacts/lifecycle-assets.json
    node experiments/browser/harness/verify.mjs experiments/browser/artifacts/lifecycle-assets.json lifecycle-assets

[group('browser prototype')]
browser-jobs:
    just --justfile experiments/browser/runtime/justfile stage-jobs
    just browser-validate experiments/browser/runtime/out/wwwroot-jobs '/index.html?jobs&lifecycle&assets' experiments/browser/artifacts/jobs.json
    node experiments/browser/harness/verify.mjs experiments/browser/artifacts/jobs.json jobs

[group('browser prototype')]
browser-offline:
    just --justfile experiments/browser/runtime/justfile stage-adapted
    just browser-validate experiments/browser/runtime/out/wwwroot-adapted '/index.html?offline' experiments/browser/artifacts/offline-baseline.json
    node experiments/browser/harness/verify.mjs experiments/browser/artifacts/offline-baseline.json offline-baseline
    just --justfile experiments/browser/runtime/justfile copy-offline
    just --justfile experiments/browser/compat/justfile offline ../runtime/out/wwwroot-offline
    just browser-validate experiments/browser/runtime/out/wwwroot-offline '/index.html?offline&patched' experiments/browser/artifacts/offline-patched.json
    node experiments/browser/harness/verify.mjs experiments/browser/artifacts/offline-patched.json offline-patched

[group('browser prototype')]
browser-draws:
    just --justfile experiments/browser/runtime/justfile stage-adapted
    just --justfile experiments/browser/graphics/justfile prepare
    mkdir -p experiments/browser/runtime/out/wwwroot-adapted/graphics
    cp experiments/browser/graphics/out/renderer.js experiments/browser/graphics/out/scene.*.glsl experiments/browser/runtime/out/wwwroot-adapted/graphics/
    cp experiments/browser/graphics/out/SceneDefault.*.glsl experiments/browser/runtime/out/wwwroot-adapted/graphics/
    just browser-validate experiments/browser/runtime/out/wwwroot-adapted '/index.html?draws' experiments/browser/artifacts/draws.json
    node experiments/browser/harness/verify.mjs experiments/browser/artifacts/draws.json draws

[group('browser prototype')]
browser-textures: browser-draws
    just --justfile experiments/browser/runtime/justfile copy-draw-assets
    just browser-validate experiments/browser/runtime/out/wwwroot-adapted '/index.html?draws&textures&assets' experiments/browser/artifacts/textures.json
    node experiments/browser/harness/verify.mjs experiments/browser/artifacts/textures.json textures

[group('browser prototype')]
browser-scene:
    just --justfile experiments/browser/runtime/justfile stage-jobs
    just --justfile experiments/browser/graphics/justfile prepare
    mkdir -p experiments/browser/runtime/out/wwwroot-jobs/graphics
    cp experiments/browser/graphics/out/renderer.js experiments/browser/graphics/out/scene.*.glsl experiments/browser/graphics/out/SceneDefault.*.glsl experiments/browser/runtime/out/wwwroot-jobs/graphics/
    just browser-validate experiments/browser/runtime/out/wwwroot-jobs '/index.html?scene' experiments/browser/artifacts/scene.json
    node experiments/browser/harness/verify.mjs experiments/browser/artifacts/scene.json scene

[group('browser prototype')]
browser-entry:
    just --justfile experiments/browser/runtime/justfile stage-core
    PROBE_PROGRESS=1 PROBE_COMMAND_TIMEOUT_MS=300000 PROBE_TIMEOUT_MS=600000 just browser-validate experiments/browser/runtime/out/wwwroot-core '/index.html?entry&assets' experiments/browser/artifacts/entry.json
    node experiments/browser/harness/verify.mjs experiments/browser/artifacts/entry.json entry

[group('browser prototype')]
browser-play:
    just --justfile experiments/browser/runtime/justfile stage-core
    PROBE_PROGRESS=1 PROBE_COMMAND_TIMEOUT_MS=900000 PROBE_TIMEOUT_MS=1800000 just browser-validate experiments/browser/runtime/out/wwwroot-core '/index.html?entry&assets&play' experiments/browser/artifacts/play.json
    just browser-play-check

[group('browser prototype')]
browser-play-check:
    node experiments/browser/harness/verify.mjs experiments/browser/artifacts/play.json play

# Export a generated colony through the game's own save writer.
[group('browser prototype')]
browser-save:
    just --justfile experiments/browser/runtime/justfile stage-core
    PROBE_SAVE_FILE=experiments/browser/artifacts/BrowserColony.rws PROBE_PROGRESS=1 PROBE_COMMAND_TIMEOUT_MS=900000 PROBE_TIMEOUT_MS=1800000 just browser-validate experiments/browser/runtime/out/wwwroot-core '/index.html?entry&assets&play&save' experiments/browser/artifacts/save.json
    node experiments/browser/harness/verify.mjs experiments/browser/artifacts/save.json play

# Reuse the local browser-generated save for further rendering work.
[group('browser prototype')]
browser-load:
    test -s experiments/browser/artifacts/BrowserColony.rws
    just --justfile experiments/browser/runtime/justfile stage-core
    cp experiments/browser/artifacts/BrowserColony.rws experiments/browser/runtime/out/wwwroot-core/assets/
    PROBE_PROGRESS=1 PROBE_COMMAND_TIMEOUT_MS=900000 PROBE_TIMEOUT_MS=1800000 just browser-validate experiments/browser/runtime/out/wwwroot-core '/index.html?entry&assets&play&load' experiments/browser/artifacts/load.json
    node experiments/browser/harness/verify.mjs experiments/browser/artifacts/load.json load experiments/browser/artifacts/save.json

[group('browser prototype')]
browser-load-check report='experiments/browser/artifacts/load.json':
    node experiments/browser/harness/verify.mjs {{quote(report)}} load experiments/browser/artifacts/save.json

[group('browser prototype')]
browser-terrain-shaders:
    just --justfile experiments/browser/graphics/justfile prepare
    just browser-validate experiments/browser/graphics/out /terrain-probe.html experiments/browser/artifacts/terrain-shaders.json

# Render real saved-colony terrain; other map layers are reported as omitted.
[group('browser prototype')]
browser-terrain:
    test -s experiments/browser/artifacts/BrowserColony.rws
    just --justfile experiments/browser/runtime/justfile stage-core
    cp experiments/browser/artifacts/BrowserColony.rws experiments/browser/runtime/out/wwwroot-core/assets/
    just --justfile experiments/browser/graphics/justfile prepare
    mkdir -p experiments/browser/runtime/out/wwwroot-core/graphics
    cp experiments/browser/graphics/out/terrain-renderer.js experiments/browser/graphics/out/MapWorldUV.vert.glsl experiments/browser/graphics/out/Terrain*.glsl experiments/browser/runtime/out/wwwroot-core/graphics/
    PROBE_PROGRESS=1 PROBE_COMMAND_TIMEOUT_MS=900000 PROBE_TIMEOUT_MS=1800000 just browser-validate experiments/browser/runtime/out/wwwroot-core '/index.html?entry&assets&play&load&terrain' experiments/browser/artifacts/terrain.json
    node experiments/browser/harness/verify.mjs experiments/browser/artifacts/terrain.json terrain experiments/browser/artifacts/save.json

[group('browser prototype')]
browser-objects:
    test -s experiments/browser/artifacts/BrowserColony.rws
    just --justfile experiments/browser/runtime/justfile stage-core
    cp experiments/browser/artifacts/BrowserColony.rws experiments/browser/runtime/out/wwwroot-core/assets/
    just --justfile experiments/browser/graphics/justfile prepare
    mkdir -p experiments/browser/runtime/out/wwwroot-core/graphics
    cp experiments/browser/graphics/out/terrain-renderer.js experiments/browser/graphics/out/MapWorldUV.vert.glsl experiments/browser/graphics/out/Terrain*.glsl experiments/browser/graphics/out/Object.vert.glsl experiments/browser/graphics/out/PlantWindSway.vert.glsl experiments/browser/graphics/out/Cutout*.glsl experiments/browser/graphics/out/Transparent.frag.glsl experiments/browser/graphics/out/LightOverlay.frag.glsl experiments/browser/graphics/out/EdgeShadow.frag.glsl experiments/browser/graphics/out/FogOfWar.frag.glsl experiments/browser/runtime/out/wwwroot-core/graphics/
    PROBE_CANVAS_SCREENSHOT=1 PROBE_PROGRESS=1 PROBE_COMMAND_TIMEOUT_MS=900000 PROBE_TIMEOUT_MS=1800000 just browser-validate experiments/browser/runtime/out/wwwroot-core '/index.html?entry&assets&play&load&terrain&objects' experiments/browser/artifacts/objects.json
    node experiments/browser/harness/verify.mjs experiments/browser/artifacts/objects.json objects experiments/browser/artifacts/save.json

[group('browser prototype')]
browser-objects-check report='experiments/browser/artifacts/objects.json':
    node experiments/browser/harness/verify.mjs {{quote(report)}} objects experiments/browser/artifacts/save.json

[group('browser prototype')]
browser-format:
    rg --files experiments/browser -g '*.cs' -g '*.csproj' -0 | xargs -0 dotnet tool run csharpier format
    uv run --locked ruff check --select I --fix experiments/browser
    uv run --locked ruff format experiments/browser

[group('browser prototype')]
browser-lint:
    rg --files experiments/browser -g '*.cs' -g '*.csproj' -0 | xargs -0 dotnet tool run csharpier check
    uv run --locked ruff check experiments/browser
    uv run --locked ruff format --check experiments/browser
    while IFS= read -r file; do node --check "$file"; done < <(rg --files experiments/browser -g '*.js' -g '*.mjs')

# Build actual managed game probes and derive shaders from the local installation.
[group('browser prototype')]
browser-build:
    just --justfile experiments/browser/runtime/justfile stage
    just --justfile experiments/browser/graphics/justfile prepare
    uv run --locked python experiments/browser/integrate.py

# Open this loopback URL manually; proprietary build output is never published.
[group('browser prototype')]
browser-run: browser-build
    just browser-serve experiments/browser/artifacts/site

# Validate the execution proof. A passing proof does not mean colony startup works.
[group('browser prototype')]
browser-check: browser-build browser-test-harness
    just --justfile experiments/browser/graphics/justfile test
    just browser-validate experiments/browser/graphics/out / experiments/browser/artifacts/graphics.json
    just browser-validate experiments/browser/artifacts/site / experiments/browser/artifacts/integrated.json
    node experiments/browser/harness/verify.mjs experiments/browser/artifacts/integrated.json

# Reproduce game startup; exits nonzero when the original game entry point fails.
[group('browser prototype')]
browser-startup: browser-check
    node experiments/browser/harness/verify.mjs experiments/browser/artifacts/integrated.json startup

# Compile one installed HLSL shader locally, remap its binding, dispatch in WebGPU.
[group('browser prototype')]
browser-webgpu:
    just --justfile experiments/browser/graphics/justfile shader-targets
    mkdir -p experiments/browser/artifacts/webgpu
    cp experiments/browser/webgpu/* experiments/browser/artifacts/webgpu/
    cp experiments/browser/graphics/out/GpuProbe.comp.hlsl.wgsl experiments/browser/artifacts/webgpu/
    PROBE_WEBGPU=1 just browser-validate experiments/browser/artifacts/webgpu / experiments/browser/artifacts/webgpu.json

# Preserve failures after bounded startup adaptations and exercise actual mod APIs.
[group('browser prototype')]
browser-diagnose: mod-build
    just --justfile experiments/browser/runtime/justfile stage-adapted
    just browser-validate experiments/browser/runtime/out/wwwroot-adapted '/index.html?deep' experiments/browser/artifacts/adapted.json
    node experiments/browser/harness/verify.mjs experiments/browser/artifacts/adapted.json adapted
    just --justfile experiments/browser/compat/justfile browser-probe
    node experiments/browser/harness/verify.mjs experiments/browser/compat/out/browser-probe.json compat

# Verify host-authenticated HTTP against the configured real daemon without printing credentials.
[group('browser prototype')]
browser-bridge-check:
    node experiments/browser/harness/bridge-check.mjs

# Exercise browser-enforced Origin and binary WebSocket delivery against real slopd.
[group('browser prototype')]
browser-transport:
    just browser-validate experiments/browser/harness/transport / experiments/browser/artifacts/transport.json

# Exercise installed Unity GUI controls through the initial browser drawing backend.
[group('browser prototype')]
browser-gui:
    just --justfile experiments/browser/runtime/justfile stage-core
    just browser-validate experiments/browser/runtime/out/wwwroot-core '/index.html?gui' experiments/browser/artifacts/gui.json
    node experiments/browser/harness/verify.mjs experiments/browser/artifacts/gui.json gui

[group('browser prototype')]
browser-gui-check:
    node experiments/browser/harness/verify.mjs experiments/browser/artifacts/gui.json gui

# Isolated Eco offline-hook stage; full mod startup is not yet supported.
[group('browser prototype')]
browser-eco-build: mod-build
    just --justfile experiments/browser/runtime/justfile stage-core
    just --justfile experiments/browser/runtime/justfile copy-eco
    just --justfile experiments/browser/compat/justfile eco ../runtime/out/wwwroot-eco

[group('browser prototype')]
browser-eco: browser-eco-build
    cp experiments/browser/artifacts/BrowserColony.rws experiments/browser/runtime/out/wwwroot-eco/assets/
    PROBE_PROGRESS=1 PROBE_COMMAND_TIMEOUT_MS=900000 PROBE_TIMEOUT_MS=1800000 just browser-validate experiments/browser/runtime/out/wwwroot-eco '/index.html?entry&assets&play&load&eco' experiments/browser/artifacts/eco.json

[group('browser prototype')]
browser-mod-http: mod-build
    just --justfile experiments/browser/runtime/justfile stage-core
    just --justfile experiments/browser/runtime/justfile copy-http
    just --justfile experiments/browser/compat/justfile eco ../runtime/out/wwwroot-http
    just browser-validate experiments/browser/runtime/out/wwwroot-http '/index.html?modHttp' experiments/browser/artifacts/mod-http.json
    node experiments/browser/harness/verify.mjs experiments/browser/artifacts/mod-http.json mod-http

[group('browser prototype')]
browser-mod-http-check:
    node experiments/browser/harness/verify.mjs experiments/browser/artifacts/mod-http.json mod-http

# Attempt actual WorkspaceWindow drawing from Entry, without loading a colony.
[group('browser prototype')]
browser-workspace: browser-eco-build
    just --justfile experiments/browser/runtime/justfile workspace-dependencies
    PROBE_PROGRESS=1 PROBE_COMMAND_TIMEOUT_MS=900000 PROBE_TIMEOUT_MS=1800000 just browser-validate experiments/browser/runtime/out/wwwroot-eco '/index.html?entry&assets&eco&workspace' experiments/browser/artifacts/workspace.json
    just browser-workspace-check

[group('browser prototype')]
browser-workspace-check report='experiments/browser/artifacts/workspace.json':
    node experiments/browser/harness/verify.mjs {{quote(report)}} workspace

# Keep the Entry-only workspace runtime available for browser interaction and inspection.
[group('browser prototype')]
browser-workspace-live: browser-eco-build
    just --justfile experiments/browser/runtime/justfile workspace-dependencies
    PROBE_KEEP_OPEN=1 PROBE_PROGRESS=1 PROBE_COMMAND_TIMEOUT_MS=900000 PROBE_TIMEOUT_MS=1800000 just browser-validate experiments/browser/runtime/out/wwwroot-eco '/index.html?entry&assets&eco&workspace&live' experiments/browser/artifacts/workspace-live.json

# Manual Entry-only work interface. Credentials stay in this host server's environment.
[group('browser prototype')]
browser-workspace-run port='8787': browser-eco-build
    just --justfile experiments/browser/runtime/justfile workspace-dependencies
    @echo 'Open http://127.0.0.1:{{port}}/index.html?entry&assets&eco&workspace&live&app'
    just browser-serve experiments/browser/runtime/out/wwwroot-eco {{quote(port)}}

[group('browser prototype')]
browser-evaluate profile script:
    node experiments/browser/harness/evaluate.mjs {{quote(profile)}} {{quote(script)}}

[group('browser prototype')]
browser-close profile:
    node experiments/browser/harness/close.mjs {{quote(profile)}}

[group('browser prototype')]
browser-screenshot profile output='experiments/browser/artifacts/workspace-current.png':
    node experiments/browser/harness/screenshot.mjs {{quote(profile)}} {{quote(output)}}

[group('browser prototype')]
browser-workspace-ui-check profile:
    just browser-evaluate {{quote(profile)}} experiments/browser/harness/workspace-ui.js

# Create two disposable host shells through native controls, verify input/output,
# switch sessions, attach an existing coding agent read-only, then remove test shells.
[group('browser prototype')]
browser-workspace-e2e-check profile:
    PROBE_EVALUATE_TIMEOUT_MS=120000 just browser-evaluate {{quote(profile)}} experiments/browser/harness/workspace-e2e.js
