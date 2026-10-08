#!/usr/bin/env python3
"""Tests for shared definition loading and generated bindings."""

from __future__ import annotations

import os
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Any
from typing import ClassVar
from unittest.mock import patch

from tools import ROOT
from tools.generated_files import write_if_changed
from tools.protocol import wire_contract

FIXED_MTIME_NS = 1_700_000_000_000_000_000


class GeneratedFileTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.path = self.directory / 'generated.rs'
        self.path.write_bytes(b'original\n')
        os.utime(self.path, ns=(FIXED_MTIME_NS, FIXED_MTIME_NS))

    def test_new_output_creates_parent_directories(self) -> None:
        path = self.directory / 'nested/generated.rs'
        write_if_changed(path, b'new\n')
        self.assertEqual(path.read_bytes(), b'new\n')
        self.assertEqual(path.stat().st_mode & 0o777, 0o644)

    def test_unchanged_output_preserves_timestamp(self) -> None:
        before = self.path.stat().st_mtime_ns
        write_if_changed(self.path, b'original\n')
        self.assertEqual(self.path.stat().st_mtime_ns, before)

    def test_changed_output_replaces_content_and_preserves_permissions(self) -> None:
        self.path.chmod(0o640)
        write_if_changed(self.path, b'changed\n')
        self.assertEqual(self.path.read_bytes(), b'changed\n')
        self.assertEqual(self.path.stat().st_mode & 0o777, 0o640)

    def test_failed_publication_keeps_original_and_cleans_temporary_file(self) -> None:
        with patch('tools.generated_files.os.replace', side_effect=OSError('failure')):
            with self.assertRaises(OSError):
                write_if_changed(self.path, b'changed\n')
        self.assertEqual(self.path.read_bytes(), b'original\n')
        self.assertEqual(list(self.directory.iterdir()), [self.path])

    def test_importing_http_generator_does_not_require_sources_or_write_outputs(self) -> None:
        shutil.copytree(ROOT / 'tools', self.directory / 'tools', ignore=shutil.ignore_patterns('__pycache__'))
        before = {p.relative_to(self.directory) for p in self.directory.rglob('*')}
        subprocess.run(
            [sys.executable, '-B', '-c', 'import tools.protocol.protobuf_http'],
            cwd=self.directory,
            check=True,
            capture_output=True,
        )
        self.assertEqual(before, {p.relative_to(self.directory) for p in self.directory.rglob('*')})

    def test_repeated_full_generation_preserves_all_output_timestamps(self) -> None:
        root = ROOT
        shutil.copytree(root / 'shared', self.directory / 'shared')
        shutil.copytree(root / 'tools', self.directory / 'tools', ignore=shutil.ignore_patterns('__pycache__'))
        command = [sys.executable, '-m', 'tools.protocol.api_contract']
        subprocess.run(command, cwd=self.directory, check=True, capture_output=True)
        outputs = list((self.directory / 'mod').rglob('*.cs'))
        outputs += list((self.directory / 'slopd').rglob('*.rs'))
        self.assertEqual(len(outputs), 4)
        for path in outputs:
            os.utime(path, ns=(FIXED_MTIME_NS, FIXED_MTIME_NS))
        before = {path: (path.read_bytes(), path.stat().st_mtime_ns) for path in outputs}
        subprocess.run(command, cwd=self.directory, check=True, capture_output=True)
        self.assertEqual(before, {path: (path.read_bytes(), path.stat().st_mtime_ns) for path in outputs})


