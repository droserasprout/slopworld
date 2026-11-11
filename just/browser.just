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
    node --test experiments/browser/harness/gui-keyboard.test.mjs experiments/browser/harness/text-input.test.mjs experiments/browser/harness/clipboard.test.mjs
    node --test experiments/browser/harness/workspace-fullscreen.test.mjs
    node --test experiments/browser/harness/workspace-cursor.test.mjs
    node --test experiments/browser/harness/workspace-audio.test.mjs
    node --test experiments/browser/harness/workspace-scheduler.test.mjs
    node --test experiments/browser/harness/gui-frame-reuse.test.mjs
    node --test experiments/browser/harness/browser-rendering.test.mjs
    node --test experiments/browser/harness/browser-process-cpu.test.mjs
    node --test experiments/browser/harness/fonts.test.mjs
    node --test experiments/browser/harness/server.test.mjs
    node --test experiments/browser/harness/viewport.test.mjs
    node --test experiments/browser/harness/tint.test.mjs
    node --test experiments/browser/harness/gui-probe.test.mjs
    node --test experiments/browser/harness/gui-quads.test.mjs
    node --test experiments/browser/harness/loading-quotes.test.mjs

[group('browser prototype')]
browser-host-rendering-check rendering='auto':
    systemd-run --user --pipe --wait --collect -p CPUQuota=100% --setenv=PROBE_CPU_LIMIT_CORES=1 --setenv=PROBE_RENDERING={{quote(rendering)}} node {{quote(justfile_directory() / 'experiments/browser/harness/rendering-check.mjs')}}

[group('browser prototype')]
browser-host-canvas-reuse-check:
    systemd-run --user --pipe --wait --collect -p CPUQuota=100% --setenv=PROBE_CPU_LIMIT_CORES=1 node {{quote(justfile_directory() / 'experiments/browser/harness/rendering-check.mjs')}} reuse

# Tiny real-Canvas clip equivalence; no game or WASM startup.
[group('browser prototype')]
browser-host-screen-clip-check rendering='auto':
    systemd-run --user --pipe --wait --collect -p CPUQuota=100% --setenv=PROBE_CPU_LIMIT_CORES=1 --setenv=PROBE_RENDERING={{quote(rendering)}} node {{quote(justfile_directory() / 'experiments/browser/harness/rendering-check.mjs')}} clip

[group('browser prototype')]
browser-host-cursor-check rendering='auto':
    systemd-run --user --pipe --wait --collect -p CPUQuota=100% --setenv=PROBE_CPU_LIMIT_CORES=1 --setenv=PROBE_RENDERING={{quote(rendering)}} node {{quote(justfile_directory() / 'experiments/browser/harness/rendering-check.mjs')}} cursor

[group('browser prototype')]
browser-screen-clip-lint:
    node --check experiments/browser/runtime/wwwroot/gui.js
    node --check experiments/browser/harness/screen-clip-check.js
    node --check experiments/browser/harness/rendering-check.mjs
    node --check experiments/browser/harness/benchmark.mjs

[group('browser prototype')]
browser-test-input:
    dotnet run --project experiments/browser/runtime/InputTests/InputTests.csproj
    dotnet run --project experiments/browser/runtime/WorkspaceFrameTests/WorkspaceFrameTests.csproj

[group('browser prototype')]
browser-test-commands:
    dotnet run --project experiments/browser/runtime/CommandTests/CommandTests.csproj

# Node-only packed glyph decode microbenchmark; no browser or native game.
[group('browser prototype')]
browser-bench-quad-decode:
    node experiments/browser/harness/gui-quads-bench.mjs

[group('browser prototype')]
browser-window-check:
    node experiments/browser/harness/fullscreen-check.mjs

[group('browser prototype')]
browser-test-window:
    just --justfile experiments/browser/runtime/PreferencesTests/justfile test
    node --test experiments/browser/harness/gui-keyboard.test.mjs experiments/browser/harness/workspace-fullscreen.test.mjs
    just --justfile experiments/browser/runtime/justfile build

[group('browser prototype')]
browser-test-preferences:
    just --justfile experiments/browser/runtime/PreferencesTests/justfile test

