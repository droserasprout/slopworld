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

from tools import ROOT

JUST = shutil.which('just')


@unittest.skipUnless(JUST, 'just is required')
class RecipeTests(unittest.TestCase):
    def setUp(self):
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
keys = ("BUILD", "CARGOFLAGS", "MOD_DEPS_LOCKED", "SLOPCAR_PROFILE", "JUST_CMD")
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

    def run_recipe(self, *args, cwd=ROOT, success=True):
        result = subprocess.run([JUST, *args], cwd=cwd, env=self.env, capture_output=True, text=True)
        if success:
            self.assertEqual(result.returncode, 0, result.stderr)
        else:
            self.assertNotEqual(result.returncode, 0)
        return result

    def calls(self, tool):
        if not self.log.exists():
            return []
        return [call for line in self.log.read_text().splitlines() if (call := json.loads(line))['tool'] == tool]

    def test_release_build_uses_environment_and_command_override(self):
        self.env['BUILD'] = 'debug'
        self.run_recipe('BUILD=release', 'daemon')
        (call,) = self.calls('cargo')
        self.assertEqual(call['args'], ['build', '--release'])
        self.assertEqual(call['env']['BUILD'], 'release')
        self.assertEqual(call['cwd'], str(ROOT / 'slopd'))

    def test_local_release_recipes_use_the_shared_orchestrator(self):
        for recipe, action in (('release-package', 'package'), ('release-latest', 'publish')):
            with self.subTest(recipe=recipe):
                self.run_recipe(recipe)
                call = self.calls('uv')[-1]
                self.assertEqual(call['args'], ['run', '--locked', 'python', '-m', 'tools.release.latest', action])
                self.assertEqual(call['env']['JUST_CMD'], JUST)
        self.assertEqual(self.calls('cargo'), [])

    def test_invalid_build_fails_before_running_commands(self):
        result = self.run_recipe('BUILD=fast', 'daemon', success=False)
        self.assertIn('BUILD must be exactly debug or release', result.stderr)
        self.assertFalse(self.log.exists())

    def test_shared_generation_runs_once_for_multiple_builds(self):
        self.run_recipe('VERSION=1.2.3', 'all')
        self.assertEqual(
            len(
                [
                    call
                    for call in self.calls('uv')
                    if call['args'] == ['run', '--locked', 'python', '-m', 'tools.protocol.api_contract']
                ]
            ),
            1,
        )
        self.assertEqual(len(self.calls('cargo')), 1)

    def test_lint_forces_release_and_warnings_through_recursive_call(self):
        self.run_recipe('BUILD=debug', 'MOD_WARNINGS_AS_ERRORS=false', 'VERSION=1.2.3', 'lint-mod')
        self.assertEqual(self.calls('cargo'), [])
        (mod,) = [call for call in self.calls('dotnet') if 'mod/Source/SlopWorld/SlopWorld.csproj' in call['args']]
        self.assertIn('Release', mod['args'])
        self.assertIn('-p:TreatWarningsAsErrors=true', mod['args'])

    def test_mac_mod_passes_native_assemblies_and_release_selection(self):
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

    def test_root_commands_exclude_the_mac_workflow(self):
        result = self.run_recipe('--list')
        self.assertNotIn('mac-', result.stdout)
        self.assertNotIn('macOS', result.stdout)
        result = self.run_recipe('--evaluate')
        self.assertNotIn('MAC_RIMWORLD', result.stdout)

    def test_mac_sidecar_workspace_defaults_to_repository_root(self):
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

    def test_typing_benchmark_forces_phase_and_preserves_other_settings(self):
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

    def test_native_build_does_not_export_sidecar_profile(self):
        self.run_recipe('daemon', cwd=ROOT / 'slopd')
        (call,) = self.calls('cargo')
        self.assertIsNone(call['env']['SLOPCAR_PROFILE'])

    def test_dependency_failure_stops_mod_build(self):
        self.env['RECIPE_TEST_FAIL'] = 'dotnet'
        self.run_recipe('VERSION=1.2.3', 'mod', success=False)
        self.assertEqual(self.calls('cargo'), [])
        (dependency,) = self.calls('dotnet')
        self.assertIn('mod/Dependencies/Runtime.csproj', dependency['args'])

    def test_mod_resolves_default_version_without_building_rust(self):
        self.run_recipe('mod')
        self.assertEqual(self.calls('cargo'), [])
        self.assertIn(
            ['run', '--locked', 'python', '-m', 'tools.version.mod_version'],
            [call['args'] for call in self.calls('uv')],
        )

    def test_version_override_skips_default_resolution(self):
        self.run_recipe('VERSION=1.2.3', 'mod')
        self.assertNotIn(
            ['run', '--locked', 'python', '-m', 'tools.version.mod_version'],
            [call['args'] for call in self.calls('uv')],
        )
        (mod,) = [call for call in self.calls('dotnet') if 'mod/Source/SlopWorld/SlopWorld.csproj' in call['args']]
        self.assertIn('-p:InformationalVersion=1.2.3', mod['args'])

    def test_python_environment_failure_stops_build_before_cargo(self):
        self.env['RECIPE_TEST_FAIL'] = 'uv'
        self.run_recipe('daemon', success=False)
        self.assertEqual(self.calls('cargo'), [])
        (call,) = self.calls('uv')
        self.assertEqual(call['args'], ['run', '--locked', 'python', '-m', 'tools.protocol.api_contract'])

    def test_install_mod_still_builds_its_launcher(self):
        # Fail during dependency staging so this test never invokes an installer.
        self.env['RECIPE_TEST_FAIL'] = 'dotnet'
        self.run_recipe('VERSION=1.2.3', 'install-mod', success=False)
        self.assertEqual(len(self.calls('cargo')), 1)


if __name__ == '__main__':
    unittest.main()
