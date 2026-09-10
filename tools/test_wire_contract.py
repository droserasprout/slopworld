#!/usr/bin/env python3
"""Tests for the Rust envelope declarations emitted by wire_contract.py."""

from __future__ import annotations

import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import wire_contract  # noqa: E402


class WireContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.data = wire_contract.load()
        cls.generated = wire_contract.rust(cls.data)

    def test_event_declaration_has_generated_tag_mapping(self) -> None:
        self.assertIn("macro_rules! wire_event_serialize", self.generated)
        self.assertIn(
            "($ty:ty, { $( $variant:ident { $( $field:ident ),+ $(,)? } ),+ $(,)? })",
            self.generated,
        )
        for value in self.data["websocket"]["events"]:
            self.assertIn(
                f"({wire_contract.pascal(value)}) => {{\n        $crate::wire::events::{wire_contract.upper(value)}\n    }};",
                self.generated,
            )

    def test_client_message_declaration_has_generated_tag_mapping(self) -> None:
        self.assertIn("macro_rules! wire_client_msg_deserialize", self.generated)
        self.assertIn("$variant:ident", self.generated)
        for value in self.data["websocket"]["messages"]:
            self.assertIn(
                f"({wire_contract.pascal(value)}) => {{\n        $crate::wire::messages::{wire_contract.upper(value)}\n    }};",
                self.generated,
            )
        self.assertIn("($value:ident, strip_tag)", self.generated)

    def test_rust_generation_is_deterministic(self) -> None:
        self.assertEqual(self.generated, wire_contract.rust(self.data))


if __name__ == "__main__":
    unittest.main()
