#!/usr/bin/env python3
"""Exercise recipe configuration and recursive overrides without building or launching."""

import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parent.parent
JUST = shutil.which("just")


@unittest.skipUnless(JUST, "just is required")
class RecipeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="slopworld recipes ")
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.log = self.directory / "calls.jsonl"
        stub = self.directory / "stub"
        stub.write_text(f"#!{sys.executable}\n" + '''
import json, os, pathlib, sys
tool = pathlib.Path(sys.argv[0]).name
if tool == "uname":
    print("Darwin")
    sys.exit(0)
keys = ("BUILD", "CARGOFLAGS", "MOD_DEPS_LOCKED", "SLOPCAR_PROFILE", "JUST_CMD")
with open(os.environ["RECIPE_TEST_LOG"], "a") as log:
    log.write(json.dumps({"tool": tool, "args": sys.argv[1:], "cwd": os.getcwd(),
        "env": {key: os.environ.get(key) for key in keys}}) + "\\n")
if tool == os.environ.get("RECIPE_TEST_FAIL"):
    sys.exit(23)
''')
        stub.chmod(0o755)
        for tool in ("cargo", "dotnet", "python3", "uname"):
            (self.directory / tool).symlink_to(stub)
        self.env = os.environ.copy()
        # Make results independent of the caller's selected build/profile/tool settings.
        settings = "\n".join(path.read_text() for path in (ROOT / "just").glob("*.just"))
        for key in re.findall(r"^(?:export )?([A-Z][A-Z_]+) :=", settings, re.MULTILINE):
            self.env.pop(key, None)
        self.env.update(PATH=str(self.directory) + os.pathsep + os.environ["PATH"],
                        RECIPE_TEST_LOG=str(self.log))

    def run_recipe(self, *args, cwd=ROOT, success=True):
        result = subprocess.run([JUST, *args], cwd=cwd, env=self.env,
                                capture_output=True, text=True)
        if success:
            self.assertEqual(result.returncode, 0, result.stderr)
        else:
            self.assertNotEqual(result.returncode, 0)
        return result

    def calls(self, tool):
        if not self.log.exists():
            return []
        return [call for line in self.log.read_text().splitlines()
                if (call := json.loads(line))["tool"] == tool]

    def test_release_build_uses_environment_and_command_override(self):
        self.env["BUILD"] = "debug"
        self.run_recipe("BUILD=release", "daemon")
        call, = self.calls("cargo")
        self.assertEqual(call["args"], ["build", "--release"])
        self.assertEqual(call["env"]["BUILD"], "release")
        self.assertEqual(call["cwd"], str(ROOT / "slopd"))

    def test_invalid_build_fails_before_running_commands(self):
        result = self.run_recipe("BUILD=fast", "daemon", success=False)
        self.assertIn("BUILD must be exactly debug or release", result.stderr)
        self.assertFalse(self.log.exists())

    def test_shared_generation_runs_once_for_multiple_builds(self):
        self.run_recipe("VERSION=1.2.3", "all")
        self.assertEqual(len([call for call in self.calls("python3")
                              if call["args"] == ["tools/api_contract.py"]]), 1)
        self.assertEqual(len(self.calls("cargo")), 1)

    def test_lint_forces_release_and_warnings_through_recursive_call(self):
        self.run_recipe("BUILD=debug", "MOD_WARNINGS_AS_ERRORS=false",
                        "VERSION=1.2.3", "lint-mod")
        self.assertEqual(self.calls("cargo"), [])
        mod, = [call for call in self.calls("dotnet")
                if "mod/Source/SlopWorld/SlopWorld.csproj" in call["args"]]
        self.assertIn("Release", mod["args"])
        self.assertIn("-p:TreatWarningsAsErrors=true", mod["args"])

    def test_mac_mod_passes_native_assemblies_and_release_selection(self):
        app = self.directory / "RimWorld.app"
        game = app / "Contents/MacOS/RimWorld by Ludeon Studios"
        game.parent.mkdir(parents=True)
        game.touch()
        game.chmod(0o755)
        managed = app / "Contents/Resources/Data/Managed"
        managed.mkdir(parents=True)
        (managed / "Assembly-CSharp.dll").touch()
        (app / "Mods").mkdir()
        self.run_recipe("BUILD=release", "VERSION=1.2.3",
                        f"MAC_RIMWORLD={app}", "mac-mod")
        mod, = [call for call in self.calls("dotnet")
                if "mod/Source/SlopWorld/SlopWorld.csproj" in call["args"]]
        self.assertIn(f"-p:RimWorldManaged={managed}", mod["args"])
        self.assertIn("Release", mod["args"])

    def test_typing_benchmark_forces_phase_and_preserves_other_settings(self):
        self.run_recipe("BENCH_PHASE=history", "BENCH_FILL_HISTORY=false",
                        "BENCH_RUN=typing trial", "bench-terminal-typing")
        call, = self.calls("python3")
        self.assertEqual(call["args"], ["bench/terminal-input/terminal-input-bench.py",
                                       "--run", "typing trial", "--phase", "typing",
                                       "--fill-history"])

    def test_native_build_does_not_export_sidecar_profile(self):
        self.run_recipe("daemon", cwd=ROOT / "slopd")
        call, = self.calls("cargo")
        self.assertIsNone(call["env"]["SLOPCAR_PROFILE"])

    def test_dependency_failure_stops_mod_build(self):
        self.env["RECIPE_TEST_FAIL"] = "dotnet"
        self.run_recipe("VERSION=1.2.3", "mod", success=False)
        self.assertEqual(self.calls("cargo"), [])
        dependency, = self.calls("dotnet")
        self.assertIn("mod/Dependencies/Runtime.csproj", dependency["args"])

    def test_mod_resolves_default_version_without_building_rust(self):
        self.run_recipe("mod")
        self.assertEqual(self.calls("cargo"), [])
        self.assertIn(["tools/mod_version.py"],
                      [call["args"] for call in self.calls("python3")])

    def test_version_override_skips_default_resolution(self):
        self.run_recipe("VERSION=1.2.3", "mod")
        self.assertNotIn(["tools/mod_version.py"],
                         [call["args"] for call in self.calls("python3")])
        mod, = [call for call in self.calls("dotnet")
                if "mod/Source/SlopWorld/SlopWorld.csproj" in call["args"]]
        self.assertIn("-p:InformationalVersion=1.2.3", mod["args"])

    def test_install_mod_still_builds_its_launcher(self):
        # Fail during dependency staging so this test never invokes an installer.
        self.env["RECIPE_TEST_FAIL"] = "dotnet"
        self.run_recipe("VERSION=1.2.3", "install-mod", success=False)
        self.assertEqual(len(self.calls("cargo")), 1)


if __name__ == "__main__":
    unittest.main()
