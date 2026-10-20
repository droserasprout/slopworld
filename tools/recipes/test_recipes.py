"""Protect recipe argument boundaries, failure ordering and side-effect isolation."""

import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Any

import yaml

from tools import ROOT

JUST = shutil.which('just')


@unittest.skipUnless(JUST, 'just is required')
class RecipeTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory(prefix='slopworld recipes ')
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.log = self.directory / 'calls.jsonl'
        stub = self.directory / 'stub'
        stub.write_text(
            f'#!{sys.executable}\n'
            + """
import json, os, pathlib, sys
tool = pathlib.Path(sys.argv[0]).name
keys = ("BUILD", "CARGOFLAGS", "JUST_CMD", "RUSTC_WRAPPER", "SCCACHE_DIR", "LLVM_COV", "LLVM_PROFDATA")
with open(os.environ["RECIPE_TEST_LOG"], "a") as log:
    log.write(json.dumps({"tool": tool, "args": sys.argv[1:], "cwd": os.getcwd(),
        "env": {key: os.environ.get(key) for key in keys}}) + "\\n")
if tool == os.environ.get("RECIPE_TEST_FAIL"):
    sys.exit(23)
"""
        )
        stub.chmod(0o755)
        for tool in ('cargo', 'dotnet', 'uv', 'runner', 'slopctl', 'container', 'sidecar'):
            (self.directory / tool).symlink_to(stub)
        self.env = os.environ.copy()
        # Make results independent of the caller's selected build/profile/tool settings.
        settings = '\n'.join(
            path.read_text() for directory in (ROOT / 'just', ROOT / 'mac') for path in directory.glob('*.just')
        )
        for key in re.findall(r'^(?:export )?([A-Z][A-Z_]+) :=', settings, re.MULTILINE):
            self.env.pop(key, None)
        self.env.update(PATH=str(self.directory) + os.pathsep + os.environ['PATH'], RECIPE_TEST_LOG=str(self.log))

    def run_recipe(self, *args: str, cwd: Path = ROOT, success: bool = True) -> subprocess.CompletedProcess[str]:
        assert JUST is not None
        result = subprocess.run([JUST, *args], cwd=cwd, env=self.env, capture_output=True, text=True, check=False)
        if success:
            self.assertEqual(result.returncode, 0, result.stderr)
        else:
            self.assertNotEqual(result.returncode, 0)
        return result

    def calls(self, tool: str) -> list[dict[str, Any]]:
        if not self.log.exists():
            return []
        return [call for line in self.log.read_text().splitlines() if (call := json.loads(line))['tool'] == tool]

    def test_coverage_preserves_output_paths_and_llvm_overrides(self) -> None:
        output = self.directory / 'coverage output'
        self.env.update(LLVM_COV='/custom/llvm-cov', LLVM_PROFDATA='/custom/llvm-profdata')
        self.run_recipe(f'COVERAGE_DIR={output}', 'daemon-coverage')
        calls = self.calls('cargo')
        for call in calls:
            self.assertEqual(call['env']['LLVM_COV'], '/custom/llvm-cov')
            self.assertEqual(call['env']['LLVM_PROFDATA'], '/custom/llvm-profdata')
        self.assertEqual(calls[0]['args'], ['llvm-cov', 'clean', '--workspace'])
        reports = [call['args'] for call in calls if '--output-path' in call['args']]
        self.assertEqual(
            {args[-1] for args in reports},
            {
                str(output / 'rust.cobertura.xml'),
                str(output / 'rust.filtered.cobertura.xml'),
            },
        )
        self.run_recipe(f'COVERAGE_DIR={output}', 'mod-coverage')
        coverlet = next(call['args'] for call in self.calls('dotnet') if 'coverlet' in call['args'])
        self.assertIn('**/mod/Tests/**/*.cs', coverlet)
        self.assertIn('**/Client/Generated/Slopworld.cs', coverlet)
        self.assertEqual(coverlet[-1], str(output / 'csharp.cobertura.xml'))

    def test_failed_coverage_cleanup_prevents_collection_and_reports(self) -> None:
        self.env['RECIPE_TEST_FAIL'] = 'cargo'
        self.run_recipe(f'COVERAGE_DIR={self.directory / "coverage"}', 'daemon-coverage', success=False)
        (call,) = self.calls('cargo')
        self.assertEqual(call['args'], ['llvm-cov', 'clean', '--workspace'])

    def test_parallel_ci_jobs_check_out_the_same_resolved_commit(self) -> None:
        workflow = yaml.safe_load((ROOT / '.github/workflows/test.yml').read_text())
        jobs = workflow['jobs']
        for name in ('rust', 'tools', 'mod'):
            with self.subTest(job=name):
                self.assertEqual(jobs[name]['needs'], 'revision')
                checkout = next(
                    step for step in jobs[name]['steps'] if step.get('uses', '').startswith('actions/checkout@')
                )
                self.assertEqual(checkout['with']['ref'], '${{ needs.revision.outputs.commit }}')

    def test_sidecar_tests_preserve_rust_test_arguments(self) -> None:
        self.run_recipe('sidecar-test', '--', '--skip', 'filter with spaces')
        (cargo,) = self.calls('cargo')
        self.assertEqual(cargo['args'][-3:], ['--', '--skip', 'filter with spaces'])

    def test_ci_tools_runs_every_tool_suite_without_compiling_rust_or_csharp(self) -> None:
        self.run_recipe('ci-tools')
        args = [call['args'] for call in self.calls('uv')]
        for suite in (
            'tools.protocol.api_contract',
            'tools.themes.validate_themes',
            'tools.terminal.pager_geometry',
        ):
            self.assertTrue(any(suite in call for call in args), suite)
        self.assertEqual(self.calls('cargo'), [])
        self.assertIn(['run', '--locked', 'python', '-m', 'pytest'], args)
        self.assertEqual(self.calls('dotnet'), [])

    def test_csharp_lint_does_not_compile_the_mod(self) -> None:
        self.run_recipe('mod-lint')
        self.assertFalse(any('build' in call['args'] for call in self.calls('dotnet')))

    def test_pytest_selection_preserves_spaces_in_arguments(self) -> None:
        self.run_recipe('tools-test', 'tools/recipes', '-k', 'release or failure')
        (call,) = self.calls('uv')
        self.assertEqual(call['args'][-3:], ['tools/recipes', '-k', 'release or failure'])

    def test_selected_runner_receives_help_and_separator_unchanged(self) -> None:
        self.run_recipe('tools-test', '--help')
        (call,) = self.calls('uv')
        self.assertEqual(call['args'][-1], '--help')
        self.log.unlink()
        self.run_recipe('daemon-test', '--', '--nocapture')
        (call,) = self.calls('cargo')
        self.assertEqual(call['args'][-2:], ['--', '--nocapture'])

    def test_benchmark_suite_and_options_are_forwarded(self) -> None:
        self.run_recipe('BUILD=release', 'bench-run', 'daemon', '--run', 'trial with spaces')
        (call,) = self.calls('uv')
        self.assertEqual(call['args'][-6:], ['--suite', 'daemon', '--build', 'release', '--run', 'trial with spaces'])

    def test_sidecar_image_builds_its_launcher_and_preserves_source_path(self) -> None:
        self.run_recipe(f'SLOPCAR={self.directory / "sidecar"}', 'sidecar-image')
        calls = [json.loads(line) for line in self.log.read_text().splitlines()]
        self.assertEqual([call['tool'] for call in calls], ['cargo', 'sidecar'])
        self.assertEqual(calls[0]['cwd'], str(ROOT / 'slopcar'))
        self.assertEqual(calls[1]['args'], ['build', '--source', str(ROOT)])

    def test_distribution_recipes_pass_the_distribution_to_their_tools(self) -> None:
        for distribution in ('arch', 'debian', 'fedora'):
            with self.subTest(distribution=distribution):
                self.log.unlink(missing_ok=True)
                self.run_recipe(f'CONTAINER={self.directory / "container"}', f'image-{distribution}')
                (container,) = self.calls('container')
                self.assertIn(f'packaging/{distribution}/Dockerfile', container['args'])
                self.run_recipe(f'package-{distribution}')
                (package,) = self.calls('uv')
                self.assertEqual(package['args'][-2:], ['tools.release.native', distribution])

    def test_invalid_image_target_and_failed_launcher_never_build_images(self) -> None:
        for what in ('typo', 'sidecar', 'build'):
            self.run_recipe(f'image-{what}', success=False)
            self.assertFalse(self.log.exists())
        help_text = self.run_recipe('--list').stdout
        for what in ('arch', 'debian', 'fedora'):
            self.assertIn(what, help_text)
        self.assertFalse(self.log.exists())
        self.env['RECIPE_TEST_FAIL'] = 'cargo'
        self.run_recipe(f'SLOPCAR={self.directory / "sidecar"}', 'sidecar-image', success=False)
        self.assertEqual(len(self.calls('cargo')), 1)
        self.assertEqual(self.calls('sidecar'), [])

    def test_macos_launch_and_install_preserve_selected_image(self) -> None:
        self.env['SLOPCAR_IMAGE'] = 'custom-agents:local'
        for recipe in ('install', 'run', 'doctor'):
            result = self.run_recipe('--dry-run', f'mac-{recipe}')
            self.assertNotIn('image build', result.stderr)
            self.assertNotIn('build --source', result.stderr)

    def test_release_build_uses_environment_and_command_override(self) -> None:
        self.env['BUILD'] = 'debug'
        self.run_recipe('BUILD=release', 'daemon-build')
        (call,) = self.calls('cargo')
        self.assertEqual(call['args'], ['build', '--locked', '--release'])
        self.assertEqual(call['env']['BUILD'], 'release')
        self.assertEqual(call['cwd'], str(ROOT / 'slopd'))

    def test_build_overrides_preserve_frozen_flags_and_custom_paths(self) -> None:
        self.run_recipe('BUILD=release', 'CARGOFLAGS=--release --frozen', 'daemon-build')
        (call,) = self.calls('cargo')
        self.assertEqual(call['args'], ['build', '--locked', '--release', '--frozen'])
        self.log.unlink()
        self.run_recipe(f'RUNNER={self.directory / "runner"}', 'install-mod')
        installer = next(call for call in self.calls('uv') if 'tools.host.install_mod' in call['args'])
        self.assertIn(str(self.directory / 'runner'), installer['args'])

    def test_rust_cache_wrapper_discovery_and_explicit_overrides(self) -> None:
        (self.directory / 'sccache').symlink_to(self.directory / 'stub')
        self.run_recipe('daemon-build')
        (call,) = self.calls('cargo')
        self.assertEqual(call['env']['RUSTC_WRAPPER'], str(self.directory / 'sccache'))
        self.assertEqual(call['env']['SCCACHE_DIR'], str(Path.home() / '.cache/sccache'))
        for wrapper in ('', '/custom/wrapper'):
            with self.subTest(wrapper=wrapper):
                self.log.unlink()
                self.run_recipe(f'RUSTC_WRAPPER={wrapper}', 'SCCACHE_DIR=/custom/cache', 'daemon-build')
                (call,) = self.calls('cargo')
                self.assertEqual(call['env']['RUSTC_WRAPPER'], wrapper)
                self.assertEqual(call['env']['SCCACHE_DIR'], '/custom/cache')

    def test_logs_delegate_path_and_mode_selection_to_slopctl(self) -> None:
        self.run_recipe(f'SLOPCTL={self.directory / "slopctl"}', 'logs', '--sidecar')
        (call,) = self.calls('slopctl')
        self.assertEqual(call['args'], ['--sidecar', 'logs', 'game', '--follow'])
        self.assertEqual(self.run_recipe('--evaluate', 'LOG').stdout.strip(), '')

    def test_sidecar_launch_passes_explicit_mode_and_shared_profile_override(self) -> None:
        self.run_recipe(f'RUNNER={self.directory / "runner"}', 'PROFILE=/chosen profile', 'sidecar-run')
        (call,) = self.calls('runner')
        self.assertEqual(call['args'][-2:], ['--profile', '/chosen profile'])
        self.assertEqual(call['args'][:2], ['--sidecar', '--game'])
        self.log.unlink()
        self.run_recipe(f'RUNNER={self.directory / "runner"}', 'mac-run')
        (call,) = self.calls('runner')
        self.assertEqual(call['args'][:2], ['--sidecar', '--game-exe'])
        self.assertNotIn('--profile', call['args'])

    def test_trace_recipe_queries_the_launcher_profile(self) -> None:
        self.run_recipe(
            f'RUNNER={self.directory / "runner"}', 'trace-capture', 'true', '', '--output', 'trace with spaces.log'
        )
        (call,) = self.calls('runner')
        self.assertEqual(call['args'], ['--sidecar', 'paths', 'profile'])
        (capture,) = self.calls('uv')
        self.assertEqual(capture['args'][-2:], ['--output', 'trace with spaces.log'])

    def test_terminal_benchmark_builds_the_selected_launcher_before_running(self) -> None:
        self.run_recipe('BUILD=release', 'bench-terminal')
        calls = [json.loads(line) for line in self.log.read_text().splitlines()]
        self.assertEqual([call['tool'] for call in calls], ['cargo', 'uv'])
        self.assertEqual(calls[0]['args'], ['build', '--locked', '--release'])
        self.assertIn('bench/terminal-input/terminal-input-bench.py', calls[1]['args'])

    def test_invalid_build_fails_before_running_commands(self) -> None:
        result = self.run_recipe('BUILD=fast', 'daemon-build', success=False)
        self.assertIn('BUILD must be exactly debug or release', result.stderr)
        self.assertFalse(self.log.exists())

    def test_refresh_is_explicit_and_stages_licenses_last(self) -> None:
        self.run_recipe('generate-all')
        modules = [call['args'][call['args'].index('-m') + 1] for call in self.calls('uv')]
        self.assertEqual(modules[-1], 'tools.licenses.stage_licenses')
        self.assertEqual(self.calls('cargo'), [])
        self.assertEqual(self.calls('dotnet'), [])
        self.assertIn('tools.licenses.sidecar_licenses', modules)
        for module in ('emoji', 'emoji_atlas', 'fileicons'):
            self.assertIn(f'tools.assets.{module}', modules)
        self.assertFalse(any('lock' in call['args'] for call in self.calls('uv')))

    def test_lock_refresh_updates_every_graph_before_staging_notices(self) -> None:
        self.run_recipe('deps-update')
        cargo = self.calls('cargo')
        self.assertEqual(
            [call['cwd'] for call in cargo],
            [
                str(ROOT / 'slopd'),
                str(ROOT / 'slopcar'),
                str(ROOT / 'bench/ipc/rust'),
            ],
        )
        self.assertTrue(all(call['args'] == ['update'] for call in cargo))
        dotnet = self.calls('dotnet')
        self.assertEqual(
            [call['args'][1] for call in dotnet],
            [
                'mod/Dependencies/Runtime.csproj',
                'mod/Source/SlopWorld/SlopWorld.csproj',
                'mod/Tests/SlopWorld.Tests.csproj',
                'bench/ipc/csharp/IpcBench.csproj',
            ],
        )
        self.assertTrue(all('--force-evaluate' in call['args'] for call in dotnet))
        self.assertTrue(all('-p:RestoreLockedMode=false' in call['args'] for call in dotnet))
        uv = self.calls('uv')
        self.assertEqual(uv[0]['args'], ['lock', '--upgrade'])
        self.assertEqual(uv[-1]['args'][-1], 'tools.licenses.stage_licenses')

    def test_failed_relock_does_not_publish_new_notices(self) -> None:
        self.env['RECIPE_TEST_FAIL'] = 'cargo'
        self.run_recipe('deps-update', success=False)
        self.assertEqual(len(self.calls('cargo')), 1)
        self.assertEqual(self.calls('dotnet'), [])
        self.assertEqual([call['args'] for call in self.calls('uv')], [['lock', '--upgrade']])

    def test_build_check_forces_release_and_warnings(self) -> None:
        self.run_recipe('BUILD=debug', 'VERSION=1.2.3', 'mod-check-build')
        self.assertEqual(self.calls('cargo'), [])
        (mod,) = [call for call in self.calls('dotnet') if 'mod/Source/SlopWorld/SlopWorld.csproj' in call['args']]
        self.assertIn('Release', mod['args'])
        self.assertIn('-p:TreatWarningsAsErrors=true', mod['args'])

    def test_rimref_ci_build_ignores_local_game_path(self) -> None:
        self.run_recipe('MANAGED=/missing/game', 'BUILD=debug', 'VERSION=1.2.3', 'mod-check-build-rimref')
        (mod,) = [call for call in self.calls('dotnet') if 'mod/Source/SlopWorld/SlopWorld.csproj' in call['args']]
        self.assertIn('-p:UseRimRef=true', mod['args'])
        self.assertFalse(any(arg.startswith('-p:RimWorldManaged=') for arg in mod['args']))
        self.assertIn('Release', mod['args'])
        self.assertIn('-p:TreatWarningsAsErrors=true', mod['args'])
        self.assertIn('-p:RestoreLockedMode=true', mod['args'])

    def test_csharp_formatting_stops_after_a_folder_fails(self) -> None:
        self.env['RECIPE_TEST_FAIL'] = 'dotnet'
        for operation in ('format', 'lint'):
            with self.subTest(operation=operation):
                self.log.unlink(missing_ok=True)
                self.run_recipe(f'mod-{operation}', success=False)
                self.assertEqual(len(self.calls('dotnet')), 1)

    def test_mac_mod_passes_native_assemblies_and_release_selection(self) -> None:
        app = self.directory / 'RimWorld.app'
        game = app / 'Contents/MacOS/RimWorld by Ludeon Studios'
        game.parent.mkdir(parents=True)
        game.touch()
        game.chmod(0o755)
        managed = app / 'Contents/Resources/Data/Managed'
        managed.mkdir(parents=True)
        (managed / 'Assembly-CSharp.dll').touch()
        (app / 'Mods').mkdir()
        self.run_recipe(
            '--justfile',
            str(ROOT / 'justfile'),
            'BUILD=release',
            'VERSION=1.2.3',
            f'MAC_RIMWORLD={app}',
            'mac-mod-build',
            cwd=ROOT / 'mac',
        )
        (mod,) = [call for call in self.calls('dotnet') if 'mod/Source/SlopWorld/SlopWorld.csproj' in call['args']]
        self.assertEqual(mod['cwd'], str(ROOT))
        self.assertEqual(mod['env']['BUILD'], 'release')
        self.assertIn(f'-p:RimWorldManaged={managed}', mod['args'])
        self.assertIn('Release', mod['args'])

    def test_mac_sidecar_workspace_defaults_to_repository_root(self) -> None:
        result = self.run_recipe(
            '--justfile', str(ROOT / 'justfile'), '--evaluate', 'SLOPCAR_WORKSPACE', cwd=ROOT / 'mac'
        )
        self.assertEqual(result.stdout.strip(), str(ROOT))
        result = self.run_recipe(
            '--justfile',
            str(ROOT / 'justfile'),
            '--evaluate',
            f'SLOPCAR_WORKSPACE={self.directory}',
            'SLOPCAR_WORKSPACE',
        )
        self.assertEqual(result.stdout.strip(), str(self.directory))

    def test_dependency_failure_stops_mod_build(self) -> None:
        self.env['RECIPE_TEST_FAIL'] = 'dotnet'
        self.run_recipe('VERSION=1.2.3', 'mod-build', success=False)
        self.assertEqual(self.calls('cargo'), [])
        (dependency,) = self.calls('dotnet')
        self.assertIn('mod/Dependencies/Runtime.csproj', dependency['args'])

    def test_mod_resolves_default_version_without_building_rust(self) -> None:
        self.run_recipe('mod-build')
        self.assertEqual(self.calls('cargo'), [])
        self.assertIn(
            ['run', '--locked', 'python', '-m', 'tools.version.mod_version'],
            [call['args'] for call in self.calls('uv')],
        )

    def test_version_override_skips_default_resolution(self) -> None:
        self.run_recipe('VERSION=1.2.3', 'mod-build')
        self.assertNotIn(
            ['run', '--locked', 'python', '-m', 'tools.version.mod_version'],
            [call['args'] for call in self.calls('uv')],
        )
        (mod,) = [call for call in self.calls('dotnet') if 'mod/Source/SlopWorld/SlopWorld.csproj' in call['args']]
        self.assertIn('-p:InformationalVersion=1.2.3', mod['args'])

    def test_daemon_build_does_not_invoke_python(self) -> None:
        (self.directory / 'python3').symlink_to(self.directory / 'stub')
        self.env['RECIPE_TEST_FAIL'] = 'python3'
        self.run_recipe('daemon-build')
        self.assertEqual(len(self.calls('cargo')), 1)
        self.assertEqual(self.calls('uv'), [])
        self.assertEqual(self.calls('python3'), [])

    def test_checks_and_builds_have_no_generation_dependencies(self) -> None:
        generators = (
            'tools.docs.api_docs',
            'tools.assets.loading_font_atlas',
            'tools.assets.icons',
            'tools.assets.emoji',
            'tools.assets.emoji_atlas',
            'tools.assets.fileicons',
            'tools.assets.appicon',
            'tools.assets.favicon',
            'tools.licenses.rust_licenses',
            'tools.licenses.stage_licenses',
        )
        for command in (
            ('build',),
            ('test',),
            ('ci',),
            ('bench-run',),
            ('docs-build',),
            ('docs-serve',),
            ('install',),
            ('install-mod',),
        ):
            with self.subTest(command=command):
                result = self.run_recipe('--dry-run', *command)
                for generator in generators:
                    self.assertNotIn(generator, result.stderr)
                for line in result.stderr.splitlines():
                    if 'tools.protocol.api_contract' in line:
                        self.assertTrue(line.endswith('--check'), line)

    def test_install_mod_still_builds_its_launcher(self) -> None:
        # Fail during dependency staging so this test never invokes an installer.
        self.env['RECIPE_TEST_FAIL'] = 'dotnet'
        self.run_recipe('VERSION=1.2.3', 'install-mod', success=False)
        self.assertEqual(len(self.calls('cargo')), 1)


if __name__ == '__main__':
    unittest.main()
