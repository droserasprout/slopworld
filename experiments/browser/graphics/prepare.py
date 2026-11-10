"""Derive a narrow WebGL2 shader adapter from the user's installed game."""

import hashlib
import json
import re
import shutil
import sys
from pathlib import Path


def replace_once(source: str, pattern: str, replacement: str) -> str:
    result, count = re.subn(pattern, replacement, source)
    if count != 1:
        raise ValueError(f'Expected one supported shader construct: {pattern!r}; got {count}')
    return result


def adapt(source: str, stage: str) -> str:
    """Adapt SceneDefault only; this is deliberately not a general GLSL compiler."""
    if stage not in ('vert', 'frag'):
        raise ValueError('Only vert and frag are supported')
    source = replace_once(source, r'^#version 450', '#version 300 es\nprecision highp float;\nprecision highp int;')
    if stage == 'vert':
        source = replace_once(source, r'layout\(push_constant\) uniform Push', 'layout(std140) uniform Push')
        source = replace_once(
            source, r'layout\(set = 0, binding = 0\) uniform VertexUniforms', 'layout(std140) uniform VertexUniforms'
        )
        source = replace_once(
            source,
            r'gl_Position = uVertex.ViewProj \* world;',
            'gl_Position = uVertex.ViewProj * world;\n    gl_Position.z = 2.0 * gl_Position.z - gl_Position.w;',
        )
        source = re.sub(r'layout\(location = \d+\) out', 'out', source)
    else:
        source = replace_once(
            source,
            r'layout\(set = 2, binding = 0\) uniform MaterialUniforms',
            'layout(std140) uniform MaterialUniforms',
        )
        source = replace_once(source, r'layout\(set = 2, binding = 1\) uniform sampler2D', 'uniform sampler2D')
        source = re.sub(r'layout\(location = \d+\) in', 'in', source)
    if re.search(r'push_constant|layout\([^)]*\b(set|binding)\s*=', source):
        raise ValueError('Unsupported Vulkan resource layout remains')
    return source


def prepare(game: Path, output: Path) -> None:
    from terrain_prepare import adapt_terrain

    shader_root = game / 'Rendering/Shaders'
    manifest = {
        'scope': 'Diagnostic and partial colony shader adapters; not a complete Waylith renderer',
        'shaders': {},
    }
    output.mkdir(parents=True, exist_ok=True)
    for stage in ('vert', 'frag'):
        path = shader_root / f'SceneDefault.{stage}.glsl'
        raw = path.read_bytes()
        (output / f'SceneDefault.{stage}.glsl').write_bytes(raw)
        adapted = adapt(raw.decode(), stage)
        (output / f'scene.{stage}.glsl').write_text(adapted)
        manifest['shaders'][stage] = {
            'source': str(path.relative_to(game)),
            'sha256': hashlib.sha256(raw).hexdigest(),
        }
    manifest['terrain_shaders'] = {}
    sprite_vertex = adapt((shader_root / 'SceneDefault.vert.glsl').read_text(), 'vert')
    sprite_vertex = sprite_vertex.replace('gl_Position.z =', 'gl_Position.y = -gl_Position.y;\n    gl_Position.z =')
    (output / 'Object.vert.glsl').write_text(sprite_vertex)
    manifest['object_shaders'] = {}
    for name in (
        'Cutout',
        'CutoutComplex',
        'Transparent',
        'CutoutPlant',
        'CutoutSkin',
        'CutoutHair',
        'LightOverlay',
        'EdgeShadow',
        'FogOfWar',
    ):
        path = game / 'Data/Core/Shaders/Map' / f'{name}.frag.glsl'
        raw = path.read_text()
        manifest['object_shaders'][name] = {
            'source': str(path.relative_to(game)),
            'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
        }
        raw = re.sub(r'#ifdef INSTANCED\s*#include "InstanceTransforms.glsl"\s*#endif', '', raw)
        raw = raw.replace('#include "MaskGate.glsl"', (game / 'Data/Core/Shaders/MaskGate.glsl').read_text())
        (output / f'{name}.frag.glsl').write_text(adapt_terrain(raw, 'frag'))
    plant = game / 'Data/Core/Shaders/Map/PlantWindSway.vert.glsl'
    plant_source = plant.read_text()
    fragment_source = (game / 'Data/Core/Shaders/Map/CutoutPlant.frag.glsl').read_text()
    block = r'layout\(set = 2, binding = 0\) uniform MaterialUniforms\s*\{.*?\} uMaterial;'
    # WebGL requires matching block declarations in both stages. The vertex
    # shader's prefix retains its original offsets in the complete game block.
    plant_source = re.sub(block, re.search(block, fragment_source, re.DOTALL).group(), plant_source, flags=re.DOTALL)
    (output / 'PlantWindSway.vert.glsl').write_text(adapt_terrain(plant_source, 'vert'))
    for path in (plant, game / 'Data/Core/Shaders/MaskGate.glsl', shader_root / 'SceneDefault.vert.glsl'):
        manifest['object_shaders'][path.name] = {
            'source': str(path.relative_to(game)),
            'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
        }
    for name in (
        'MapWorldUV.vert.glsl',
        'TerrainHard.frag.glsl',
        'TerrainFade.frag.glsl',
        'TerrainFadeRough.frag.glsl',
    ):
        path = game / 'Data/Core/Shaders/Map' / name
        raw = path.read_bytes()
        (output / name).write_text(adapt_terrain(raw.decode(), name.split('.')[1]))
        manifest['terrain_shaders'][name] = {
            'source': str(path.relative_to(game)),
            'sha256': hashlib.sha256(raw).hexdigest(),
        }
    files = list((game / 'Rendering/Shaders').rglob('*.glsl')) + list((game / 'Data/Core/Shaders').rglob('*.glsl'))
    files += list((game / 'Rendering/Shaders').rglob('*.hlsl'))
    features = {
        'push_constants': r'push_constant',
        'descriptor_sets': r'layout\([^)]*set\s*=',
        'storage_buffers': r'\bbuffer\s+\w+\s*\{',
        'compute_local_size': r'local_size_|\[numthreads',
        'includes': r'#include',
        'texture_arrays': r'sampler2DArray|Texture2DArray',
    }
    manifest['shader_inventory'] = {
        'file_count': len(files),
        'features': {
            name: [str(p.relative_to(game)) for p in files if re.search(pattern, p.read_text(errors='replace'))]
            for name, pattern in features.items()
        },
    }
    # A shipped PNG exercises browser decoding without implementing Waylith asset-pack I/O.
    texture = game / 'Data/Core/Languages/English/LangIcon.png'
    raw = texture.read_bytes()
    shutil.copyfile(texture, output / 'local-texture.png')
    manifest['texture'] = {
        'source': str(texture.relative_to(game)),
        'file': 'local-texture.png',
        'sha256': hashlib.sha256(raw).hexdigest(),
    }
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    for name in (
        'index.html',
        'renderer.js',
        'probe.js',
        'terrain-renderer.js',
        'terrain-probe.html',
        'terrain-probe.js',
    ):
        shutil.copyfile(Path(__file__).parent / name, output / name)
    print(f'Prepared {output}/index.html; locally derived game shaders stay in ignored out/')


if __name__ == '__main__':
    prepare(Path(sys.argv[1]).expanduser(), Path(__file__).parent / 'out')