class WireContractTests(unittest.TestCase):
    data: ClassVar[dict[str, Any]]
    generated: ClassVar[str]

    @classmethod
    def setUpClass(cls) -> None:
        cls.data = wire_contract.load()['protocol']
        cls.generated = wire_contract.rust(cls.data)

    def test_websocket_catalog_matches_protobuf_payloads(self) -> None:
        schema = (wire_contract.SHARED / 'slopworld.proto').read_text()
        for message, section in (('Event', 'events'), ('ClientMessage', 'messages')):
            with self.subTest(message=message):
                payload = re.search(
                    rf'message {message}\s*\{{\s*oneof payload\s*\{{(.*?)\}}',
                    schema,
                    re.S,
                )
                assert payload is not None
                names = re.findall(r'\w+\s+(\w+)\s*=\s*\d+;', payload[1])
                self.assertEqual(names, self.data['websocket'][section])

    def test_protobuf_route_types_match_handler_signatures(self) -> None:
        from tools.docs.api_docs import api_routes

        root = ROOT
        source = '\n'.join(
            p.read_text()
            for p in sorted((root / 'slopd/src/api/handlers').rglob('*.rs'))
            if not (p.stem.endswith('_tests') or p.stem == 'tests')
        )
        signatures = {
            m[1]: (m[2], m[3] or 'Ack')
            for m in re.finditer(
                r'pub\(crate\) async fn (\w+)\(([^{}]*?)\) -> ApiResult(?:<wire::(\w+)>)?\s*\{', source
            )
        }
        # Session actions share a body-free request and an acknowledgement response.
        for actions in re.findall(r'session_action!\(([^)]+)\)', source):
            for action in actions.split(','):
                if action.strip():
                    signatures[action.strip()] = ('', 'Ack')
        declared = {
            (method, route['path']): types
            for route in self.data['http']['routes'].values()
            for method, types in route['protobuf'].items()
        }
        for route in api_routes():
            if route.path == '/ws':
                continue
            self.assertIn(route.handler, signatures, f'missing handler signature: {route.handler}')
            body, response = signatures[route.handler]
            request = re.search(r'Proto<wire::(\w+)>', body)
            self.assertEqual(
                declared[(route.method, route.path)], [request[1] if request else 'Empty', response], route.handler
            )

    def test_route_access_groups_preserve_root_boundaries(self) -> None:
        from tools.docs.api_docs import api_routes

        routes = {(route.method, route.path): route.scope for route in api_routes()}
        expected = {
            (method, route['path']): 'root-only' if route['scope'] == 'root' else 'scoped'
            for route in self.data['http']['routes'].values()
            for method in route['methods']
        }
        expected[('GET', '/ws')] = 'scoped'
        self.assertEqual(routes, expected)

    def test_public_inventory_separates_websocket_directions(self) -> None:
        from tools.docs.api_docs import render

        page = render()
        self.assertIn('Client → daemon | `ClientMessage`', page)
        self.assertIn('Daemon → client | `Event`', page)
        self.assertNotIn('| Handler |', page)


class SharedDefinitionTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        for path in wire_contract.SHARED.glob('*.yaml'):
            shutil.copy(path, self.directory / path.name)

    def test_duplicate_yaml_key_is_rejected(self) -> None:
        path = self.directory / 'protocol.yaml'
        path.write_text(path.read_text() + 'constants: {}\n')
        with self.assertRaisesRegex(ValueError, "duplicate key 'constants'"):
            wire_contract.load(self.directory)

    def test_missing_definition_is_rejected(self) -> None:
        (self.directory / 'protocol.yaml').unlink()
        with self.assertRaises(FileNotFoundError):
            wire_contract.load(self.directory)

    def test_misplaced_section_is_rejected(self) -> None:
        path = self.directory / 'protocol.yaml'
        path.write_text(path.read_text() + 'extra: {}\n')
        with self.assertRaisesRegex(ValueError, 'expected sections'):
            wire_contract.load(self.directory)

    def test_empty_routes_are_rejected(self) -> None:
        data = wire_contract.load(self.directory)
        data['protocol']['http']['routes'] = {}
        with self.assertRaisesRegex(ValueError, 'no HTTP routes'):
            wire_contract.validate(data)

    def test_invalid_constant_type_is_rejected(self) -> None:
        path = self.directory / 'protocol.yaml'
        path.write_text(path.read_text().replace('host_identity: host', 'host_identity: []'))
        with self.assertRaisesRegex(ValueError, 'expected string, boolean or integer'):
            wire_contract.load(self.directory)

    def test_checked_in_bindings_match_all_definitions(self) -> None:
        for path, generated in wire_contract.outputs(wire_contract.load()).items():
            with self.subTest(path=path.name):
                self.assertEqual(path.read_text(), generated)


if __name__ == '__main__':
    unittest.main()
