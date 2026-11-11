"""Own release container execution and atomic artifact handoff.

Native packagers own version conversion, payload staging, and archive formats.
"""

import os
import shlex
import tempfile
from pathlib import Path

from tools import ROOT
from tools.release import staging
from tools.release.arch import arch_version
from tools.release.debian import debian_version
from tools.release.fedora import rpm_version
from tools.utils import command
from tools.utils import run


def package(
    distribution: str,
    output: Path,
    revision: str,
    version: str,
    *,
    product: str = 'slopworld',
    architecture: str = 'amd64',
) -> Path:
    return package_products(distribution, output, revision, version, product=product, architecture=architecture)[0]


def package_native(distribution: str, output: Path, revision: str, version: str) -> list[Path]:
    """Build once per distribution, then publish the three native products together."""
    return package_products(distribution, output, revision, version, product='native')


def archive_names(distribution: str, version: str, product: str, architecture: str) -> tuple[str, str]:
    machine = 'x86_64' if architecture == 'amd64' else 'aarch64'
    if distribution == 'arch':
        if architecture != 'amd64':
            raise ValueError('Arch packages require amd64')
        arch_version(version)
        name = f'{product}-{version}-{machine}.pkg.tar.zst'
        return name, name
    if distribution == 'debian':
        return f'{product}_{debian_version(version)}_{architecture}.deb', f'{product}-{version}-{architecture}.deb'
    if distribution == 'fedora':
        return f'{product}-{rpm_version(version)}-1.{machine}.rpm', f'{product}-{version}-{machine}.rpm'
    raise ValueError(f'Unsupported distribution: {distribution}')


def package_products(
    distribution: str, output: Path, revision: str, version: str, *, product: str, architecture: str = 'amd64'
) -> list[Path]:
    if product not in ('native', *staging.PRODUCTS) or architecture not in ('amd64', 'arm64'):
        raise ValueError('Unsupported product or architecture')
    products = staging.NATIVE_PRODUCTS if product == 'native' else (product,)
    names = [archive_names(distribution, version, name, architecture) for name in products]
    output = output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    # Fresh output prevents stale package reuse; the same filesystem allows atomic rename.
    with tempfile.TemporaryDirectory(prefix=f'.{distribution}-container-', dir=output) as temporary:
        workspace = Path(temporary)
        # Docker preserves the host runner's UID on /output. Arch's Bubblewrap
        # maps container root to an ordinary UID, which must traverse this mount
        # to reach its own writable staging directory. TemporaryDirectory is 0700.
        workspace.chmod(0o755)
        compile_package(ROOT, distribution, workspace, revision, version, product=product, architecture=architecture)
        # Validate the whole set before replacing any previous artifact.
        for generated_name, _ in names:
            generated = workspace / generated_name
            if not generated.is_file() or generated.stat().st_size == 0:
                raise ValueError(f'Missing or empty container package: {generated_name}')
        archives = []
        for generated_name, archive_name in names:
            archive = output / archive_name
            (workspace / generated_name).replace(archive)
            archives.append(archive)
    return archives


def compile_package(
    root: Path,
    distribution: str,
    output: Path,
    revision: str,
    version: str,
    *,
    product: str = 'slopworld',
    architecture: str = 'amd64',
) -> None:
    container = command('CONTAINER', 'podman')
    suffix = '' if architecture == 'amd64' else '-arm64'
    image = f'slopworld-{distribution}{suffix}-release'
    run(
        [
            *container,
            'build',
            '--platform',
            f'linux/{architecture}',
            *shlex.split(os.environ.get('CONTAINER_BUILD_ARGS', '')),
            '-t',
            image,
            '-f',
            str(root / f'packaging/{distribution}/Dockerfile'),
            str(root / f'packaging/{distribution}'),
        ]
    )
    cache = root / f'dist/{distribution}{suffix}-build'
    mounts = []
    for name, destination in (
        ('target', '/build/target'),
        ('registry', '/usr/local/cargo/registry'),
        ('git', '/usr/local/cargo/git'),
    ):
        directory = cache / name
        directory.mkdir(parents=True, exist_ok=True)
        mounts.extend(['--volume', f'{directory}:{destination}'])
    common = Path(
        run(['git', 'rev-parse', '--git-common-dir'], cwd=root, capture_output=True, text=True).stdout.strip()
    )
    if not common.is_absolute():
        common = (root / common).resolve()
    if not common.is_relative_to(root):
        mounts.extend(['--volume', f'{common}:{common}:ro'])
    # The browser bundle is built once on the host. The checkout mount is
    # read-only, and distro packagers only consume this redistributable stage.
    binary_dir = shlex.quote(str(root / 'slopweb/out')) if product == 'slopweb' else '/build/target/release'
    compile_command = (
        ''
        if product == 'slopweb'
        else f'cargo build --manifest-path {"slopcar" if product == "slopcar" else "slopd"}/Cargo.toml --release --locked; '
    )
    run(
        [
            *container,
            'run',
            '--platform',
            f'linux/{architecture}',
            *shlex.split(os.environ.get('CONTAINER_RUN_ARGS', '')),
            '--rm',
            # Arch's root makepkg wrapper needs nested Bubblewrap namespaces.
            *(['--privileged'] if distribution == 'arch' else []),
            # Allow SELinux access without relabeling repositories or worktree metadata.
            '--security-opt',
            'label=disable',
            '--volume',
            f'{root}:{root}:ro',
            '--volume',
            f'{output}:/output',
            *mounts,
            '--workdir',
            str(root),
            '--env',
            f'SLOPWORLD_BUILD_VERSION={version}',
            '--env',
            'CARGO_TARGET_DIR=/build/target',
            # Container and checkout owners may differ. Trust only this checkout.
            '--env',
            'GIT_CONFIG_COUNT=1',
            '--env',
            'GIT_CONFIG_KEY_0=safe.directory',
            '--env',
            f'GIT_CONFIG_VALUE_0={root}',
            image,
            'bash',
            '-euc',
            (
                compile_command
                + f'python3 -m pytest tools/release/test_{distribution}.py tools/release/test_sidecar.py -q -p no:cacheprovider; '
                f'python3 -m tools.release.native {distribution} --product {product} --stage-only --binary-dir {binary_dir} '
                '--output /output --revision "$1" --version "$2"'
            ),
            f'release-{distribution}',
            revision,
            version,
        ]
    )