browser-host-preferences-check profile:
    systemd-run --user --pipe --wait --collect node {{ quote(justfile_directory() / 'experiments/browser/harness/preferences-check.mjs') }} {{quote(profile)}}

[group('browser prototype')]
browser-test-startup:
    just --justfile experiments/browser/runtime/StartupTests/justfile test
    just --justfile experiments/browser/runtime/UnderlyingTests/justfile test

# Exercise startup recovery without contacting or stopping the user's daemon.
[group('browser prototype')]
browser-host-offline-startup-check:
    systemd-run --user --pipe --wait --collect -p CPUQuota=200% -p RuntimeMaxSec=600 --setenv=PROBE_CPU_LIMIT_CORES=2 --setenv=SLOPD_URL= --setenv=SLOPD_TOKEN= --setenv=PROBE_PROGRESS=1 --setenv=PROBE_COMMAND_TIMEOUT_MS=540000 --setenv=PROBE_TIMEOUT_MS=540000 node {{ quote(justfile_directory() / 'experiments/browser/harness/validate.mjs') }} {{ quote(justfile_directory() / 'experiments/browser/runtime/out/wwwroot-eco') }} / {{ quote(justfile_directory() / 'experiments/browser/artifacts/offline-startup.json') }}
    just browser-offline-startup-check

[group('browser prototype')]
browser-offline-startup-check report='experiments/browser/artifacts/offline-startup.json':
    node experiments/browser/harness/offline-startup-check.mjs {{quote(report)}}

[group('browser prototype')]
browser-serialization-check profile:
    just browser-evaluate {{quote(profile)}} experiments/browser/harness/serialization-check.js

# Check whether a long interpreter frame is working or waiting.
[group('browser prototype')]
browser-processes:
    ps -eo pid,ppid,etime,pcpu,pmem,stat,args | rg 'chromium.*(renderer|remote-debugging)|harness/validate' | head -20

[group('browser prototype')]
browser-console profile:
    node experiments/browser/harness/console.mjs {{quote(profile)}}

# Observe the selected session without sending input; save Chrome CPU samples and frame intervals.
[group('browser prototype')]
browser-profile profile output='experiments/browser/artifacts/baseline.json' seconds='15':
    node experiments/browser/harness/profile.mjs {{quote(profile)}} {{quote(output)}} {{quote(seconds)}}

# Inspect the running host game and paths without printing daemon credentials.
[group('browser prototype')]
browser-host-inspect:
    systemd-run --user --pipe --wait --collect python3 {{quote(justfile_directory() / 'experiments/browser/harness/host_reference.py')}}

[group('browser prototype')]
browser-host-display:
    systemd-run --user --pipe --wait --collect gdbus call --session --dest org.gnome.Mutter.DisplayConfig --object-path /org/gnome/Mutter/DisplayConfig --method org.gnome.Mutter.DisplayConfig.GetCurrentState

[group('browser prototype')]
browser-host-stop unit:
    systemctl --user stop {{quote(unit)}}

browser-host-status unit:
    systemctl --user show {{quote(unit)}} --property=ActiveState,SubState,Result,ExecMainStatus,MainPID

[group('browser prototype')]
browser-host-close profile:
    systemd-run --user --pipe --wait --collect node {{quote(justfile_directory() / 'experiments/browser/harness/close.mjs')}} {{quote(profile)}}

[group('browser prototype')]
browser-host-serve port='8787':
    systemd-run --user --pipe --wait --collect python3 {{quote(justfile_directory() / 'experiments/browser/harness/host_serve.py')}} {{quote(justfile_directory() / 'experiments/browser/runtime/out/wwwroot-eco')}} {{quote(port)}}

browser-host-url-check port='8787':
    systemd-run --user --pipe --wait --collect node {{quote(justfile_directory() / 'experiments/browser/harness/url-check.mjs')}} {{quote('http://127.0.0.1:' + port)}}

browser-host-appearance:
    systemd-run --user --pipe --wait --collect python3 {{quote(justfile_directory() / 'experiments/browser/harness/host_serve.py')}} --appearance

