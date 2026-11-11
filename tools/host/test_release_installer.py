"""Exercise installer boundaries without network access or system installation."""

import hashlib
import io
import subprocess
import sys
from pathlib import Path
from unittest.mock import patch

import pytest

from tools.host import release_installer as installer


def test_piped_menu_reconnects_subprocess_input_to_terminal() -> None:
    with (
        patch.object(sys, 'argv', ['-']),
        patch.object(Path, 'open') as terminal,
        patch('tools.host.release_installer.os.dup2') as duplicate,
    ):
        terminal.return_value.__enter__.return_value.fileno.return_value = 7
        installer.connect_piped_terminal()
    terminal.assert_called_once_with(encoding='utf-8')
    duplicate.assert_called_once_with(7, 0)


@pytest.mark.parametrize('arguments', [['install.py'], ['-', '--install', 'slopctl'], ['-', '--help']])
def test_file_and_explicit_invocations_preserve_input(arguments: list[str]) -> None:
    with patch.object(sys, 'argv', arguments), patch.object(Path, 'open') as terminal:
        installer.connect_piped_terminal()
    terminal.assert_not_called()


@pytest.mark.parametrize(
    'identifier,machine,distribution,components',
    [
        ('ubuntu', 'x86_64', 'debian', list(installer.COMPONENTS)),
        ('debian', 'aarch64', 'debian', ['slopcar']),
        ('fedora', 'arm64', 'fedora', ['slopcar']),
        ('arch', 'x86_64', 'arch', list(installer.COMPONENTS)),
    ],
)
def test_host_selects_only_shipped_components(
    identifier: str, machine: str, distribution: str, components: list[str]
) -> None:
    with (
        patch('tools.host.release_installer.platform.system', return_value='Linux'),
        patch('tools.host.release_installer.platform.machine', return_value=machine),
        patch('tools.host.release_installer.platform.freedesktop_os_release', return_value={'ID': identifier}),
        patch('tools.host.release_installer.shutil.which', return_value='/usr/bin/tool'),
    ):
        host = installer.detect_host()
    assert host.distribution == distribution
    assert host.components() == components


@pytest.mark.parametrize(
    'system,identifier,machine',
    [
        ('Darwin', 'arch', 'x86_64'),
        ('Linux', 'arch', 'aarch64'),
        ('Linux', 'unknown', 'x86_64'),
        ('Linux', 'debian', 'riscv64'),
    ],
)
def test_unsupported_hosts_fail_before_package_queries(system: str, identifier: str, machine: str) -> None:
    with (
        patch('tools.host.release_installer.platform.system', return_value=system),
        patch('tools.host.release_installer.platform.machine', return_value=machine),
        patch('tools.host.release_installer.platform.freedesktop_os_release', return_value={'ID': identifier}),
        patch('tools.host.release_installer.shutil.which', return_value=None),
        pytest.raises(ValueError),
    ):
        installer.detect_host()


@pytest.mark.parametrize(
    'distribution,output', [('debian', 'installed\t1.2.3-1'), ('fedora', '1.2.3-1'), ('arch', 'slopworld 1.2.3-1')]
)
def test_installed_and_absent_packages(distribution: str, output: str) -> None:
    results = [subprocess.CompletedProcess([], 0, output)] + [subprocess.CompletedProcess([], 1, '')] * 3
    with patch('tools.host.release_installer.subprocess.run', side_effect=results):
        assert installer.installed_versions(installer.Host(distribution, 'amd64', 'Host')) == {'slopworld': '1.2.3-1'}


def test_removed_debian_package_is_not_installed() -> None:
    result = subprocess.CompletedProcess([], 0, 'config-files\t1.2.3-1')
    with patch('tools.host.release_installer.subprocess.run', return_value=result):
        assert installer.installed_versions(installer.Host('debian', 'amd64', 'Host')) == {}


def test_query_failure_is_not_treated_as_absence() -> None:
    with (
        patch('tools.host.release_installer.subprocess.run', return_value=subprocess.CompletedProcess([], 2, '')),
        pytest.raises(subprocess.CalledProcessError),
    ):
        installer.installed_versions(installer.Host('debian', 'amd64', 'Host'))


