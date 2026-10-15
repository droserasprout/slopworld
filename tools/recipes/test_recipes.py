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
from tools.recipes.dispatch import ACTIONS

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

    def test_help_exposes_tasks_and_specialist_menus(self) -> None:
        result = self.run_recipe()
        for group in ('Build and run', 'Check and fix', 'Maintain', 'Specialist commands'):
            self.assertIn('[' + group + ']', result.stdout)
        self.assertIn("build what='all'", result.stdout)
        self.assertNotIn('daemon ...', result.stdout)
        self.assertNotIn('_daemon', result.stdout)
        self.assertFalse(self.log.exists())

    def test_every_public_scope_resolves_to_an_existing_recipe(self) -> None:
        result = self.run_recipe('--dump', '--dump-format', 'json')
        catalog = json.loads(result.stdout)
        for action_name, action in ACTIONS.items():
            for scope, targets in action.targets.items():
                with self.subTest(action=action_name, scope=scope):
                    for target in targets:
                        parts = target.split('::')
                        owner = catalog
                        for module in parts[:-1]:
                            owner = owner['modules'][module]
                        self.assertIn(parts[-1], owner['recipes'])

    def test_default_build_excludes_optional_sidecar_and_uses_each_owner_once(self) -> None:
        self.run_recipe('VERSION=1.2.3', 'build')
        self.assertEqual([call['cwd'] for call in self.calls('cargo')], [str(ROOT / 'slopd')])
        dotnet = self.calls('dotnet')
        self.assertEqual(len(dotnet), 2)
        self.assertIn('mod/Dependencies/Runtime.csproj', dotnet[0]['args'])
        self.assertIn('mod/Source/SlopWorld/SlopWorld.csproj', dotnet[1]['args'])

    def test_regular_tests_exclude_sidecar_and_benchmarks(self) -> None:
        self.run_recipe('test')
        self.assertEqual([call['cwd'] for call in self.calls('cargo')], [str(ROOT / 'slopd')])
        modules = [call['args'] for call in self.calls('uv')]
        self.assertEqual(sum('pytest' in call for call in modules), 1)
        self.assertFalse(any(any('bench' in arg or arg == 'slopcar' for arg in call) for call in modules))
        self.assertTrue(any('tools.terminal.pager_geometry' in call for call in modules))
        self.assertTrue(any('tools.themes.validate_themes' in call for call in modules))
        self.assertTrue(any('run' in call['args'] for call in self.calls('dotnet')))

    def test_sidecar_has_no_top_level_component_aliases(self) -> None:
        for verb in ('build', 'install', 'format', 'lint', 'test', 'clean'):
            with self.subTest(verb=verb):
                self.run_recipe(verb, 'sidecar', success=False)
                self.assertFalse(self.log.exists())
        self.run_recipe('test', 'bench', success=False)
        self.assertFalse(self.log.exists())

    def test_sidecar_tests_own_rust_and_python_suites(self) -> None:
        self.run_recipe('sidecar', 'test')
        (cargo,) = self.calls('cargo')
        self.assertEqual(cargo['cwd'], str(ROOT / 'slopcar'))
        (python,) = self.calls('uv')
        self.assertEqual(python['args'][-2:], ['pytest', 'slopcar'])

    def test_benchmark_tests_are_only_in_the_bench_group(self) -> None:
        self.run_recipe('bench', 'test')
        calls = self.calls('uv')
        self.assertEqual(len(calls), 2)
        self.assertIn('bench/test_runner.py', calls[0]['args'])
        self.assertIn('bench/test_bench_report.py', calls[0]['args'])
        self.assertIn('bench/terminal-input', calls[1]['args'])

    def test_specialist_defaults_list_commands_without_running_tools(self) -> None:
        for owner in ('sidecar', 'release', 'image', 'mac', 'docs', 'bench', 'deps'):
            with self.subTest(owner=owner):
                result = self.run_recipe(owner)
                self.assertIn('Available recipes:', result.stdout)
                self.assertFalse(self.log.exists())

    def test_task_help_lists_scopes_and_default_without_running_tools(self) -> None:
        result = self.run_recipe('help', 'build')
        self.assertIn('all,daemon,mod', result.stdout)
        self.assertIn('daemon and mod by default', result.stdout)
        self.assertFalse(self.log.exists())

    def test_invalid_scope_or_unsupported_arguments_fail_before_tools(self) -> None:
        for command in (
            ('build', 'tools'),
            ('coverage', 'sidecar'),
            ('install', 'typo'),
            ('test', 'all', '-k', 'failure'),
        ):
            with self.subTest(command=command):
                self.run_recipe(*command, success=False)
                self.assertFalse(self.log.exists())

    def test_ci_workflow_uses_the_same_ordered_stages_as_local_ci(self) -> None:
        result = self.run_recipe('--dump', '--dump-format', 'json')
        catalog = json.loads(result.stdout)
        local = [entry['recipe'] for entry in catalog['modules']['_ci']['recipes']['all']['dependencies']]
        workflow = (ROOT / '.github/workflows/test.yml').read_text()
        self.assertEqual(re.findall(r'run: just ci ([\w-]+)', workflow), local)

    def test_ci_test_stage_executes_each_suite_instead_of_forwarding_recipe_names(self) -> None:
        self.run_recipe('ci', 'tests')
        args = [call['args'] for call in self.calls('uv')]
        self.assertTrue(any('tools.themes.validate_themes' in call for call in args))
        self.assertFalse(any(any('bench' in arg or arg == 'slopcar' for arg in call) for call in args))
        self.assertTrue(any('pytest' in call for call in args))
        self.assertTrue(any('tools.terminal.pager_geometry' in call for call in args))
        self.assertFalse(any(any('::' in argument for argument in call) for call in args))

    def test_python_lint_checks_without_applying_fixes(self) -> None:
        self.run_recipe('lint', 'tools')
        args = [call['args'] for call in self.calls('uv')]
        self.assertTrue(any('format' in call and '--check' in call for call in args))
        self.assertFalse(any('--fix' in call for call in args))

    def test_csharp_lint_does_not_compile_the_mod(self) -> None:
        self.run_recipe('lint', 'mod')
        self.assertFalse(any('build' in call['args'] for call in self.calls('dotnet')))

    def test_pytest_selection_preserves_spaces_in_arguments(self) -> None:
        self.run_recipe('test', 'tools', 'tools/recipes', '-k', 'release or failure')
        (call,) = self.calls('uv')
        self.assertEqual(call['args'][-3:], ['tools/recipes', '-k', 'release or failure'])

    def test_selected_runner_receives_help_and_separator_unchanged(self) -> None:
        self.run_recipe('test', 'tools', '--help')
        (call,) = self.calls('uv')
        self.assertEqual(call['args'][-1], '--help')
        self.log.unlink()
        self.run_recipe('test', 'daemon', '--', '--nocapture')
        (call,) = self.calls('cargo')
        self.assertEqual(call['args'][-2:], ['--', '--nocapture'])

    def test_benchmark_suite_and_options_are_forwarded(self) -> None:
        self.run_recipe('BUILD=release', 'bench', 'run', 'daemon', '--run', 'trial with spaces')
        (call,) = self.calls('uv')
        self.assertEqual(call['args'][-6:], ['--suite', 'daemon', '--build', 'release', '--run', 'trial with spaces'])

    def test_native_targets_include_all_distributions(self) -> None:
        for distribution in ('arch', 'debian', 'fedora'):
            self.log.unlink(missing_ok=True)
            self.run_recipe('package', distribution)
            (call,) = self.calls('uv')
            self.assertEqual(call['args'][-3:], ['-m', 'tools.release.native', distribution])
            self.log.unlink(missing_ok=True)
            self.run_recipe('CONTAINER=container', 'image', distribution)
            (call,) = self.calls('container')
            self.assertIn(f'packaging/{distribution}/Dockerfile', call['args'])
            self.assertIn(f'slopworld-{distribution}-release', call['args'])
            self.log.unlink(missing_ok=True)
            self.run_recipe('CONTAINER=container', 'test', 'package', distribution)
            (call,) = self.calls('container')
            self.assertIn(f'tools/release/test_{distribution}.py', call['args'])
            self.assertEqual('--privileged' in call['args'], distribution == 'arch')

    def test_package_requires_a_distribution_and_has_no_subcommands(self) -> None:
        for command in (
            ('package',),
            ('package', 'test'),
            ('package', 'release'),
            ('test', 'package'),
            ('test', 'package', 'typo'),
        ):
            with self.subTest(command=command):
                self.run_recipe(*command, success=False)
                self.assertFalse(self.log.exists())
        result = self.run_recipe('help', 'package')
        self.assertIn('arch', result.stdout)
        self.assertIn('debian', result.stdout)
        self.assertIn('fedora', result.stdout)

    def test_package_help_uses_the_component_parser_without_running_tools(self) -> None:
        outputs = [
            self.run_recipe('package', '-h').stdout,
            self.run_recipe('package', '--help').stdout,
            self.run_recipe('help', 'package').stdout,
        ]
        self.assertTrue(all(output == outputs[0] for output in outputs))
        self.assertIn('usage: just package', outputs[0])
        self.assertIn('{arch,debian,fedora}', outputs[0])
        self.assertFalse(self.log.exists())

    def test_sidecar_image_builds_its_launcher_and_preserves_source_path(self) -> None:
        self.run_recipe(f'SLOPCAR={self.directory / "sidecar"}', 'sidecar', 'image')
        calls = [json.loads(line) for line in self.log.read_text().splitlines()]
        self.assertEqual([call['tool'] for call in calls], ['cargo', 'sidecar'])
        self.assertEqual(calls[0]['cwd'], str(ROOT / 'slopcar'))
        self.assertEqual(calls[1]['args'], ['build', '--source', str(ROOT)])

    def test_invalid_image_target_and_failed_launcher_never_build_images(self) -> None:
        for what in ('typo', 'sidecar', 'build'):
            self.run_recipe('image', what, success=False)
            self.assertFalse(self.log.exists())
        help_text = self.run_recipe('help', 'image').stdout
        for what in ('arch', 'debian', 'fedora'):
            self.assertIn(what, help_text)
        self.assertFalse(self.log.exists())
        self.env['RECIPE_TEST_FAIL'] = 'cargo'
        self.run_recipe(f'SLOPCAR={self.directory / "sidecar"}', 'sidecar', 'image', success=False)
        self.assertEqual(len(self.calls('cargo')), 1)
        self.assertEqual(self.calls('sidecar'), [])

    def test_release_workflow_and_private_mod_staging_use_their_owners(self) -> None:
        self.run_recipe('release', 'package')
        (call,) = self.calls('uv')
        self.assertEqual(call['args'][-3:], ['-m', 'tools.release.publish', 'package'])
        self.log.unlink()
        destination = str(self.directory / 'source mod')
        self.run_recipe('_stage-mod', destination)
        (call,) = self.calls('uv')
        self.assertEqual(call['args'][-3:], ['-m', 'tools.release.source_mod', destination])

    def test_macos_launch_and_install_preserve_selected_image(self) -> None:
        self.env['SLOPCAR_IMAGE'] = 'custom-agents:local'
        for recipe in ('install', 'run', 'doctor'):
            result = self.run_recipe('--dry-run', 'mac', recipe)
            self.assertNotIn('image build', result.stderr)
            self.assertNotIn('build --source', result.stderr)

    def test_release_build_uses_environment_and_command_override(self) -> None:
        self.env['BUILD'] = 'debug'
        self.run_recipe('BUILD=release', 'build', 'daemon')
        (call,) = self.calls('cargo')
        self.assertEqual(call['args'], ['build', '--release'])
        self.assertEqual(call['env']['BUILD'], 'release')
        self.assertEqual(call['cwd'], str(ROOT / 'slopd'))

    def test_mod_build_uses_game_root_and_preserves_reference_override(self) -> None:
        self.env.pop('MANAGED', None)
        game = self.directory / 'Steam game'
        self.run_recipe(f'RIMWORLD={game}', 'VERSION=1.2.3', 'build', 'mod')
        self.assertIn(f'-p:RimWorldManaged={game}', self.calls('dotnet')[-1]['args'])
        self.log.unlink()
        managed = self.directory / 'custom references'
        self.run_recipe(f'RIMWORLD={game}', f'MANAGED={managed}', 'VERSION=1.2.3', 'build', 'mod')
        self.assertIn(f'-p:RimWorldManaged={managed}', self.calls('dotnet')[-1]['args'])

    def test_task_selection_preserves_frozen_flags_and_custom_build_paths(self) -> None:
        self.run_recipe('BUILD=release', 'CARGOFLAGS=--release --frozen', 'build', 'daemon')
        (call,) = self.calls('cargo')
        self.assertEqual(call['args'], ['build', '--release', '--frozen'])
        self.log.unlink()
        self.run_recipe(f'RUNNER={self.directory / "runner"}', 'TEST_PROJECT=custom/test.csproj', 'install', 'mod')
        installer = next(call for call in self.calls('uv') if 'tools.host.install_mod' in call['args'])
        self.assertIn(str(self.directory / 'runner'), installer['args'])
        self.log.unlink()
        self.run_recipe('TEST_PROJECT=custom/test.csproj', 'test', 'mod')
        self.assertTrue(any('custom/test.csproj' in call['args'] for call in self.calls('dotnet')))

    def test_rust_cache_wrapper_discovery_and_explicit_overrides(self) -> None:
        (self.directory / 'sccache').symlink_to(self.directory / 'stub')
        self.run_recipe('build', 'daemon')
        (call,) = self.calls('cargo')
        self.assertEqual(call['env']['RUSTC_WRAPPER'], str(self.directory / 'sccache'))
        self.assertEqual(call['env']['SCCACHE_DIR'], str(Path.home() / '.cache/sccache'))
        for wrapper in ('', '/custom/wrapper'):
            with self.subTest(wrapper=wrapper):
                self.log.unlink()
                self.run_recipe(f'RUSTC_WRAPPER={wrapper}', 'SCCACHE_DIR=/custom/cache', 'build', 'daemon')
                (call,) = self.calls('cargo')
                self.assertEqual(call['env']['RUSTC_WRAPPER'], wrapper)
                self.assertEqual(call['env']['SCCACHE_DIR'], '/custom/cache')

    def test_logs_delegate_path_and_mode_selection_to_slopctl(self) -> None:
        self.run_recipe(f'SLOPCTL={self.directory / "slopctl"}', 'logs', '--sidecar')
        (call,) = self.calls('slopctl')
        self.assertEqual(call['args'], ['--sidecar', 'logs', 'game', '--follow'])
        self.assertEqual(self.run_recipe('--evaluate', 'LOG').stdout.strip(), '')

    def test_sidecar_launch_passes_explicit_mode_and_shared_profile_override(self) -> None:
        self.run_recipe(f'RUNNER={self.directory / "runner"}', 'PROFILE=/chosen profile', 'sidecar', 'run')
        (call,) = self.calls('runner')
        self.assertEqual(call['args'][-2:], ['--profile', '/chosen profile'])
        self.assertEqual(call['args'][:2], ['--sidecar', '--game'])
        self.log.unlink()
        self.run_recipe(f'RUNNER={self.directory / "runner"}', 'mac', 'run')
        (call,) = self.calls('runner')
        self.assertEqual(call['args'][:2], ['--sidecar', '--game-exe'])
        self.assertNotIn('--profile', call['args'])

    def test_trace_recipe_queries_the_launcher_profile(self) -> None:
        self.run_recipe(f'RUNNER={self.directory / "runner"}', 'trace', 'capture', 'true')
        (call,) = self.calls('runner')
        self.assertEqual(call['args'], ['--sidecar', 'paths', 'profile'])

    def test_terminal_benchmark_builds_the_selected_launcher_before_running(self) -> None:
        self.run_recipe('BUILD=release', 'bench', 'terminal')
        calls = [json.loads(line) for line in self.log.read_text().splitlines()]
        self.assertEqual([call['tool'] for call in calls], ['cargo', 'uv'])
        self.assertEqual(calls[0]['args'], ['build', '--release'])
        self.assertIn('bench/terminal-input/terminal-input-bench.py', calls[1]['args'])

    def test_invalid_build_fails_before_running_commands(self) -> None:
        result = self.run_recipe('BUILD=fast', 'build', 'daemon', success=False)
        self.assertIn('BUILD must be exactly debug or release', result.stderr)
        self.assertFalse(self.log.exists())

    def test_refresh_is_explicit_and_stages_licenses_last(self) -> None:
        self.run_recipe('generate', 'all')
        modules = [call['args'][call['args'].index('-m') + 1] for call in self.calls('uv')]
        self.assertEqual(modules[-1], 'tools.licenses.stage_licenses')
        self.assertEqual(self.calls('cargo'), [])
        self.assertEqual(self.calls('dotnet'), [])
        self.assertIn('tools.licenses.sidecar_licenses', modules)
        self.assertFalse(any('lock' in call['args'] for call in self.calls('uv')))

    def test_lock_refresh_updates_every_graph_before_staging_notices(self) -> None:
        self.run_recipe('MOD_DEPS_LOCKED=true', 'deps', 'update', 'all')
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
        self.run_recipe('deps', 'update', 'all', success=False)
        self.assertEqual(len(self.calls('cargo')), 1)
        self.assertEqual(self.calls('dotnet'), [])
        self.assertEqual([call['args'] for call in self.calls('uv')], [['lock', '--upgrade']])

    def test_build_check_forces_release_and_warnings_through_recursive_call(self) -> None:
        self.run_recipe(
            'BUILD=debug',
            'MOD_WARNINGS_AS_ERRORS=false',
            'VERSION=1.2.3',
            'MANAGED=/game assemblies',
            'MOD_DLL=/built mod/SlopWorld.dll',
            'check',
            'mod-build',
        )
        self.assertEqual(self.calls('cargo'), [])
        (mod,) = [call for call in self.calls('dotnet') if 'mod/Source/SlopWorld/SlopWorld.csproj' in call['args']]
        self.assertIn('Release', mod['args'])
        self.assertIn('-p:TreatWarningsAsErrors=true', mod['args'])
        (bindings,) = [call for call in self.calls('dotnet') if '--game-bindings' in call['args']]
        self.assertIn('Release', bindings['args'])
        self.assertEqual(bindings['args'][-3:], ['--game-bindings', '/game assemblies', '/built mod/SlopWorld.dll'])

    def test_csharp_formatting_scopes_discovery_to_source_folders(self) -> None:
        folders = [
            'mod/Source/SlopWorld',
            'mod/Tests',
            'bench/ipc/csharp',
        ]
        for operation in ('format', 'lint'):
            with self.subTest(operation=operation):
                self.log.unlink(missing_ok=True)
                self.run_recipe(operation, 'mod')
                calls = self.calls('dotnet')
                self.assertEqual(calls[0]['args'], ['tool', 'restore'])
                calls = calls[1:]
                self.assertEqual([call['cwd'] for call in calls], [str(ROOT / folder) for folder in folders])
                for call in calls:
                    action = 'check' if operation == 'lint' else 'format'
                    self.assertEqual(call['args'], ['csharpier', action, '.'])

    def test_csharp_formatting_stops_after_a_folder_fails(self) -> None:
        self.env['RECIPE_TEST_FAIL'] = 'dotnet'
        for operation in ('format', 'lint'):
            with self.subTest(operation=operation):
                self.log.unlink(missing_ok=True)
                self.run_recipe(operation, 'mod', success=False)
                self.assertEqual(len(self.calls('dotnet')), 1)

    def test_mac_mod_passes_native_assemblies_and_release_selection(self) -> None:
        app = self.directory / 'RimWorld.app'
        game = app / 'Contents/MacOS/RimWorld'
        game.parent.mkdir(parents=True)
        game.touch()
        game.chmod(0o755)
        managed = app / 'Contents/MacOS'
        managed.mkdir(parents=True, exist_ok=True)
        for assembly in ('Assembly-CSharp.dll', 'UnityStubs.dll', 'Waylith.dll'):
            (managed / assembly).touch()
        (app / 'Mods').mkdir()
        self.run_recipe(
            '--justfile',
            str(ROOT / 'justfile'),
            'BUILD=release',
            'VERSION=1.2.3',
            f'MAC_RIMWORLD={app}',
            'mac',
            'mod-build',
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

    def test_typing_benchmark_forces_phase_and_preserves_other_settings(self) -> None:
        self.run_recipe('BENCH_RUN=typing trial', 'bench', 'terminal', '--phase', 'typing', '--fill-history')
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
        self.run_recipe('VERSION=1.2.3', 'build', 'mod', success=False)
        self.assertEqual(self.calls('cargo'), [])
        (dependency,) = self.calls('dotnet')
        self.assertIn('mod/Dependencies/Runtime.csproj', dependency['args'])

    def test_mod_resolves_default_version_without_building_rust(self) -> None:
        self.run_recipe('build', 'mod')
        self.assertEqual(self.calls('cargo'), [])
        self.assertIn(
            ['run', '--locked', 'python', '-m', 'tools.version.mod_version'],
            [call['args'] for call in self.calls('uv')],
        )

    def test_version_override_skips_default_resolution(self) -> None:
        self.run_recipe('VERSION=1.2.3', 'build', 'mod')
        self.assertNotIn(
            ['run', '--locked', 'python', '-m', 'tools.version.mod_version'],
            [call['args'] for call in self.calls('uv')],
        )
        (mod,) = [call for call in self.calls('dotnet') if 'mod/Source/SlopWorld/SlopWorld.csproj' in call['args']]
        self.assertIn('-p:InformationalVersion=1.2.3', mod['args'])

    def test_daemon_build_does_not_require_python_generation(self) -> None:
        self.env['RECIPE_TEST_FAIL'] = 'uv'
        self.run_recipe('build', 'daemon')
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
        for command in (
            ('build',),
            ('test',),
            ('ci',),
            ('bench', 'run'),
            ('docs', 'build'),
            ('docs', 'serve'),
            ('install',),
            ('install', 'mod'),
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
        self.run_recipe('VERSION=1.2.3', 'install', 'mod', success=False)
        self.assertEqual(len(self.calls('cargo')), 1)


if __name__ == '__main__':
    unittest.main()
