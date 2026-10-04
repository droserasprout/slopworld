#!/usr/bin/env python3
"""Protect the mod/daemon ownership boundary of the environment reference."""

import unittest

from reference import ROOT, env_inventory


class EnvironmentInventoryTests(unittest.TestCase):
    def test_build_and_tooling_variables_are_excluded(self):
        names, dynamic = env_inventory({
            ROOT / "just/config.just": 'export BUILD := env("BUILD", "debug")',
            ROOT / "justfile": 'value := env("JUST_SETTING", "")',
            ROOT / "tools/example.rs": 'std::env::var("TOOL_SETTING"); std::env::vars();',
            ROOT / "bench/example.rs": 'std::env::var("BENCH_SETTING");',
            ROOT / "docs/example.md": '$DOC_SETTING',
        })
        self.assertEqual(names, {})
        self.assertEqual(dynamic, [])

    def test_mod_daemon_launcher_and_preset_variables_are_kept(self):
        names, _ = env_inventory({
            ROOT / "mod/Source/Endpoint.cs": 'Environment.GetEnvironmentVariable("SLOPD_ENDPOINT");',
            ROOT / "slopd/src/config.rs": 'std::env::var_os("SLOPD_CONFIG");',
            ROOT / "slopd/src/bin/slopworld.rs": 'option_env_nonempty("SLOPCAR_PROFILE");',
            ROOT / "slopd/presets/example.toml": 'path = "${XDG_DATA_HOME}/slopworld"',
        })
        self.assertEqual(set(names), {"SLOPD_ENDPOINT", "SLOPD_CONFIG",
                                     "SLOPCAR_PROFILE", "XDG_DATA_HOME"})

    def test_shared_variable_has_only_mod_and_daemon_sources(self):
        names, _ = env_inventory({
            ROOT / "slopd/src/config.rs": 'std::env::var("SLOPD_ENDPOINT");',
            ROOT / "just/config.just": 'value := env("SLOPD_ENDPOINT", "")',
            ROOT / "docs/example.md": '$SLOPD_ENDPOINT',
        })
        self.assertEqual([item.path for item in names["SLOPD_ENDPOINT"]],
                         ["slopd/src/config.rs"])


if __name__ == "__main__":
    unittest.main()
