#!/usr/bin/env python3
"""Protect the mod/daemon ownership boundary of the environment reference."""

import unittest

from tools import ROOT
from tools.docs.reference import api_routes
from tools.docs.reference import cli_inventory
from tools.docs.reference import env_inventory


class EnvironmentInventoryTests(unittest.TestCase):
    def test_generic_lookups_are_dynamic_and_path_overrides_are_named(self) -> None:
        path = ROOT / 'slopd/src/example.rs'
        names, dynamic = env_inventory(
            {path: 'env::var(name);\nenv::var_os(variable);\ncrate::paths::override_path("SLOPD_CONFIG_ROOT", base);'}
        )
        self.assertEqual(set(names), {'SLOPD_CONFIG_ROOT'})
        self.assertEqual([item.line for item in dynamic], [1, 2])

    def test_storage_root_overrides_are_discovered(self) -> None:
        names, _ = env_inventory(
            {
                ROOT / 'slopd/src/paths.rs': """
            override_path("SLOPD_CONFIG_ROOT", default);
            crate::paths::override_path("SLOPD_DATA", default);
        """
            }
        )
        self.assertEqual(set(names), {'SLOPD_CONFIG_ROOT', 'SLOPD_DATA'})

    def test_build_and_tooling_variables_are_excluded(self) -> None:
        names, dynamic = env_inventory(
            {
                ROOT / 'just/config.just': 'export BUILD := env("BUILD", "debug")',
                ROOT / 'justfile': 'value := env("JUST_SETTING", "")',
                ROOT / 'tools/example.rs': 'std::env::var("TOOL_SETTING"); std::env::vars();',
                ROOT / 'bench/example.rs': 'std::env::var("BENCH_SETTING");',
                ROOT / 'docs/example.md': '$DOC_SETTING',
            }
        )
        self.assertEqual(names, {})
        self.assertEqual(dynamic, [])

    def test_mod_daemon_launcher_and_preset_variables_are_kept(self) -> None:
        names, _ = env_inventory(
            {
                ROOT / 'mod/Source/Endpoint.cs': 'Environment.GetEnvironmentVariable("SLOPD_ENDPOINT");',
                ROOT / 'slopd/src/config.rs': 'std::env::var_os("SLOPD_CONFIG_ROOT");',
                ROOT / 'slopd/src/bin/slopworld.rs': 'option_env_nonempty("SLOPCAR_PROFILE");',
                ROOT / 'slopd/presets/example.toml': 'path = "${XDG_DATA_HOME}/slopworld"',
            }
        )
        self.assertEqual(set(names), {'SLOPD_ENDPOINT', 'SLOPD_CONFIG_ROOT', 'SLOPCAR_PROFILE', 'XDG_DATA_HOME'})

    def test_shared_variable_has_only_mod_and_daemon_sources(self) -> None:
        names, _ = env_inventory(
            {
                ROOT / 'slopd/src/config.rs': 'std::env::var("SLOPD_ENDPOINT");',
                ROOT / 'just/config.just': 'value := env("SLOPD_ENDPOINT", "")',
                ROOT / 'docs/example.md': '$SLOPD_ENDPOINT',
            }
        )
        self.assertEqual([item.path for item in names['SLOPD_ENDPOINT']], ['slopd/src/config.rs'])


class CommandInventoryTests(unittest.TestCase):
    def test_mac_commands_use_the_separate_justfile(self) -> None:
        commands, _ = cli_inventory(
            {
                ROOT / 'just/build.just': '# Build shared code\nmod:\n',
                ROOT / 'mac/justfile': '# Build the native mod\nmod:\n',
            }
        )
        self.assertEqual({command for _, command, _ in commands}, {'just mod', 'just --justfile mac/justfile mod'})


class RouteInventoryTests(unittest.TestCase):
    def test_unresolved_path_fails_instead_of_omitting_route(self) -> None:
        with self.assertRaisesRegex(ValueError, 'unresolved API route path'):
            api_routes({ROOT / 'slopd/src/api/router.rs': 'fn root_routes() { router.route(UNKNOWN, get(handler)) }'})

    def test_documented_http_routes_match_wire_contract(self) -> None:
        from tools.docs.api_docs import render

        self.assertIn('# API route inventory', render())


if __name__ == '__main__':
    unittest.main()
