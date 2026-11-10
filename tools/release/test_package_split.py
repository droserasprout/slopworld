"""Protect independently installable products and complete native release sets."""

import shutil
import subprocess
from pathlib import Path
from typing import Any

import pytest

from tools import ROOT
from tools.release import arch
from tools.release import container
from tools.release import debian
from tools.release import fedora
from tools.release import staging


@pytest.mark.parametrize('distribution', ['arch', 'debian', 'fedora'])
def test_native_packages_have_disjoint_files_and_role_dependencies(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, distribution: str
) -> None:
    module = {'arch': arch, 'debian': debian, 'fedora': fedora}[distribution]
    machine = 'amd64' if distribution == 'debian' else 'x86_64'
    monkeypatch.setenv('SOURCE_DATE_EPOCH', '1234567890')
    binary_dir = tmp_path / 'bin'
    binary_dir.mkdir()
    owned: dict[str, set[str]] = {}
    metadata: dict[str, str] = {}

    def stage_mod(path: Path, revision: str, version: str) -> None:
        path.mkdir(parents=True)
        staging.metadata(path, revision, version)

    if distribution != 'arch':
        monkeypatch.setattr(module, 'stage_mod', stage_mod)
    monkeypatch.setattr(staging, 'stage_mod', stage_mod)

    def run(arguments: list[str], **kwargs: Any) -> subprocess.CompletedProcess[str]:
        if arguments[0] == 'dpkg-shlibdeps':
            assert [Path(arg).name for arg in arguments[2:]] == [product]
            return subprocess.CompletedProcess(arguments, 0, 'shlibs:Depends=libc6\n')
        if arguments[0] == 'dpkg-deb':
            payload = Path(arguments[-2])
            metadata[product] = (payload / 'DEBIAN/control').read_text()
            output = Path(arguments[-1])
        elif arguments[0] == 'rpmbuild':
            spec = Path(arguments[-1])
            metadata[product] = spec.read_text()
            payload = spec.parent.parent / 'SOURCES/payload'
            output = spec.parent.parent / f'RPMS/{product}-1.2.3-1.x86_64.rpm'
        elif 'makepkg' in arguments:
            workspace = Path(kwargs['cwd'])
            payload = workspace / 'payload'
            # Evaluate the actual selected metadata, without invoking makepkg or downloads.
            metadata[product] = subprocess.run(
                [
                    'bash',
                    '-euc',
                    'source "$1"; _package_metadata "$2"; printf "%s\\n" "${depends[@]}"',
                    'metadata',
                    str(ROOT / 'packaging/arch/packages.sh'),
                    product,
                ],
                check=True,
                capture_output=True,
                text=True,
            ).stdout
            output = workspace / f'{product}-1.2.3-1-x86_64.pkg.tar.zst'
        else:
            return subprocess.CompletedProcess(arguments, 0, '')
        assert [path.name for path in (payload / 'usr/bin').iterdir()] == [product]
        assert (payload / 'usr/share/slopworld/SlopWorld').exists() == (product == 'slopworld')
        unit = payload / 'usr/lib/systemd/user/slopd.service'
        assert unit.exists() == (product == 'slopd')
        if product == 'slopd':
            assert '/usr/bin/slopd' in unit.read_text()
            assert '%h/.local/bin/slopd' not in unit.read_text()
        for cli in ('slopworld', 'slopctl'):
            assert (payload / f'usr/share/bash-completion/completions/{cli}').exists() == (cli == product)
        owned[product] = {str(path.relative_to(payload)) for path in (payload / 'usr').rglob('*') if path.is_file()}
        assert any(product in path and path.endswith(('LICENSE', 'copyright')) for path in owned[product])
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_bytes(b'archive fixture')
        return subprocess.CompletedProcess(arguments, 0, '')

    monkeypatch.setattr(module, 'run', run)
    # Each package can stage with only its own executable available.
    for product in staging.NATIVE_PRODUCTS:
        binary = binary_dir / product
        binary.write_bytes(b'binary fixture')
        module.package(tmp_path / 'output', 'revision', '1.2.3', machine, binary_dir, product=product)
        binary.unlink()
    assert not (
        owned['slopworld'] & owned['slopd'] | owned['slopworld'] & owned['slopctl'] | owned['slopd'] & owned['slopctl']
    )
    assert 'slopctl' in metadata['slopd'] and 'tmux' in metadata['slopd']
    assert 'bubblewrap' not in metadata['slopworld']
    assert 'xclip' in metadata['slopworld']
    # Descriptions may mention a daemon, so check dependency lines rather than all metadata.
    cli_dependencies = [line for line in metadata['slopctl'].splitlines() if line.startswith(('Depends:', 'Requires:'))]
    assert not any(name in line for line in cli_dependencies for name in ('slopd', 'slopworld', 'tmux'))