browser-host-portraits source='':
    systemd-run --user --pipe --wait --collect python3 {{quote(justfile_directory() / 'experiments/browser/harness/host_serve.py')}} --portraits {{quote(source)}}

# Read genuine portraits from an offline copy without replacing the live mod.
browser-host-native-portraits scale='2':
    systemd-run --user --pipe --wait --collect -p CPUQuota=200% -p MemoryMax=6G -p RuntimeMaxSec=300 python3 {{quote(justfile_directory() / 'experiments/browser/harness/native-portraits.py')}} {{quote(scale)}}

[group('browser prototype')]
browser-host-bridge-check:
    systemd-run --user --pipe --wait --collect python3 {{quote(justfile_directory() / 'experiments/browser/harness/host_serve.py')}} --check

# Reproduce the captured host display: 2560x1440 physical pixels, 5/3 desktop scale.
[group('browser prototype')]
browser-workspace-reference session='': browser-eco-build
    just --justfile experiments/browser/runtime/justfile workspace-dependencies
    systemd-run --user --pipe --wait --collect python3 {{quote(justfile_directory() / 'experiments/browser/harness/host_serve.py')}} --reference {{quote(session)}}

browser-workspace-font-reference font session='' size='15': browser-eco-build
    just --justfile experiments/browser/runtime/justfile workspace-dependencies
    just --justfile experiments/browser/runtime/justfile font-pack {{quote(font)}} {{quote(size)}}
    systemd-run --user --pipe --wait --collect python3 {{quote(justfile_directory() / 'experiments/browser/harness/host_serve.py')}} --reference {{quote(session)}} nativeFonts

browser-host-evaluate profile script:
    systemd-run --user --pipe --wait --collect node {{quote(justfile_directory() / 'experiments/browser/harness/evaluate.mjs')}} {{quote(profile)}} {{quote(justfile_directory() / script)}}

browser-host-native-text-check profile:
    just browser-host-evaluate {{quote(profile)}} experiments/browser/harness/native-text-check.js

browser-host-screenshot profile output:
    systemd-run --user --pipe --wait --collect node {{quote(justfile_directory() / 'experiments/browser/harness/screenshot.mjs')}} {{quote(profile)}} {{quote(justfile_directory() / output)}}

browser-host-canvas-reference profile output:
    systemd-run --user --pipe --wait --collect node {{quote(justfile_directory() / 'experiments/browser/harness/screenshot.mjs')}} {{quote(profile)}} {{quote(justfile_directory() / output)}} canvas

browser-host-shortcuts-check profile:
    python3 -c 'from pathlib import Path; p=Path("experiments/browser/harness"); Path("experiments/browser/artifacts/shortcuts-check.js").write_text("(async()=>{await " + (p/"benchmark-workload.js").read_text().strip().removesuffix(";") + ";return await " + (p/"shortcuts-check.js").read_text().lstrip(";").strip().removesuffix(";") + ";})()")'
    just browser-host-evaluate {{quote(profile)}} experiments/browser/artifacts/shortcuts-check.js

browser-host-wake profile:
    systemd-run --user --pipe --wait --collect node {{quote(justfile_directory() / 'experiments/browser/harness/wake.mjs')}} {{quote(profile)}}

browser-host-background:
    systemd-run --user --pipe --wait --collect --working-directory={{quote(justfile_directory())}} just --justfile experiments/browser/runtime/justfile background-cache

browser-host-workspace-check session='' keep='0':
    systemd-run --user --pipe --wait --collect python3 {{quote(justfile_directory() / 'experiments/browser/harness/host_serve.py')}} --check-workspace {{quote(session)}} {{quote(keep)}}

browser-loading-check:
    just --justfile experiments/browser/runtime/justfile refresh-web
    cp experiments/browser/harness/loading.html experiments/browser/runtime/out/wwwroot-eco/loading-probe.html
    just browser-validate experiments/browser/runtime/out/wwwroot-eco /loading-probe.html experiments/browser/artifacts/loading.json

browser-host-font family='monospace':
    systemd-run --user --pipe --wait --collect fc-match --format '%{family}\n%{file}\n' {{quote(family)}}