@pytest.mark.parametrize(
    'answers,expected',
    [(['2', '3', ''], ['slopd', 'slopctl']), (['1', ''], ['slopworld']), (['invalid', '99', 'q'], None), ([''], [])],
)
def test_checkboxes_and_required_cli(answers: list[str], expected: list[str] | None) -> None:
    with patch('builtins.input', side_effect=answers):
        assert installer.select_components(installer.Host('debian', 'amd64', 'Host'), {}, '1.2.3') == expected


def test_installed_components_can_all_be_unchecked() -> None:
    with patch('builtins.input', side_effect=['1', '']):
        assert (
            installer.select_components(installer.Host('debian', 'amd64', 'Host'), {'slopworld': '1.0.0'}, '1.2.3')
            == []
        )


@pytest.mark.parametrize('version', ['../../bad', 'v1.2.3/extra', 'latest', '1.2.3-preview'])
def test_version_cannot_inject_paths(version: str) -> None:
    with pytest.raises(ValueError):
        installer.release_version(version)


@pytest.mark.parametrize('damage', ['', 'missing', 'corrupt', 'network'])
def test_download_failures_prevent_any_install_and_clean_temporary_files(damage: str) -> None:
    host = installer.Host('debian', 'amd64', 'Host')
    names = [host.asset(component, '1.2.3') for component in ('slopd', 'slopctl')]
    payload = b'package bytes'
    digest = hashlib.sha256(payload).hexdigest()
    paths: list[Path] = []

    def download(url: str, path: Path) -> None:
        assert '/releases/download/v1.2.3/' in url
        paths.append(path)
        if path.name == 'SHA256SUMS':
            path.write_text(''.join(f'{digest}  {name}\n' for name in (names[:1] if damage == 'missing' else names)))
        elif path.name == names[-1] and damage == 'network':
            raise OSError('connection lost')
        else:
            path.write_bytes(b'corrupted' if damage == 'corrupt' else payload)

    with (
        patch('tools.host.release_installer.sys.argv', ['install.py']),
        patch.object(installer, 'release_version', return_value='1.2.3'),
        patch.object(installer, 'detect_host', return_value=host),
        patch.object(installer, 'installed_versions', return_value={}),
        patch.object(installer, 'select_components', return_value=['slopd', 'slopctl']),
        patch.object(installer, 'download', side_effect=download),
        patch.object(installer, 'install_packages') as install,
    ):
        if damage:
            with pytest.raises((ValueError, OSError)):
                installer.main()
            install.assert_not_called()
        else:
            installer.main()
            assert [path.name for path in install.call_args.args[1]] == names
    assert paths and all(not path.parent.exists() for path in paths)


@pytest.mark.parametrize(
    'distribution,command', [('debian', ['apt', 'install']), ('fedora', ['dnf', 'install']), ('arch', ['pacman', '-U'])]
)
def test_all_packages_install_in_one_interactive_transaction(distribution: str, command: list[str]) -> None:
    packages = [Path('/tmp/download/slopd'), Path('/tmp/download/slopctl')]
    with (
        patch('tools.host.release_installer.os.geteuid', return_value=1000),
        patch('tools.host.release_installer.shutil.which', return_value='/usr/bin/sudo'),
        patch('tools.host.release_installer.subprocess.run') as run,
    ):
        installer.install_packages(installer.Host(distribution, 'amd64', 'Host'), packages)
    run.assert_called_once_with(['sudo', *command, *map(str, packages)], check=True)


def test_cancel_does_not_download_or_install() -> None:
    with (
        patch('tools.host.release_installer.sys.argv', ['install.py']),
        patch.object(installer, 'release_version', return_value='1.2.3'),
        patch.object(installer, 'detect_host', return_value=installer.Host('debian', 'amd64', 'Host')),
        patch.object(installer, 'installed_versions', return_value={}),
        patch('builtins.input', return_value='q'),
        patch.object(installer, 'fetch_packages') as fetch,
        patch.object(installer, 'install_packages') as install,
    ):
        installer.main()
    fetch.assert_not_called()
    install.assert_not_called()


