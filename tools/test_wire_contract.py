#!/usr/bin/env python3
"""Tests for shared definition loading and generated bindings."""

from __future__ import annotations

import sys
import shutil
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import wire_contract  # noqa: E402


class WireContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.data = wire_contract.load()["protocol"]
        cls.generated = wire_contract.rust(cls.data)

    def test_event_declaration_has_generated_tag_mapping(self) -> None:
        self.assertIn("macro_rules! wire_event_tag", self.generated)
        for value in self.data["websocket"]["events"]:
            self.assertIn(
                f"({wire_contract.pascal(value)}) => {{\n        $crate::shared::protocol::events::{wire_contract.upper(value)}\n    }};",
                self.generated,
            )

    def test_client_message_declaration_has_generated_tag_mapping(self) -> None:
        self.assertIn("macro_rules! wire_client_msg_tag", self.generated)
        for value in self.data["websocket"]["messages"]:
            self.assertIn(
                f"({wire_contract.pascal(value)}) => {{\n        $crate::shared::protocol::messages::{wire_contract.upper(value)}\n    }};",
                self.generated,
            )

    def test_rust_generation_is_deterministic(self) -> None:
        self.assertEqual(self.generated, wire_contract.rust(self.data))


class SharedDefinitionTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        for path in wire_contract.SHARED.glob("*.yaml"):
            shutil.copy(path, self.directory / path.name)

    def test_duplicate_yaml_key_is_rejected(self) -> None:
        path = self.directory / "defaults.yaml"
        path.write_text(path.read_text() + "  default_shell: zsh\n")
        with self.assertRaisesRegex(ValueError, "duplicate key 'default_shell'"):
            wire_contract.load(self.directory)

    def test_missing_definition_is_rejected(self) -> None:
        (self.directory / "usage.yaml").unlink()
        with self.assertRaises(FileNotFoundError):
            wire_contract.load(self.directory)

    def test_misplaced_section_is_rejected(self) -> None:
        path = self.directory / "defaults.yaml"
        path.write_text(path.read_text() + "websocket: {}\n")
        with self.assertRaisesRegex(ValueError, "expected sections"):
            wire_contract.load(self.directory)

    def test_constants_cannot_have_two_owners(self) -> None:
        path = self.directory / "defaults.yaml"
        path.write_text(path.read_text() + "  host_identity: host\n")
        with self.assertRaisesRegex(ValueError, "duplicate constants across definitions"):
            wire_contract.load(self.directory)

    def test_empty_routes_are_rejected(self) -> None:
        data = wire_contract.load(self.directory)
        data["protocol"]["http"]["routes"] = {}
        with self.assertRaisesRegex(ValueError, "no HTTP routes"):
            wire_contract.validate(data)

    def test_invalid_constant_type_is_rejected(self) -> None:
        path = self.directory / "defaults.yaml"
        path.write_text(path.read_text().replace("endpoint_port: 7717", "endpoint_port: []"))
        with self.assertRaisesRegex(ValueError, "expected string, boolean or integer"):
            wire_contract.load(self.directory)

    def test_folded_prompt_preserves_paragraphs_in_both_languages(self) -> None:
        path = self.directory / "defaults.yaml"
        path.write_text(path.read_text() + '  test_prompt: >-\n    First line\n    continues.\n\n\n    Second paragraph.\n')
        data = wire_contract.load(self.directory)
        self.assertEqual(data["defaults"]["constants"]["test_prompt"], "First line continues.\n\nSecond paragraph.")
        outputs = wire_contract.outputs(data)
        for suffix in ("defaults.rs", "SharedDefaults.cs"):
            content = next(value for path, value in outputs.items() if path.name == suffix)
            self.assertIn('"First line continues.\\n\\nSecond paragraph."', content)

    def test_checked_in_bindings_match_all_definitions(self) -> None:
        for path, generated in wire_contract.outputs(wire_contract.load()).items():
            with self.subTest(path=path.name):
                self.assertEqual(path.read_text(), generated)


if __name__ == "__main__":
    unittest.main()