# Reuse staged redistributable font tooling with the actual host profile/resolver.
browser-host-font-bakes: slopweb-stage
    systemd-run --user --pipe --wait --collect just --justfile {{quote(justfile_directory() / 'justfile')}} slopweb-check-fonts {{quote(env('HOME') + '/.local/share/Steam/steamapps/common/RimWorld')}} {{quote(justfile_directory() / 'experiments/browser/runtime/out/wwwroot-eco')}}
    just browser-font-styles-check

browser-host-font-density-check profile:
    just browser-host-evaluate {{quote(profile)}} experiments/browser/harness/font-density-check.js

browser-host-discarded-drawing-check profile:
    just browser-host-evaluate {{quote(profile)}} experiments/browser/harness/discarded-drawing-check.js

browser-host-profile profile output seconds='15':
    systemd-run --user --pipe --wait --collect node {{quote(justfile_directory() / 'experiments/browser/harness/profile.mjs')}} {{quote(profile)}} {{quote(justfile_directory() / output)}} {{quote(seconds)}}

browser-host-benchmark profile output phases='idle,output,typing,scrolling,switching' quad_cache='on' canvas_reuse='keep' packet_cache='keep' idle_yield='keep' discarded_drawing='keep' screenshots='1' early_input='keep' comparison='none':
    systemd-run --user --pipe --wait --collect --setenv=PROBE_SCREENSHOTS={{quote(screenshots)}} node {{quote(justfile_directory() / 'experiments/browser/harness/benchmark.mjs')}} {{quote(profile)}} {{quote(justfile_directory() / output)}} {{quote(phases)}} {{quote(quad_cache)}} {{quote(canvas_reuse)}} {{quote(packet_cache)}} {{quote(idle_yield)}} {{quote(discarded_drawing)}} {{quote(early_input)}} {{quote(comparison)}}

[group('browser prototype')]
browser-host-workspace-bounded session='slop-al-7' width='1536' height='864' scale='1.666666667' color_type='unorm8' screenshots='1' rendering='auto':
    systemd-run --user --pipe --wait --collect -p CPUQuota=200% -p RuntimeMaxSec=600 --setenv=PROBE_CPU_LIMIT_CORES=2 --setenv=PROBE_WIDTH={{quote(width)}} --setenv=PROBE_HEIGHT={{quote(height)}} --setenv=PROBE_DPR={{quote(scale)}} --setenv=PROBE_SCREENSHOTS={{quote(screenshots)}} --setenv=PROBE_CANVAS_COLOR_TYPE={{quote(color_type)}} --setenv=PROBE_RENDERING={{quote(rendering)}} python3 {{quote(justfile_directory() / 'experiments/browser/harness/host_serve.py')}} --check-workspace {{quote(session)}} 1

# Capture the existing native game window without input or geometry changes.
[group('browser prototype')]
browser-native-reference:
    systemd-run --user --pipe --wait --collect python3 {{quote(justfile_directory() / 'experiments/browser/harness/native_reference.py')}} {{quote(justfile_directory() / 'experiments/browser/artifacts/native-reference.png')}}

# Capture the game window only, without changing focus or including other desktop apps.
[group('browser prototype')]
browser-native-window-reference:
    systemd-run --user --pipe --wait --collect python3 {{quote(justfile_directory() / 'experiments/browser/harness/native_window_reference.py')}} {{quote(justfile_directory() / 'experiments/browser/artifacts/native-window-reference.png')}}

[group('browser prototype')]
browser-compare native browser output *args:
    uv run --locked python experiments/browser/harness/compare_reference.py {{quote(native)}} {{quote(browser)}} {{quote(output)}} {{args}}

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

# Opt-in managed allocation/drawing and browser presentation timings.
[group('browser prototype')]
browser-workspace-profile: browser-eco-build
    just --justfile experiments/browser/runtime/justfile workspace-dependencies
    PROBE_KEEP_OPEN=1 PROBE_PROGRESS=1 PROBE_COMMAND_TIMEOUT_MS=900000 PROBE_TIMEOUT_MS=1800000 just browser-validate experiments/browser/runtime/out/wwwroot-eco '/index.html?entry&assets&eco&workspace&live&profile' experiments/browser/artifacts/workspace-profile.json

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