@pytest.mark.parametrize('distribution', ['arch', 'debian', 'fedora'])
@pytest.mark.parametrize('missing', [None, 'slopctl'])
def test_native_set_compiles_once_and_validates_all_outputs_before_replacement(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, distribution: str, missing: str | None
) -> None:
    output = tmp_path / 'output'
    output.mkdir()
    for product in staging.NATIVE_PRODUCTS:
        _, name = container.archive_names(distribution, '1.0.0', product, 'amd64')
        (output / name).write_bytes(b'previous')
    calls = []

    def compile_packages(root: Path, distro: str, workspace: Path, revision: str, version: str, **kwargs: Any) -> None:
        calls.append(kwargs)
        for product in staging.NATIVE_PRODUCTS:
            if product != missing:
                generated, _ = container.archive_names(distro, version, product, 'amd64')
                (workspace / generated).write_bytes(b'new')

    monkeypatch.setattr(container, 'compile_package', compile_packages)
    if missing:
        with pytest.raises(ValueError, match='slopctl'):
            container.package_native(distribution, output, 'revision', '1.0.0')
    else:
        assert len(container.package_native(distribution, output, 'revision', '1.0.0')) == 3
    assert calls == [{'product': 'native', 'architecture': 'amd64'}]
    assert {path.read_bytes() for path in output.iterdir()} == ({b'previous'} if missing else {b'new'})


@pytest.mark.parametrize('product', ['slopd', 'slopctl'])
def test_real_arch_server_and_cli_archives(tmp_path: Path, product: str) -> None:
    if not all(shutil.which(tool) for tool in ('makepkg', 'pacman', 'fakeroot', 'bwrap', 'bsdtar')):
        pytest.skip('Arch packaging tools are required')
    binary_dir = tmp_path / 'bin'
    binary_dir.mkdir()
    shutil.copyfile('/bin/true', binary_dir / product)
    archive = arch.package(tmp_path / 'output', 'revision', '1.0.0', 'x86_64', binary_dir, product=product)
    contents = subprocess.run(['bsdtar', '-tf', str(archive)], check=True, capture_output=True, text=True).stdout
    assert f'usr/bin/{product}' in contents
    assert 'usr/share/slopworld/SlopWorld' not in contents
    assert ('usr/lib/systemd/user/slopd.service' in contents) == (product == 'slopd')
    metadata = subprocess.run(
        ['bsdtar', '-xOf', str(archive), '.PKGINFO'], check=True, capture_output=True, text=True
    ).stdout
    assert ('depend = slopctl\n' in metadata) == (product == 'slopd')
    assert ('depend = tmux>=3.7\n' in metadata) == (product == 'slopd')


@pytest.mark.parametrize('variant,suffix', [('local', ''), ('git', '-git')])
def test_arch_source_metadata_exposes_each_products_dependencies(tmp_path: Path, variant: str, suffix: str) -> None:
    if not shutil.which('makepkg'):
        pytest.skip('makepkg is required')
    for name in (f'PKGBUILD.{variant}', 'packages.sh', 'slopworld.install'):
        shutil.copyfile(ROOT / 'packaging/arch' / name, tmp_path / name)
    result = subprocess.run(
        ['makepkg', '-p', f'PKGBUILD.{variant}', '--printsrcinfo'],
        cwd=tmp_path,
        check=True,
        capture_output=True,
        text=True,
    ).stdout
    sections = {section.splitlines()[0]: section for section in result.split('pkgname = ')[1:]}
    assert set(sections) == {product + suffix for product in staging.NATIVE_PRODUCTS}
    assert 'depends = slopctl' + suffix in sections['slopd' + suffix]
    assert 'depends = xclip' in sections['slopworld' + suffix]
    assert 'depends = tmux>=3.7' in sections['slopd' + suffix]
    assert 'depends = slopd' not in sections['slopctl' + suffix]
