"""Standalone release installer; uses only Python's standard library.

Package managers own dependency resolution and system changes. Release packaging
copies this file to install.py, so it must not import checkout modules.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import platform
import re
import shlex
import shutil
import subprocess
import sys
import tempfile
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from curses import window

REPOSITORY = 'droserasprout/slopworld'
COMPONENTS = {
    'slopworld': 'Game launcher, mod and assets',
    'slopd': 'Native agent daemon (requires slopctl)',
    'slopctl': 'Command-line client',
    'slopcar': 'Docker sidecar launcher',
    'slopweb': 'Browser workspace (requires a local RimWorld installation)',
}
MANAGERS = {'debian': 'apt', 'fedora': 'dnf', 'arch': 'pacman'}


@dataclass(frozen=True)
class Host:
    distribution: str
    architecture: str
    name: str

    def asset(self, component: str, version: str) -> str:
        machine = 'x86_64' if self.architecture == 'amd64' else 'aarch64'
        suffix = {'debian': f'{self.architecture}.deb', 'fedora': f'{machine}.rpm', 'arch': f'{machine}.pkg.tar.zst'}
        return f'{component}-{version}-{suffix[self.distribution]}'

    def components(self) -> list[str]:
        if self.architecture == 'amd64':
            return list(COMPONENTS)
        return ['slopcar'] if self.distribution != 'arch' else []


def detect_host() -> Host:
    if platform.system() != 'Linux':
        raise ValueError('Only Linux is supported.')
    release = platform.freedesktop_os_release()
    identifier = release.get('ID', '')
    families = {'debian': 'debian', 'ubuntu': 'debian', 'fedora': 'fedora', 'arch': 'arch'}
    # Prefer declared ancestry when several package managers are installed.
    identities = [identifier, *release.get('ID_LIKE', '').split()]
    distribution = next((families[name] for name in identities if name in families), None)
    if distribution is None:
        available = [family for family, manager in MANAGERS.items() if shutil.which(manager)]
        if len(available) != 1:
            raise ValueError('Cannot determine package manager: expected exactly one of apt, dnf, or pacman.')
        distribution = available[0]
    architecture = {'x86_64': 'amd64', 'aarch64': 'arm64', 'arm64': 'arm64'}.get(platform.machine())
    if architecture is None:
        raise ValueError(f'Unsupported architecture: {platform.machine()}')
    host = Host(distribution, architecture, release.get('PRETTY_NAME', identifier))
    if not host.components():
        raise ValueError('No release packages are available for this OS/architecture.')
    for executable in (
        MANAGERS[distribution],
        {'debian': 'dpkg-query', 'fedora': 'rpm', 'arch': 'pacman'}[distribution],
    ):
        if shutil.which(executable) is None:
            raise ValueError(f'Required command is missing: {executable}')
    return host


def installed_versions(host: Host) -> dict[str, str]:
    versions = {}
    for component in COMPONENTS:
        commands = {
            'debian': ['dpkg-query', '-W', '-f=${db:Status-Status}\t${Version}', component],
            'fedora': ['rpm', '-q', '--qf', '%{VERSION}-%{RELEASE}', component],
            'arch': ['pacman', '-Q', component],
        }
        result = subprocess.run(commands[host.distribution], capture_output=True, text=True, check=False)
        if result.returncode == 1:
            continue
        result.check_returncode()
        value = result.stdout.strip()
        if host.distribution == 'debian':
            status, value = value.split('\t', 1)
            if status != 'installed':
                continue
        elif host.distribution == 'arch':
            value = value.split(' ', 1)[1]
        versions[component] = value
    return versions


def release_version(requested: str | None) -> str:
    if requested is None:
        request = urllib.request.Request(
            f'https://api.github.com/repos/{REPOSITORY}/releases/latest',
            headers={'Accept': 'application/vnd.github+json', 'User-Agent': 'slopworld-installer'},
        )
        with urllib.request.urlopen(request, timeout=60) as response:
            requested = str(json.load(response)['tag_name'])
    version = requested.removeprefix('v')
    if not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+', version):
        raise ValueError('Expected a stable release version, such as 1.2.3 or v1.2.3.')
    return version


def component_action(component: str, selected: set[str], installed: dict[str, str]) -> str:
    if component in installed:
        action = 'install/update' if component in selected else 'REMOVE'
        return f'{action} (installed {installed[component]})'
    return 'install' if component in selected else 'not installed'


@dataclass(frozen=True)
class Plan:
    install: list[str]
    remove: list[str]


def plan_changes(host: Host, installed: dict[str, str], install: list[str], remove: list[str]) -> Plan:
    wanted, unwanted = set(install), set(remove)
    if wanted & unwanted:
        raise ValueError('A component cannot be both installed and removed.')
    if 'slopd' in wanted:
        if 'slopctl' in unwanted:
            raise ValueError('slopd requires slopctl; cannot remove its CLI.')
        wanted.add('slopctl')
    if wanted - set(host.components()):
        raise ValueError('Selected components have no release package for this architecture.')
    remaining = (set(installed) | wanted) - unwanted
    if 'slopd' in remaining and 'slopctl' in unwanted:
        raise ValueError('Remove slopd too before removing its required slopctl.')
    return Plan(
        [name for name in COMPONENTS if name in wanted],
        [name for name in COMPONENTS if name in unwanted and name in installed],
    )


def toggle_component(selected: set[str], component: str) -> None:
    if component == 'slopctl' and 'slopd' in selected:
        return
    if component in selected:
        selected.remove(component)
    else:
        selected.add(component)
    if 'slopd' in selected:
        selected.add('slopctl')


def select_components(host: Host, installed: dict[str, str], version: str) -> list[str] | None:
    if sys.stdin.isatty() and sys.stdout.isatty():
        try:
            import curses
        except ImportError:
            pass
        else:
            try:
                # wrapper restores terminal modes before downloads or errors.
                return curses.wrapper(select_terminal, host, installed, version)
            except curses.error:
                pass
    return select_numbered(host, installed, version)


def select_terminal(screen: window, host: Host, installed: dict[str, str], version: str) -> list[str] | None:
    import curses

    available = host.components()
    selected = set(installed).intersection(available)
    if 'slopd' in selected:
        selected.add('slopctl')
    cursor = 0
    screen.keypad(True)
    try:
        curses.curs_set(0)
    except curses.error:
        pass
    while True:
        screen.erase()
        height, width = screen.getmaxyx()
        lines = [f'{host.name} ({host.architecture}) - release {version}', '']
        for component in available:
            mark = 'x' if component in selected else ' '
            state = component_action(component, selected, installed)
            lines.append(f'[{mark}] {component}: {state} - {COMPONENTS[component]}')
        lines.extend(
            [
                '',
                'Up/Down: move  Space: toggle  Enter: apply  Q/Esc: cancel',
                'Unchecking installed components REMOVES them. slopd requires slopctl.',
            ]
        )
        for component in installed.keys() - set(available):
            lines.append(f'{component}: installed {installed[component]}; unavailable on this architecture')
        # Require enough room to keep every checkbox visible. Resize events redraw
        # without losing selection; tiny terminals still allow cancellation.
        fits = height >= len(lines) + 1 and width >= 40
        visible = lines if fits else ['Terminal too small; resize or press Q/Esc to cancel.']
        for row, line in enumerate(visible[: max(0, height - 1)]):
            if width > 1:
                screen.addnstr(row, 0, line, width - 1, curses.A_REVERSE if fits and row == cursor + 2 else 0)
        screen.refresh()
        key = screen.getch()
        if key in (27, ord('q'), ord('Q')):
            return None
        if not fits:
            continue
        if key == curses.KEY_UP:
            cursor = (cursor - 1) % len(available)
        elif key == curses.KEY_DOWN:
            cursor = (cursor + 1) % len(available)
        elif key == ord(' '):
            toggle_component(selected, available[cursor])
        elif key in (10, 13, curses.KEY_ENTER):
            return [component for component in available if component in selected]


def select_numbered(host: Host, installed: dict[str, str], version: str) -> list[str] | None:
    available = host.components()
    selected = set(installed).intersection(available)
    while True:
        if 'slopd' in selected:
            selected.add('slopctl')
        print(f'\n{host.name} ({host.architecture}) — release {version}')
        for index, component in enumerate(available, 1):
            mark = 'x' if component in selected else ' '
            state = component_action(component, selected, installed)
            print(f' {index}. [{mark}] {component:10} {COMPONENTS[component]} — {state}')
        for component in installed.keys() - set(available):
            print(f'     [-] {component}: installed {installed[component]}; no release package for this architecture')
        print('Enter component numbers to toggle; Enter to apply; q to cancel.')
        print('Unchecking installed components REMOVES them. Uncheck slopd before slopctl.')
        answer = input('> ').strip()
        if answer.lower() == 'q':
            return None
        if not answer:
            return [component for component in available if component in selected]
        numbers = answer.split()
        if not all(number.isdecimal() and 1 <= int(number) <= len(available) for number in numbers):
            print('Choose numbers from the list, separated by spaces.')
            continue
        for number in numbers:
            component = available[int(number) - 1]
            toggle_component(selected, component)


def download(url: str, destination: Path) -> None:
    request = urllib.request.Request(url, headers={'User-Agent': 'slopworld-installer'})
    with urllib.request.urlopen(request, timeout=60) as response, destination.open('wb') as output:
        shutil.copyfileobj(response, output)


def fetch_packages(host: Host, version: str, components: list[str], directory: Path) -> list[Path]:
    base = f'https://github.com/{REPOSITORY}/releases/download/v{version}'
    checksum_file = directory / 'SHA256SUMS'
    download(f'{base}/SHA256SUMS', checksum_file)
    checksums = {}
    for line in checksum_file.read_text().splitlines():
        digest, name = line.split('  ', 1)
        if not re.fullmatch(r'[a-f0-9]{64}', digest) or name in checksums:
            raise ValueError('Invalid release checksums.')
        checksums[name] = digest
    packages = []
    for component in components:
        name = host.asset(component, version)
        if name not in checksums:
            raise ValueError(f'Release has no checksum for {name}.')
        path = directory / name
        print(f'Downloading {name}…', flush=True)
        download(f'{base}/{name}', path)
        actual = hashlib.sha256()
        with path.open('rb') as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b''):
                actual.update(chunk)
        if actual.hexdigest() != checksums[name]:
            raise ValueError(f'Checksum verification failed: {name}')
        packages.append(path)
    return packages


def run_package_manager(command: list[str], noninteractive: bool) -> None:
    if noninteractive:
        manager = command[0]
        command.append('--noconfirm' if manager == 'pacman' else '-y')
        if manager == 'apt':
            command.extend(['-o', 'Dpkg::Options::=--force-confdef', '-o', 'Dpkg::Options::=--force-confold'])
            command = ['env', 'DEBIAN_FRONTEND=noninteractive', *command]
    if os.geteuid() != 0:
        if shutil.which('sudo') is None:
            raise ValueError('sudo is required to change system packages.')
        command = ['sudo', *(['-n'] if noninteractive else []), *command]
    print(f'Running {shlex.join(command)}', flush=True)
    if noninteractive:
        subprocess.run(command, check=True, stdin=subprocess.DEVNULL)
    else:
        subprocess.run(command, check=True)


def install_packages(host: Host, packages: list[Path], noninteractive: bool = False) -> None:
    if packages:
        command = {'debian': ['apt', 'install'], 'fedora': ['dnf', 'install'], 'arch': ['pacman', '-U']}[
            host.distribution
        ]
        run_package_manager([*command, *map(str, packages)], noninteractive)


def remove_packages(host: Host, components: list[str], noninteractive: bool = False) -> None:
    if components:
        # Keep user configuration; no purge, recursive removal, or autoremove.
        command = {
            'debian': ['apt', 'remove'],
            'fedora': ['dnf', 'remove', '--setopt=clean_requirements_on_remove=False'],
            'arch': ['pacman', '-R'],
        }[host.distribution]
        run_package_manager([*command, *components], noninteractive)


def main() -> None:
    parser = argparse.ArgumentParser(description='Install, update, or remove SlopWorld components.')
    parser.add_argument('--version', help='Release version (default: latest stable release)')
    parser.add_argument(
        '--install',
        nargs='+',
        choices=COMPONENTS,
        action='extend',
        default=[],
        help='Install/update these components without the menu; other components stay unchanged',
    )
    parser.add_argument(
        '--remove',
        nargs='+',
        choices=COMPONENTS,
        action='extend',
        default=[],
        help='Remove these components without the menu',
    )
    parser.add_argument('--no-tui', action='store_true', help='Require explicit --install or --remove actions')
    args = parser.parse_args()
    explicit = len(sys.argv) > 1
    if explicit and not (args.install or args.remove):
        parser.error('CLI mode requires --install or --remove; run without arguments for the menu')
    host = detect_host()
    installed = installed_versions(host)
    version = ''
    if explicit:
        plan = plan_changes(host, installed, args.install, args.remove)
    else:
        version = release_version(args.version)
        selected = select_components(host, installed, version)
        if selected is None:
            print('Cancelled. Nothing changed.')
            return
        remove = [name for name in host.components() if name in installed and name not in selected]
        plan = plan_changes(host, installed, selected, remove)
    if not plan.install and not plan.remove:
        print('Nothing to change.')
        return
    print('Install/update: ' + (', '.join(plan.install) or 'none'))
    print('Remove: ' + (', '.join(plan.remove) or 'none'))
    # Verify and install before removing anything. Failed downloads/installations
    # must not trigger removals. Package transactions are not jointly atomic.
    if plan.install:
        version = version or release_version(args.version)
        with tempfile.TemporaryDirectory(prefix='slopworld-', dir='/tmp') as temporary:
            packages = fetch_packages(host, version, plan.install, Path(temporary))
            install_packages(host, packages, explicit)
    remove_packages(host, plan.remove, explicit)
    print('Package changes completed. Game attachment and service setup are separate steps.')
    print('https://slopworld.space/installation/linux.html#attach-the-mod-and-launch')


def connect_piped_terminal() -> None:
    # Python has consumed the script from the pipe. Reconnect fd 0 as well as
    # Python input so curses and package-manager subprocesses share the terminal.
    if sys.argv == ['-']:
        with Path('/dev/tty').open(encoding='utf-8') as terminal:
            os.dup2(terminal.fileno(), 0)


if __name__ == '__main__':
    try:
        connect_piped_terminal()
        main()
    except (KeyboardInterrupt, EOFError):
        print('\nCancelled.', file=sys.stderr)
        sys.exit(130)
    except (OSError, ValueError, KeyError, subprocess.CalledProcessError) as error:
        print(f'Installation failed: {error}', file=sys.stderr)
        sys.exit(1)
