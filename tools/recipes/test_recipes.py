#!/usr/bin/env python3
"""Exercise recipe configuration and recursive overrides without building or launching."""

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
keys = ("BUILD", "CARGOFLAGS", "MOD_DEPS_LOCKED", "JUST_CMD", "RUSTC_WRAPPER", "SCCACHE_DIR")
with open(os.environ["RECIPE_TEST_LOG"], "a") as log:
    log.write(json.dumps({"tool": tool, "args": sys.argv[1:], "cwd": os.getcwd(),
        "env": {key: os.environ.get(key) for key in keys}}) + "\\n")
if tool == os.environ.get("RECIPE_TEST_FAIL"):
    sys.exit(23)
"""
        )
        stub.chmod(0o755)
        for tool in ('cargo', 'dotnet', 'uv'):
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
        result = subprocess.run([JUST, *args], cwd=cwd, env=self.env, capture_output=True, text=True)
        if success:
            self.assertEqual(result.returncode, 0, result.stderr)
        else:
            self.assertNotEqual(result.returncode, 0)
        return result

    def calls(self, tool: str) -> list[dict[str, Any]]:
        if not self.log.exists():
            return []
        return [call for line in self.log.read_text().splitlines() if (call := json.loads(line))['tool'] == tool]

    def test_native_targets_include_all_distributions(self) -> None:
        for distribution in ('arch', 'debian', 'fedora'):
            result = self.run_recipe('--dry-run', 'pkg-native', distribution)
            self.assertIn(f"tools.release.native '{distribution}'", result.stderr)
            result = self.run_recipe('--dry-run', 'release-image', distribution)
            self.assertIn(f'packaging/{distribution}/Dockerfile', result.stderr)
            result = self.run_recipe('--dry-run', 'test-release-native', distribution)
            self.assertIn(f'tools/release/test_{distribution}.py', result.stderr)
            self.assertEqual('--privileged' in result.stderr, distribution == 'arch')

    def test_macos_launch_and_install_preserve_selected_image(self) -> None:
        self.env['SLOPCAR_IMAGE'] = 'custom-agents:local'
        for recipe in ('install', 'run', 'doctor'):
            result = self.run_recipe('--dry-run', '--justfile', 'mac/justfile', recipe)
            self.assertNotIn('sidecar-build', result.stderr)
            self.assertNotIn('build --source', result.stderr)

    def test_release_build_uses_environment_and_command_override(self) -> None:
        self.env['BUILD'] = 'debug'
        self.run_recipe('BUILD=release', 'daemon')
        (call,) = self.calls('cargo')
        self.assertEqual(call['args'], ['build', '--release'])
        self.assertEqual(call['env']['BUILD'], 'release')
        self.assertEqual(call['cwd'], str(ROOT / 'slopd'))

    def test_rust_cache_wrapper_discovery_and_explicit_overrides(self) -> None:
        (self.directory / 'sccache').symlink_to(self.directory / 'stub')
        self.run_recipe('daemon')
        (call,) = self.calls('cargo')
        self.assertEqual(call['env']['RUSTC_WRAPPER'], str(self.directory / 'sccache'))
        self.assertEqual(call['env']['SCCACHE_DIR'], str(Path.home() / '.cache/sccache'))
        for wrapper in ('', '/custom/wrapper'):
            with self.subTest(wrapper=wrapper):
                self.log.unlink()
                self.run_recipe(f'RUSTC_WRAPPER={wrapper}', 'SCCACHE_DIR=/custom/cache', 'daemon')
                (call,) = self.calls('cargo')
                self.assertEqual(call['env']['RUSTC_WRAPPER'], wrapper)
                self.assertEqual(call['env']['SCCACHE_DIR'], '/custom/cache')

    def test_logs_delegate_path_and_mode_selection_to_slopctl(self) -> None:
        result = self.run_recipe('--dry-run', 'logs', '--sidecar')
        self.assertIn('slopctl" --sidecar logs game --follow', result.stderr)
        self.assertNotIn('Player.log', result.stderr)
        self.assertEqual(self.run_recipe('--evaluate', 'LOG').stdout.strip(), '')

    def test_sidecar_launch_passes_explicit_mode_and_shared_profile_override(self) -> None:
        result = self.run_recipe('--dry-run', 'PROFILE=/chosen/profile', 'sidecar-run')
        self.assertIn('--sidecar --game', result.stderr)
        self.assertIn('--profile "/chosen/profile"', result.stderr)
        self.assertNotIn('SLOPD_ENDPOINT=', result.stderr)
        result = self.run_recipe('--justfile', str(ROOT / 'mac/justfile'), '--dry-run', 'run')
        self.assertIn('--sidecar --game-exe', result.stderr)
        self.assertNotIn('--profile', result.stderr)

    def test_trace_recipe_queries_the_launcher_profile(self) -> None:
        result = self.run_recipe('--dry-run', 'trace-mod', 'true')
        self.assertIn('--sidecar paths profile', result.stderr)
        self.assertNotIn('.local/share', result.stderr)

    def test_terminal_benchmark_builds_the_selected_launcher_before_running(self) -> None:
        self.run_recipe('BUILD=release', 'bench-terminal')
        calls = [json.loads(line) for line in self.log.read_text().splitlines()]
        self.assertEqual([call['tool'] for call in calls], ['cargo', 'uv'])
        self.assertEqual(calls[0]['args'], ['build', '--release'])
        self.assertIn('bench/terminal-input/terminal-input-bench.py', calls[1]['args'])

    def test_invalid_build_fails_before_running_commands(self) -> None:
        result = self.run_recipe('BUILD=fast', 'daemon', success=False)
        self.assertIn('BUILD must be exactly debug or release', result.stderr)
        self.assertFalse(self.log.exists())

    def test_refresh_is_explicit_and_stages_licenses_last(self) -> None:
        self.run_recipe('refresh')
        modules = [call['args'][call['args'].index('-m') + 1] for call in self.calls('uv')]
        self.assertEqual(modules[-1], 'tools.licenses.stage_licenses')
        self.assertEqual(self.calls('cargo'), [])
        self.assertEqual(self.calls('dotnet'), [])

    def test_lint_forces_release_and_warnings_through_recursive_call(self) -> None:
        self.run_recipe('BUILD=debug', 'MOD_WARNINGS_AS_ERRORS=false', 'VERSION=1.2.3', 'lint-mod')
        self.assertEqual(self.calls('cargo'), [])
        (mod,) = [call for call in self.calls('dotnet') if 'mod/Source/SlopWorld/SlopWorld.csproj' in call['args']]
        self.assertIn('Release', mod['args'])
        self.assertIn('-p:TreatWarningsAsErrors=true', mod['args'])

    def test_csharp_formatting_scopes_discovery_to_source_folders(self) -> None:
        folders = [
            'mod/Source/SlopWorld',
            'mod/Tests',
            'bench/ipc/csharp',
            'bench/terminal-input/tests/http_transport',
        ]
        for recipe in ('format-mod', 'check-format-mod'):
            with self.subTest(recipe=recipe):
                self.log.unlink(missing_ok=True)
                self.run_recipe(recipe)
                calls = self.calls('dotnet')
                self.assertEqual([call['cwd'] for call in calls], [str(ROOT / folder) for folder in folders])
                for call in calls:
                    self.assertEqual(call['args'][:2], ['format', 'whitespace'])
                    self.assertEqual(call['args'][2], '.')
                    self.assertIn('--folder', call['args'])
                    self.assertIn('Client/Generated', call['args'])
                    self.assertEqual('--verify-no-changes' in call['args'], recipe == 'check-format-mod')

    def test_csharp_formatting_stops_after_a_folder_fails(self) -> None:
        self.env['RECIPE_TEST_FAIL'] = 'dotnet'
        for recipe in ('format-mod', 'check-format-mod'):
            with self.subTest(recipe=recipe):
                self.log.unlink(missing_ok=True)
                self.run_recipe(recipe, success=False)
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
            str(ROOT / 'mac/justfile'),
            'BUILD=release',
            'VERSION=1.2.3',
            f'MAC_RIMWORLD={app}',
            'mod',
            cwd=ROOT / 'mac',
        )
        (mod,) = [call for call in self.calls('dotnet') if 'mod/Source/SlopWorld/SlopWorld.csproj' in call['args']]
        self.assertEqual(mod['cwd'], str(ROOT))
        self.assertEqual(mod['env']['BUILD'], 'release')
        self.assertIn(f'-p:RimWorldManaged={managed}', mod['args'])
        self.assertIn('Release', mod['args'])

    def test_mac_sidecar_workspace_defaults_to_repository_root(self) -> None:
        result = self.run_recipe(
            '--justfile', str(ROOT / 'mac/justfile'), '--evaluate', 'SLOPCAR_WORKSPACE', cwd=ROOT / 'mac'
        )
        self.assertEqual(result.stdout.strip(), str(ROOT))
        result = self.run_recipe(
            '--justfile',
            str(ROOT / 'mac/justfile'),
            '--evaluate',
            f'SLOPCAR_WORKSPACE={self.directory}',
            'SLOPCAR_WORKSPACE',
        )
        self.assertEqual(result.stdout.strip(), str(self.directory))

    def test_typing_benchmark_forces_phase_and_preserves_other_settings(self) -> None:
        self.run_recipe(
            'BENCH_PHASE=history', 'BENCH_FILL_HISTORY=false', 'BENCH_RUN=typing trial', 'bench-terminal-typing'
        )
        (call,) = self.calls('uv')
        self.assertEqual(
            call['args'],
            [
                'run',
                '--locked',
                'python',
                'bench/terminal-input/terminal-input-bench.py',
                '--run',
                'typing trial',
                '--phase',
                'typing',
                '--fill-history',
            ],
        )

    def test_dependency_failure_stops_mod_build(self) -> None:
        self.env['RECIPE_TEST_FAIL'] = 'dotnet'
        self.run_recipe('VERSION=1.2.3', 'mod', success=False)
        self.assertEqual(self.calls('cargo'), [])
        (dependency,) = self.calls('dotnet')
        self.assertIn('mod/Dependencies/Runtime.csproj', dependency['args'])

    def test_mod_resolves_default_version_without_building_rust(self) -> None:
        self.run_recipe('mod')
        self.assertEqual(self.calls('cargo'), [])
        self.assertIn(
            ['run', '--locked', 'python', '-m', 'tools.version.mod_version'],
            [call['args'] for call in self.calls('uv')],
        )

    def test_version_override_skips_default_resolution(self) -> None:
        self.run_recipe('VERSION=1.2.3', 'mod')
        self.assertNotIn(
            ['run', '--locked', 'python', '-m', 'tools.version.mod_version'],
            [call['args'] for call in self.calls('uv')],
        )
        (mod,) = [call for call in self.calls('dotnet') if 'mod/Source/SlopWorld/SlopWorld.csproj' in call['args']]
        self.assertIn('-p:InformationalVersion=1.2.3', mod['args'])

    def test_daemon_build_does_not_require_python_generation(self) -> None:
        self.env['RECIPE_TEST_FAIL'] = 'uv'
        self.run_recipe('daemon')
        self.assertEqual(len(self.calls('cargo')), 1)
        self.assertEqual(self.calls('uv'), [])

    def test_checks_and_builds_have_no_generation_dependencies(self) -> None:
        generators = (
            'tools.docs.api_docs',
            'tools.assets.loading_font_atlas',
            'tools.assets.icons',
            'tools.assets.appicon',
            'tools.assets.favicon',
            'tools.licenses.rust_licenses',
            'tools.licenses.stage_licenses',
        )
        for recipe in ('all', 'test', 'ci', 'bench-build', 'docs', 'docs-serve', 'install', 'install-mod'):
            with self.subTest(recipe=recipe):
                result = self.run_recipe('--dry-run', recipe)
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
