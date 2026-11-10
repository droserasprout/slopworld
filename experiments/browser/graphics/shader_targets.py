"""Test installed Slang's real WGSL path, isolated per shader/target in subprocesses."""

import ctypes as c
import json
import os
import resource
import subprocess
import sys
from pathlib import Path


def compile_shader(game: Path, relative: str, entry: str, language: int, stage: int, target: int):
    # ABI/enums: public BRUTAL Brutal.Slang/Generated/{Slang,Enumerations}.gen.cs.
    output_path = Path('out') / (Path(relative).name + ('.wgsl' if target == 28 else '.spv'))
    output_path.unlink(missing_ok=True)
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    library = c.CDLL(str(game / 'libslang.so'))

    def bind(name, restype, *argtypes):
        function = getattr(library, name)
        function.restype = restype
        function.argtypes = list(argtypes)
        return function

    create_session = bind('spCreateSession', c.c_void_p, c.c_char_p)
    destroy_session = bind('spDestroySession', None, c.c_void_p)
    create_request = bind('spCreateCompileRequest', c.c_void_p, c.c_void_p)
    destroy_request = bind('spDestroyCompileRequest', None, c.c_void_p)
    set_target = bind('spSetCodeGenTarget', None, c.c_void_p, c.c_int)
    set_pass_through = bind('spSetPassThrough', None, c.c_void_p, c.c_int)
    add_unit = bind('spAddTranslationUnit', c.c_int, c.c_void_p, c.c_int, c.c_char_p)
    add_file = bind('spAddTranslationUnitSourceFile', None, c.c_void_p, c.c_int, c.c_char_p)
    add_entry = bind('spAddEntryPoint', c.c_int, c.c_void_p, c.c_int, c.c_char_p, c.c_int)
    compile_request = bind('spCompile', c.c_int, c.c_void_p)
    diagnostic = bind('spGetDiagnosticOutput', c.c_char_p, c.c_void_p)
    get_code = bind('spGetEntryPointCode', c.c_void_p, c.c_void_p, c.c_int, c.POINTER(c.c_size_t))
    session = create_session(None)
    if not session:
        raise RuntimeError('spCreateSession returned null')
    request = None
    try:
        request = create_request(session)
        if not request:
            raise RuntimeError('spCreateCompileRequest returned null')
        set_target(request, target)
        # glslang provides the native GLSL->SPIR-V control; direct Slang WGSL is separate.
        if language == 3 and target == 6:
            set_pass_through(request, 3)
        unit = add_unit(request, language, b'probe')
        add_file(request, unit, os.fsencode(game / relative))
        point = add_entry(request, unit, entry.encode(), stage)
        result = compile_request(request)
        messages = (diagnostic(request) or b'').decode(errors='replace')
        size = c.c_size_t()
        pointer = get_code(request, point, c.byref(size)) if result >= 0 else None
        output = None
        if pointer and size.value:
            output = output_path
            output.write_bytes(c.string_at(pointer, size.value))
        return {
            'source': relative,
            'entry': entry,
            'target': 'wgsl' if target == 28 else 'spirv',
            'result': result,
            'bytes': size.value,
            'output': str(output) if output else None,
            'diagnostics': messages,
        }
    finally:
        if request:
            destroy_request(request)
        destroy_session(session)


if __name__ == '__main__':
    game = Path(sys.argv[1]).resolve()
    if len(sys.argv) > 2:
        print(json.dumps(compile_shader(game, sys.argv[2], sys.argv[3], *map(int, sys.argv[4:]))))
    else:
        Path('out').mkdir(exist_ok=True)
        results = []
        cases = [
            ('Rendering/Shaders/SceneDefault.vert.glsl', 'main', 3, 1),
            ('Rendering/Shaders/SceneDefault.frag.glsl', 'main', 3, 5),
            ('Rendering/Shaders/Compute/GpuProbe.comp.hlsl', 'BandwidthProbeCS', 2, 6),
        ]
        for source, entry, language, stage in cases:
            for target in (6, 28):
                try:
                    process = subprocess.run(
                        [sys.executable, __file__, str(game), source, entry, str(language), str(stage), str(target)],
                        check=False,
                        capture_output=True,
                        text=True,
                        timeout=60,
                        env={**os.environ, 'LD_LIBRARY_PATH': str(game)},
                    )
                    result = (
                        json.loads(process.stdout)
                        if process.returncode == 0
                        else {
                            'source': source,
                            'target': target,
                            'process_exit': process.returncode,
                            'stderr': process.stderr,
                        }
                    )
                except subprocess.TimeoutExpired:
                    result = {'source': source, 'target': target, 'timeout_seconds': 60}
                results.append(result)
                print(
                    json.dumps(
                        {key: value for key, value in result.items() if key != 'diagnostics'}
                        | {'diagnostic_preview': result.get('diagnostics', '')[:350]}
                    ),
                    flush=True,
                )
        Path('out/shader-targets.json').write_text(json.dumps(results, indent=2) + '\n')
