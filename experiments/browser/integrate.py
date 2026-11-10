"""Assemble only local outputs; never package or publish proprietary input files."""

import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parent
output = ROOT / 'artifacts' / 'site'
if output.exists():
    shutil.rmtree(output)
shutil.copytree(ROOT / 'runtime/out/wwwroot', output)
graphics = output / 'graphics'
graphics.mkdir()
for name in ('renderer.js', 'scene.vert.glsl', 'scene.frag.glsl', 'manifest.json'):
    shutil.copyfile(ROOT / 'graphics/out' / name, graphics / name)
for file in (ROOT / 'app').iterdir():
    shutil.copyfile(file, output / file.name)
print(f'Local-only prototype: {output}')