def test_latest_release_resolves_once_to_a_stable_version() -> None:
    with patch(
        'tools.host.release_installer.urllib.request.urlopen', return_value=io.BytesIO(b'{"tag_name":"v1.2.3"}')
    ) as request:
        assert installer.release_version(None) == '1.2.3'
    assert request.call_args.args[0].full_url.endswith('/releases/latest')


def test_download_streams_response_to_file(tmp_path: Path) -> None:
    destination = tmp_path / 'package'
    with patch('tools.host.release_installer.urllib.request.urlopen', return_value=io.BytesIO(b'package')):
        installer.download('https://github.com/example', destination)
    assert destination.read_bytes() == b'package'


def test_failed_install_cleans_downloads_and_propagates_failure(tmp_path: Path) -> None:
    directories: list[Path] = []

    def fetch(host: installer.Host, version: str, components: list[str], directory: Path) -> list[Path]:
        directories.append(directory)
        package = directory / 'package.deb'
        package.write_bytes(b'package')
        return [package]

    with (
        patch('tools.host.release_installer.sys.argv', ['install.py']),
        patch.object(installer, 'release_version', return_value='1.2.3'),
        patch.object(installer, 'detect_host', return_value=installer.Host('debian', 'amd64', 'Host')),
        patch.object(installer, 'installed_versions', return_value={}),
        patch.object(installer, 'select_components', return_value=['slopctl']),
        patch.object(installer, 'fetch_packages', side_effect=fetch),
        patch.object(installer, 'install_packages', side_effect=subprocess.CalledProcessError(1, ['apt'])),
        pytest.raises(subprocess.CalledProcessError),
    ):
        installer.main()
    assert directories and not directories[0].exists()


def test_release_entry_point_runs_without_checkout_or_site_packages(tmp_path: Path) -> None:
    script = tmp_path / 'install.py'
    script.write_bytes(Path(installer.__file__).read_bytes())
    result = subprocess.run(
        [sys.executable, '-I', '-S', str(script), '--help'],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        check=True,
    )
    assert '--version' in result.stdout


@pytest.mark.parametrize(
    'release,commands,expected',
    [
        ({'ID': 'cachyos', 'ID_LIKE': 'arch'}, ['pacman', 'apt'], 'arch'),
        ({'ID': 'cachyos'}, ['pacman'], 'arch'),
        ({'ID': 'derivative', 'ID_LIKE': 'ubuntu debian'}, ['apt', 'dpkg-query', 'pacman'], 'debian'),
        ({'ID': 'derivative'}, ['dnf', 'rpm'], 'fedora'),
        ({'ID': 'derivative'}, ['apt', 'dpkg-query'], 'debian'),
    ],
)
def test_derivatives_use_ancestry_or_available_package_manager(
    release: dict[str, str], commands: list[str], expected: str
) -> None:
    with (
        patch('tools.host.release_installer.platform.system', return_value='Linux'),
        patch('tools.host.release_installer.platform.machine', return_value='x86_64'),
        patch('tools.host.release_installer.platform.freedesktop_os_release', return_value=release),
        patch(
            'tools.host.release_installer.shutil.which',
            side_effect=lambda name: f'/usr/bin/{name}' if name in commands else None,
        ),
    ):
        assert installer.detect_host().distribution == expected


@pytest.mark.parametrize('commands', [[], ['apt', 'dnf'], ['apt']])
def test_unknown_hosts_require_unambiguous_manager_and_query_tool(commands: list[str]) -> None:
    with (
        patch('tools.host.release_installer.platform.system', return_value='Linux'),
        patch('tools.host.release_installer.platform.machine', return_value='x86_64'),
        patch('tools.host.release_installer.platform.freedesktop_os_release', return_value={'ID': 'unknown'}),
        patch(
            'tools.host.release_installer.shutil.which',
            side_effect=lambda name: f'/usr/bin/{name}' if name in commands else None,
        ),
        pytest.raises(ValueError),
    ):
        installer.detect_host()


