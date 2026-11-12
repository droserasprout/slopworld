"""Validate native portrait readback with a copied save and no daemon connectivity."""

import hashlib
import json
import math
import os
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT))

from slopweb.portraits import request_snapshot
from tools.release.source_mod import stage


def main():
    scale = float(sys.argv[1]) if len(sys.argv) > 1 else 2.0
    if not math.isfinite(scale) or not 0.5 <= scale <= 4:
        raise ValueError('Expected UI scale between 0.5 and 4')
    game = Path.home() / '.local/share/Steam/steamapps/common/RimWorld'
    original = Path.home() / '.local/share/slopworld/profiles/local'
    saves = list((original / 'Saves').glob('*.rws'))
    if not saves:
        raise RuntimeError('No existing save; refusing automatic new-colony creation')
    save = max(saves, key=lambda path: path.stat().st_mtime_ns)
    output = ROOT / 'slopweb/runtime/out'
    root = Path(tempfile.mkdtemp(prefix='native-portraits-', dir=output))
    profile = root / 'profile'
    (profile / 'Saves').mkdir(parents=True)
    (profile / 'Config').mkdir()
    shutil.copy2(save, profile / 'Saves' / save.name)
    for name in ('Prefs.xml', 'ModsConfig.xml', 'SlopWorld.toml'):
        shutil.copy2(original / 'Config' / name, profile / 'Config' / name)
    # Fix the effective scale independently of Xvfb's desktop DPI inference.
    # Only the disposable profile changes; the live preferences stay untouched.
    prefs_path = profile / 'Config/Prefs.xml'
    prefs = ET.parse(prefs_path)
    for name, value in (('scaleRelativeToSystem', 'False'), ('uiScale', str(scale))):
        element = prefs.getroot().find(name)
        if element is None:
            element = ET.SubElement(prefs.getroot(), name)
        element.text = value
    prefs.write(prefs_path, encoding='utf-8', xml_declaration=True)
    (profile / 'slopworld.profile').touch()
    stage(root / 'mod')
    for name in ('cache', 'config', 'data', 'tmp'):
        (root / name).mkdir()
    endpoint = root / 'config/offline.toml'
    endpoint.write_text('url = "http://127.0.0.1:1"\ntoken = ""\n')
    environment = {
        key: value
        for key, value in os.environ.items()
        if not key.startswith(('SLOPD_', 'SLOPWORLD_'))
        and key not in ('DBUS_SESSION_BUS_ADDRESS', 'WAYLAND_DISPLAY', 'DISPLAY', 'XAUTHORITY')
    }
    environment.update(
        {
            'SLOPD_CONFIG_ROOT': str(root / 'config'),
            'SLOPD_DATA': str(root / 'data'),
            'SLOPD_CACHE': str(root / 'cache'),
            'SLOPD_ENDPOINT': str(endpoint),
            'XDG_CACHE_HOME': str(root / 'cache'),
            'TMPDIR': str(root / 'tmp'),
            'FORCE_X11': '1',
            'ALSOFT_DRIVERS': 'null',
            # Xvfb cannot present through the host GPU's DRI3 queue. Use the locally
            # installed software Vulkan driver, under the diagnostic CPU quota.
            'VK_DRIVER_FILES': '/usr/lib/chromium/vk_swiftshader_icd.json',
        }
    )
    # The installed game and live profile remain read-only. Only this private
    # output tree is writable; the replacement mod exists only in this mount view.
    # A separate network namespace prevents even fallback endpoint access.
    command = [
        'xvfb-run',
        '-a',
        '-s',
        '-screen 0 3072x1664x24 -dpi 192 -nolisten tcp',
        'bwrap',
        '--die-with-parent',
        '--unshare-net',
        '--unshare-pid',
        '--ro-bind',
        '/',
        '/',
        '--dev-bind',
        '/dev',
        '/dev',
        '--proc',
        '/proc',
        '--bind',
        str(root),
        str(root),
        '--bind',
        str(root / 'tmp'),
        '/tmp',
        '--ro-bind',
        '/tmp/.X11-unix',
        '/tmp/.X11-unix',
        '--ro-bind',
        str(root / 'mod'),
        str(game / 'Mods/SlopWorld'),
        '--chdir',
        str(game),
        '--',
        str(game / 'RimWorld'),
        f'-savedatafolder={profile}',
        '-NoFileLog',
    ]
    print(json.dumps({'output': str(root), 'save': save.name}), flush=True)
    source = root / 'cache/workspace-portraits' / hashlib.sha256(str(profile).encode()).hexdigest()
    with (root / 'native.log').open('wb') as log:
        process = subprocess.Popen(
            command, env=environment, stdout=log, stderr=subprocess.STDOUT, start_new_session=True
        )
        try:
            deadline = time.monotonic() + 180
            while not (source / 'available').exists():
                if process.poll() is not None:
                    raise RuntimeError(f'Native process exited {process.returncode}; inspect {root}/native.log')
                if time.monotonic() >= deadline:
                    raise TimeoutError(f'Native exporter did not become ready; inspect {root}/native.log')
                time.sleep(1)
            snapshot = request_snapshot(source, timeout=60)
            inventory = json.loads(snapshot.manifest)
            if abs(inventory['uiScale'] - scale) > 0.00001:
                raise RuntimeError(f'Native export scale {inventory["uiScale"]} differs from requested {scale}')
            if not inventory['entries']:
                raise RuntimeError('Copied colony exported no agent identities')
            # A failed native readback can return an allocated, all-zero buffer.
            # Require actual color variation and visible pixels, not just valid headers.
            for name, pixels in snapshot.files.items():
                if not any(pixels[11::4]) or len(set(pixels[8:])) < 3:
                    raise RuntimeError(f'Empty or uniform native readback: {name}')
            snapshot.write(root / 'browser')
            report = {
                'entries': len(inventory['entries']),
                'textures': len(snapshot.files),
                'uiScale': inventory['uiScale'],
                'snapshot': str(source),
                'identity': snapshot.identity,
                'networkIsolated': True,
            }
            (root / 'report.json').write_text(json.dumps(report, indent=2) + '\n')
            print(json.dumps(report), flush=True)
        finally:
            # Terminate only this test's process group, including its X server.
            if process.poll() is None:
                os.killpg(process.pid, signal.SIGTERM)
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    os.killpg(process.pid, signal.SIGKILL)
                    process.wait(timeout=10)


if __name__ == '__main__':
    main()
