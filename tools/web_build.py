"""Coordinate timed redistributable builds; recipes retain individual tool ownership."""

import os
import subprocess
from concurrent.futures import ThreadPoolExecutor

from tools.web_timings import Timings

PROJECTS = ('Probe', 'Adapters', 'FontProbe', 'BackgroundProbe', 'Scanner', 'Offline')


def build_managed(timings: Timings) -> None:
    jobs = int(os.environ.get('SLOPWEB_BUILD_JOBS', str(min(6, os.cpu_count() or 1))))
    if jobs < 1:
        raise ValueError('SLOPWEB_BUILD_JOBS must be a positive integer')

    def build(project: str) -> None:
        path = (
            'slopweb/offline/Offline.csproj'
            if project == 'Offline'
            else (
                f'slopweb/runtime/{project}/{project}.csproj' if project != 'Probe' else 'slopweb/runtime/Probe.csproj'
            )
        )
        with timings.step(project):
            subprocess.run(
                ['dotnet', 'build', path, '--nologo', '-v', 'quiet', '-p:RestoreLockedMode=true'], check=True
            )

    # Each producer has its own bin/obj and no ProjectReference edges. Join every
    # writer, including on failure, before staging can read their outputs.
    with ThreadPoolExecutor(max_workers=min(jobs, len(PROJECTS))) as pool:
        list(pool.map(build, PROJECTS))


def main() -> None:
    timings = Timings()
    try:
        with timings.step('Rust host'):
            subprocess.run(['just', 'web-host-release'], check=True)
        with timings.step('Mod and dependencies'):
            subprocess.run(['just', 'mod-build', 'rimref'], check=True)
        with timings.step('Local font rasterizer (build/cache check)'):
            subprocess.run(['python3', 'slopweb/runtime/LocalFonts/build.py'], check=True)
        build_managed(timings)
        with timings.step('Stage redistributable bundle'):
            subprocess.run(['just', 'web-stage'], check=True)
    finally:
        timings.summary('Build summary')


if __name__ == '__main__':
    main()