@pytest.mark.parametrize(
    'keys,expected',
    [
        (['down', 'space', 'down', 'space', 'enter'], ['slopd', 'slopctl']),
        (['up', 'space', 'enter'], ['slopcar']),
        (['space', 'space', 'enter'], []),
        (['space', 'escape'], None),
    ],
)
def test_terminal_navigation_selection_and_cancellation(keys: list[str], expected: list[str] | None) -> None:
    import curses
    from unittest.mock import MagicMock

    codes = {'down': curses.KEY_DOWN, 'up': curses.KEY_UP, 'space': ord(' '), 'enter': 10, 'escape': 27}
    screen = MagicMock()
    screen.getmaxyx.return_value = (24, 120)
    screen.getch.side_effect = [codes[key] for key in keys]
    with patch('curses.curs_set'):
        assert installer.select_terminal(screen, installer.Host('arch', 'amd64', 'CachyOS'), {}, '1.2.3') == expected
    screen.keypad.assert_called_once_with(True)


def test_terminal_resize_preserves_selection_and_blocks_hidden_confirmation() -> None:
    import curses
    from unittest.mock import MagicMock

    screen = MagicMock()
    screen.getmaxyx.side_effect = [(24, 120), (2, 10), (24, 120)]
    screen.getch.side_effect = [ord(' '), 10, curses.KEY_ENTER]
    with patch('curses.curs_set'):
        assert installer.select_terminal(screen, installer.Host('arch', 'amd64', 'CachyOS'), {}, '1.2.3') == [
            'slopworld'
        ]


def test_terminal_initialization_failure_uses_numbered_fallback() -> None:
    import curses

    with (
        patch('tools.host.release_installer.sys.stdin.isatty', return_value=True),
        patch('tools.host.release_installer.sys.stdout.isatty', return_value=True),
        patch('curses.wrapper', side_effect=curses.error('no terminal')),
        patch('builtins.input', side_effect=['1', '']),
    ):
        assert installer.select_components(installer.Host('arch', 'amd64', 'CachyOS'), {}, '1.2.3') == ['slopworld']


@pytest.mark.parametrize('answer,removed', [('1', ['slopworld']), ('q', [])])
def test_unchecking_installed_package_removes_it_but_cancel_does_not(answer: str, removed: list[str]) -> None:
    with (
        patch('tools.host.release_installer.sys.argv', ['install.py']),
        patch.object(installer, 'release_version', return_value='1.2.3'),
        patch.object(installer, 'detect_host', return_value=installer.Host('arch', 'amd64', 'Host')),
        patch.object(installer, 'installed_versions', return_value={'slopworld': '1.0.0'}),
        patch('builtins.input', side_effect=[answer, '']),
        patch.object(installer, 'fetch_packages') as fetch,
        patch.object(installer, 'remove_packages') as remove,
    ):
        installer.main()
    fetch.assert_not_called()
    if removed:
        assert remove.call_args.args[1] == removed
    else:
        remove.assert_not_called()


@pytest.mark.parametrize(
    'wanted,unwanted,expected',
    [
        (['slopd'], [], installer.Plan(['slopd', 'slopctl'], [])),
        ([], ['slopd', 'slopctl'], installer.Plan([], ['slopd', 'slopctl'])),
        ([], ['slopcar'], installer.Plan([], [])),
    ],
)
def test_cli_plan_dependencies_and_absent_removals(
    wanted: list[str], unwanted: list[str], expected: installer.Plan
) -> None:
    assert (
        installer.plan_changes(
            installer.Host('arch', 'amd64', 'Host'), {'slopd': '1', 'slopctl': '1'}, wanted, unwanted
        )
        == expected
    )


@pytest.mark.parametrize('wanted,unwanted', [(['slopd'], ['slopctl']), ([], ['slopctl']), (['slopctl'], ['slopctl'])])
def test_invalid_dependency_or_conflicting_actions_fail(wanted: list[str], unwanted: list[str]) -> None:
    with pytest.raises(ValueError):
        installer.plan_changes(
            installer.Host('arch', 'amd64', 'Host'), {'slopd': '1', 'slopctl': '1'}, wanted, unwanted
        )


