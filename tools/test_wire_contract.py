#!/usr/bin/env python3
"""Tests for shared definition loading and generated bindings."""

from __future__ import annotations

import sys
import re
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

    def test_websocket_catalog_matches_protobuf_payloads(self) -> None:
        schema = (wire_contract.SHARED / "slopworld.proto").read_text()
        for message, section in (("Event", "events"), ("ClientMessage", "messages")):
            with self.subTest(message=message):
                payload = re.search(
                    rf"message {message}\s*\{{\s*oneof payload\s*\{{(.*?)\}}",
                    schema, re.S,
                )
                self.assertIsNotNone(payload)
                names = re.findall(r"\w+\s+(\w+)\s*=\s*\d+;", payload[1])
                self.assertEqual(names, self.data["websocket"][section])

    def test_protobuf_route_types_match_handler_signatures(self) -> None:
        from reference import api_routes, read_files
        root = Path(__file__).resolve().parents[1]
        source = "\n".join(p.read_text().split("#[cfg(test)]")[0]
                           for p in sorted((root / "slopd/src/api/handlers").rglob("*.rs")))
        signatures = {m[1]: (m[2], m[3] or "Ack") for m in re.finditer(
            r"pub\(crate\) async fn (\w+)\(([^{}]*?)\) -> ApiResult(?:<wire::(\w+)>)?\s*\{", source)}
        # Session actions share a body-free request and an acknowledgement response.
        for actions in re.findall(r"session_action!\(([^)]+)\)", source):
            for action in actions.split(","):
                if action.strip():
                    signatures[action.strip()] = ("", "Ack")
        declared = {(method, route["path"]): types
                    for route in self.data["http"]["routes"].values()
                    for method, types in route["protobuf"].items()}
        for route in api_routes(read_files()):
            if route.path == "/ws":
                continue
            self.assertIn(route.handler, signatures, f"missing handler signature: {route.handler}")
            body, response = signatures[route.handler]
            request = re.search(r"Proto<wire::(\w+)>", body)
            self.assertEqual(declared[(route.method, route.path)],
                             [request[1] if request else "Empty", response], route.handler)

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
        path = self.directory / "protocol.yaml"
        path.write_text(path.read_text() + "constants: {}\n")
        with self.assertRaisesRegex(ValueError, "duplicate key 'constants'"):
            wire_contract.load(self.directory)

    def test_missing_definition_is_rejected(self) -> None:
        (self.directory / "protocol.yaml").unlink()
        with self.assertRaises(FileNotFoundError):
            wire_contract.load(self.directory)

    def test_misplaced_section_is_rejected(self) -> None:
        path = self.directory / "protocol.yaml"
        path.write_text(path.read_text() + "extra: {}\n")
        with self.assertRaisesRegex(ValueError, "expected sections"):
            wire_contract.load(self.directory)

    def test_empty_routes_are_rejected(self) -> None:
        data = wire_contract.load(self.directory)
        data["protocol"]["http"]["routes"] = {}
        with self.assertRaisesRegex(ValueError, "no HTTP routes"):
            wire_contract.validate(data)

    def test_invalid_constant_type_is_rejected(self) -> None:
        path = self.directory / "protocol.yaml"
        path.write_text(path.read_text().replace("host_identity: host", "host_identity: []"))
        with self.assertRaisesRegex(ValueError, "expected string, boolean or integer"):
            wire_contract.load(self.directory)

    def test_checked_in_bindings_match_all_definitions(self) -> None:
        for path, generated in wire_contract.outputs(wire_contract.load()).items():
            with self.subTest(path=path.name):
                self.assertEqual(path.read_text(), generated)


if __name__ == "__main__":
    unittest.main()
