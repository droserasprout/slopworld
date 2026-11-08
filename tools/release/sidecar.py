"""Build independent sidecar distro packages; publish owns the final bundle."""

import argparse
import os
import tempfile
from pathlib import Path

from tools.release import container
from tools.release.arch import arch_version
from tools.utils import run_main

ARCHITECTURES = {'amd64': 'x86_64', 'arm64': 'aarch64'}


def asset_names(version: str, architecture: str) -> list[str]:
    arch_version(version)
    machine = ARCHITECTURES[architecture]
    return [
        *([f'slopcar-{version}-{machine}.pkg.tar.zst'] if architecture == 'amd64' else []),
        f'slopcar-{version}-{architecture}.deb',
        f'slopcar-{version}-{machine}.rpm',
    ]


def build(output: Path, revision: str, version: str, architecture: str) -> list[Path]:
    names = asset_names(version, architecture)
    output = output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='.sidecar-release-', dir=output) as temporary:
        workspace = Path(temporary)
        for distribution in ('arch', 'debian', 'fedora'):
            if distribution == 'arch' and architecture != 'amd64':
                continue
            container.package(distribution, workspace, revision, version, product='slopcar', architecture=architecture)
        for name in names:
            (workspace / name).replace(output / name)
    return [output / name for name in names]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('architecture', choices=ARCHITECTURES)
    args = parser.parse_args()
    # Use the same clean-checkout/coordinator identity gate as the final package job.
    from tools.release import ci

    release = ci.identity(preview=os.environ.get('RELEASE_MODE') == 'preview')
    output = Path(os.environ.get('SIDECAR_RELEASE_DIR', 'dist/sidecar'))
    build(output, release.revision, release.version, args.architecture)
    ci.validate(release)


if __name__ == '__main__':
    run_main(main)