@pytest.mark.parametrize('arguments', [['--no-tui'], ['--version', '1.2.3'], ['--yes']])
def test_noninteractive_mode_requires_explicit_actions(arguments: list[str]) -> None:
    with patch('tools.host.release_installer.sys.argv', ['install.py', *arguments]), pytest.raises(SystemExit) as error:
        installer.main()
    assert error.value.code == 2


def test_cli_removal_is_offline_and_never_opens_menu() -> None:
    with (
        patch('tools.host.release_installer.sys.argv', ['install.py', '--remove', 'slopcar']),
        patch.object(installer, 'detect_host', return_value=installer.Host('arch', 'amd64', 'Host')),
        patch.object(installer, 'installed_versions', return_value={'slopcar': '1.0.0', 'slopworld': '1.0.0'}),
        patch.object(installer, 'release_version') as release,
        patch.object(installer, 'select_components') as menu,
        patch.object(installer, 'remove_packages') as remove,
    ):
        installer.main()
    release.assert_not_called()
    menu.assert_not_called()
    assert remove.call_args.args[1:] == (['slopcar'], True)


@pytest.mark.parametrize('failure', ['', 'fetch', 'install', 'remove'])
def test_mixed_cli_changes_install_before_removal_and_propagate_failure(failure: str) -> None:
    stages: list[str] = []

    def stage(name: str) -> None:
        stages.append(name)
        if failure == name:
            raise OSError(name)

    def fetch(host: installer.Host, version: str, components: list[str], directory: Path) -> list[Path]:
        stage('fetch')
        return [directory / 'package']

    with (
        patch(
            'tools.host.release_installer.sys.argv',
            ['install.py', '--install', 'slopworld', '--remove', 'slopcar', '--version', '1.2.3'],
        ),
        patch.object(installer, 'detect_host', return_value=installer.Host('arch', 'amd64', 'Host')),
        patch.object(installer, 'installed_versions', return_value={'slopcar': '1.0.0'}),
        patch.object(installer, 'fetch_packages', side_effect=fetch),
        patch.object(installer, 'install_packages', side_effect=lambda *args: stage('install')),
        patch.object(installer, 'remove_packages', side_effect=lambda *args: stage('remove')),
    ):
        if failure:
            with pytest.raises(OSError):
                installer.main()
        else:
            installer.main()
    expected = ['fetch', 'install', 'remove']
    assert stages == (expected[: expected.index(failure) + 1] if failure else expected)


@pytest.mark.parametrize(
    'distribution,command',
    [
        ('arch', ['pacman', '-R', 'slopd', 'slopctl', '--noconfirm']),
        (
            'debian',
            [
                'env',
                'DEBIAN_FRONTEND=noninteractive',
                'apt',
                'remove',
                'slopd',
                'slopctl',
                '-y',
                '-o',
                'Dpkg::Options::=--force-confdef',
                '-o',
                'Dpkg::Options::=--force-confold',
            ],
        ),
        ('fedora', ['dnf', 'remove', '--setopt=clean_requirements_on_remove=False', 'slopd', 'slopctl', '-y']),
    ],
)
def test_removal_uses_package_manager_without_purging_or_autoremoving(distribution: str, command: list[str]) -> None:
    with (
        patch('tools.host.release_installer.os.geteuid', return_value=0),
        patch('tools.host.release_installer.subprocess.run') as run,
    ):
        installer.remove_packages(
            installer.Host(distribution, 'amd64', 'Host'), ['slopd', 'slopctl'], noninteractive=True
        )
    run.assert_called_once_with(command, check=True, stdin=subprocess.DEVNULL)


def test_cli_install_never_prompts_for_sudo_or_package_input() -> None:
    with (
        patch('tools.host.release_installer.os.geteuid', return_value=1000),
        patch('tools.host.release_installer.shutil.which', return_value='/usr/bin/sudo'),
        patch('tools.host.release_installer.subprocess.run') as run,
    ):
        installer.install_packages(installer.Host('arch', 'amd64', 'Host'), [Path('/tmp/package')], noninteractive=True)
    run.assert_called_once_with(
        ['sudo', '-n', 'pacman', '-U', '/tmp/package', '--noconfirm'], check=True, stdin=subprocess.DEVNULL
    )