browser-host-workspace-ui-check profile:
    systemd-run --user --pipe --wait --collect --setenv=PROBE_EVALUATE_TIMEOUT_MS=120000 node {{quote(justfile_directory() / 'experiments/browser/harness/evaluate.mjs')}} {{quote(profile)}} {{quote(justfile_directory() / 'experiments/browser/harness/workspace-ui.js')}}

# Create two disposable host shells through native controls, verify input/output,
# switch sessions, attach an existing coding agent read-only, then remove test shells.
[group('browser prototype')]
browser-workspace-e2e-check profile:
    PROBE_EVALUATE_TIMEOUT_MS=120000 just browser-evaluate {{quote(profile)}} experiments/browser/harness/workspace-e2e.js

browser-host-workspace-e2e-check profile:
    systemd-run --user --pipe --wait --collect --setenv=PROBE_EVALUATE_TIMEOUT_MS=180000 node {{quote(justfile_directory() / 'experiments/browser/harness/evaluate.mjs')}} {{quote(profile)}} {{quote(justfile_directory() / 'experiments/browser/harness/workspace-e2e.js')}}

[group('browser prototype')]
browser-clipboard-check profile:
    PROBE_CLIPBOARD=1 PROBE_EVALUATE_TIMEOUT_MS=120000 just browser-evaluate {{quote(profile)}} experiments/browser/harness/workspace-e2e.js

browser-host-responsive-check profile:
    systemd-run --user --pipe --wait --collect node {{quote(justfile_directory() / 'experiments/browser/harness/responsive-check.mjs')}} {{quote(profile)}}

# Native-font-compatible clipboard check through trusted Chromium shortcuts.
browser-host-clipboard-check profile:
    python3 -c 'from pathlib import Path; import json; p=Path("experiments/browser/harness"); command="python3 -u " + str((p/"benchmark-fixture.py").resolve()); Path("experiments/browser/artifacts/clipboard-integrated.js").write_text("(async()=>{await " + (p/"benchmark-workload.js").read_text().strip().removesuffix(";") + ";window.__clipboardFixtureCommand=" + json.dumps(command) + ";return await " + (p/"clipboard-workspace.js").read_text().strip().removesuffix(";") + ";})()")'
    systemd-run --user --pipe --wait --collect --setenv=PROBE_CLIPBOARD=1 --setenv=PROBE_EVALUATE_TIMEOUT_MS=120000 node {{quote(justfile_directory() / 'experiments/browser/harness/evaluate.mjs')}} {{quote(profile)}} {{quote(justfile_directory() / 'experiments/browser/artifacts/clipboard-integrated.js')}}

# Paste a PNG into an invocation-owned Codex composer without submitting a prompt.
browser-host-image-paste-check profile:
    python3 -c 'from pathlib import Path; p=Path("experiments/browser/harness"); Path("experiments/browser/artifacts/clipboard-image-integrated.js").write_text("(async()=>{await " + (p/"benchmark-workload.js").read_text().strip().removesuffix(";") + ";return await " + (p/"clipboard-image-workspace.js").read_text().strip().removesuffix(";") + ";})()")'
    systemd-run --user --pipe --wait --collect --setenv=PROBE_CLIPBOARD=1 --setenv=PROBE_EVALUATE_TIMEOUT_MS=120000 node {{quote(justfile_directory() / 'experiments/browser/harness/evaluate.mjs')}} {{quote(profile)}} {{quote(justfile_directory() / 'experiments/browser/artifacts/clipboard-image-integrated.js')}}

# Keep disposable edge-row fixtures for visual capture; finish with benchmark cleanup.
browser-host-terminal-visual-check profile:
    python3 -c 'from pathlib import Path; import json; p=Path("experiments/browser/harness"); command="python3 -u " + str((p/"benchmark-fixture.py").resolve()); Path("experiments/browser/artifacts/terminal-visual.js").write_text("(async()=>{await " + (p/"benchmark-workload.js").read_text().strip().removesuffix(";") + ";window.__clipboardFixtureCommand=" + json.dumps(command) + ";return await " + (p/"terminal-visual-check.js").read_text().strip().removesuffix(";") + ";})()")'
    just browser-host-evaluate {{quote(profile)}} experiments/browser/artifacts/terminal-visual.js

