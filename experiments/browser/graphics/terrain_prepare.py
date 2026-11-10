"""Adapt the installed terrain shaders; preserve their texture/edge formulas."""

import re

from prepare import replace_once


def adapt_terrain(source: str, stage: str) -> str:
    source = replace_once(source, r'^#version 450', '#version 300 es\nprecision highp float;\nprecision highp int;')
    if stage == 'vert':
        source = replace_once(source, r'layout\(push_constant\) uniform Push', 'layout(std140) uniform Push')
        source = replace_once(
            source, r'layout\(set = 0, binding = 0\) uniform VertexUniforms', 'layout(std140) uniform VertexUniforms'
        )
        source = source.replace(
            'layout(set = 2, binding = 0) uniform MaterialUniforms', 'layout(std140) uniform MaterialUniforms'
        )
        source = re.sub(r'layout\(location = \d+\) (?=(?:flat )?out)', '', source)
        # Installed UnityStubs.Ortho uses flipped Y and reversed Vulkan depth.
        # Undo Y for WebGL and map its 0..W depth interval into -W..W.
        source = replace_once(
            source,
            r'gl_Position = uVertex.ViewProj \* push.modelMatrix \* vec4\((inPosition|pos), 1.0\);',
            r'gl_Position = uVertex.ViewProj * push.modelMatrix * vec4(\1, 1.0);' + '\n'
            '    gl_Position.y = -gl_Position.y;\n'
            '    gl_Position.z = 2.0 * gl_Position.z - gl_Position.w;',
        )
    elif stage == 'frag':
        source = replace_once(
            source,
            r'layout\(set = 2, binding = 0\) uniform MaterialUniforms',
            'layout(std140) uniform MaterialUniforms',
        )
        if 'sampler2D' in source:
            source = replace_once(source, r'layout\(set = 2, binding = 1\) uniform sampler2D', 'uniform sampler2D')
        source = source.replace('layout(set = 2, binding = 2) uniform sampler2D', 'uniform sampler2D')
        source = re.sub(r'layout\(location = \d+\) (?=(?:flat )?in)', '', source)
    else:
        raise ValueError('Only terrain vertex/fragment stages are supported')
    if re.search(r'push_constant|layout\([^)]*\b(set|binding)\s*=|#include', source):
        raise ValueError('Unsupported terrain shader resource or include')
    return source
