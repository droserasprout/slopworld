"""Coordinate timed redistributable builds; recipes retain individual tool ownership."""

import subprocess

from tools.web_timings import Timings


def main() -> None:
    timings = Timings()
    try:
        with timings.step('Rust host'):
            subprocess.run(['just', 'web-host-release'], check=True)
        with timings.step('Mod and dependencies'):
            subprocess.run(['just', 'mod-build', 'rimref'], check=True)
        with timings.step('Local font rasterizer (build/cache check)'):
            subprocess.run(['python3', 'slopweb/runtime/LocalFonts/build.py'], check=True)
        for project in ('Probe', 'Adapters', 'FontProbe', 'BackgroundProbe', 'Scanner', 'Offline'):
            path = (
                'slopweb/offline/Offline.csproj'
                if project == 'Offline'
                else (
                    f'slopweb/runtime/{project}/{project}.csproj'
                    if project != 'Probe'
                    else 'slopweb/runtime/Probe.csproj'
                )
            )
            with timings.step(project):
                subprocess.run(
                    ['dotnet', 'build', path, '--nologo', '-v', 'quiet', '-p:RestoreLockedMode=true'], check=True
                )
        with timings.step('Stage redistributable bundle'):
            subprocess.run(['just', 'web-stage'], check=True)
    finally:
        timings.summary('Build summary')


if __name__ == '__main__':
    main()