browser-font-styles-check root='experiments/browser/runtime/out/wwwroot-eco':
    uv run --locked python experiments/browser/harness/font-styles-check.py {{quote(root)}}

# Installed managed binding APIs and origin storage, without game/browser startup.
[group('browser prototype')]
browser-test-keybindings:
    just --justfile experiments/browser/runtime/KeyBindingsTests/justfile test

browser-host-keybindings-check profile:
    systemd-run --user --pipe --wait --collect node {{quote(justfile_directory() / 'experiments/browser/harness/keybindings-check.mjs')}} {{quote(profile)}}

browser-host-window-check profile:
    systemd-run --user --pipe --wait --collect node {{quote(justfile_directory() / 'experiments/browser/harness/workspace-window-check.mjs')}} {{quote(profile)}}

browser-host-reconnect-check profile:
    systemd-run --user --pipe --wait --collect node {{quote(justfile_directory() / 'experiments/browser/harness/reconnect-check.mjs')}} {{quote(profile)}}

browser-host-composition-check profile reload='0':
    systemd-run --user --pipe --wait --collect node {{quote(justfile_directory() / 'experiments/browser/harness/composition-check.mjs')}} {{quote(profile)}} {{quote(reload)}}

# Inspect private native snapshot and prepared portrait inventories without loading a game.
browser-host-portrait-inventory root='':
    systemd-run --user --pipe --wait --collect python3 {{quote(justfile_directory() / 'experiments/browser/harness/portrait-inventory.py')}} {{quote(root)}}

# Retain an isolated door-check browser with no daemon access or audio mutation.
browser-host-workspace-doors-run root:
    systemd-run --user --pipe --wait --collect -p CPUQuota=200% -p RuntimeMaxSec=600 --setenv=PROBE_CPU_LIMIT_CORES=2 --setenv=PROBE_SCREENSHOTS=0 --setenv=PROBE_KEEP_OPEN=1 --setenv=PROBE_TIMEOUT_MS=300000 --setenv=PROBE_COMMAND_TIMEOUT_MS=300000 --setenv=SLOPD_URL=http://127.0.0.1:1 --setenv=SLOPD_TOKEN=offline-door-check node {{quote(justfile_directory() / 'experiments/browser/harness/validate.mjs')}} {{quote(root)}} /workspace.html {{quote(justfile_directory() / 'experiments/browser/artifacts/workspace-doors-startup.json')}}

# Exercise native door menus through DOM input without starting playback.
browser-host-workspace-doors-check profile:
    systemd-run --user --pipe --wait --collect --setenv=PROBE_EVALUATE_TIMEOUT_MS=120000 node {{quote(justfile_directory() / 'experiments/browser/harness/evaluate.mjs')}} {{quote(profile)}} {{quote(justfile_directory() / 'experiments/browser/harness/workspace-doors-check.js')}}

# Dynamic font pipeline checks never start a game or browser.
[group('browser prototype')]
browser-fonts-format: mod-restore-tools
    dotnet tool run csharpier format experiments/browser/runtime/Adapters/BrowserFontBank.cs experiments/browser/runtime/Adapters/BrowserGui.cs experiments/browser/runtime/Adapters/BrowserPreferences.cs experiments/browser/runtime/BrowserWorkspace.cs experiments/browser/runtime/FontProbe/Program.cs experiments/browser/compat/Offline/EcoWorkspace.cs experiments/browser/compat/Offline/BrowserPreferencesPatch.cs experiments/browser/runtime/NativeTextTests/FontChecks.cs experiments/browser/runtime/NativeTextTests/Program.cs experiments/browser/runtime/PreferencesTests/Program.cs
    just slopweb-format

[group('browser prototype')]
browser-fonts-build:
    just --justfile experiments/browser/runtime/justfile build
    just --justfile experiments/browser/runtime/justfile adapters
    just --justfile experiments/browser/runtime/justfile font-probe-build

[group('browser prototype')]
browser-fonts-test:
    node --test experiments/browser/harness/fonts.test.mjs
    just --justfile experiments/browser/runtime/NativeTextTests/justfile check
