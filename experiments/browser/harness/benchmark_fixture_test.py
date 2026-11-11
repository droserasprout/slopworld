"""Check single-byte replies through a real PTY and return to canonical input."""

import importlib.util
import io
import os
import pty
import select
import subprocess
import sys
import termios
import time
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch


class KeystrokeFixtureTests(unittest.TestCase):
    def test_each_key_replies_without_enter_and_restores_terminal_mode(self):
        master, slave = pty.openpty()
        process = subprocess.Popen(
            [sys.executable, str(Path(__file__).with_name('benchmark-fixture.py'))],
            stdin=slave,
            stdout=slave,
            stderr=slave,
        )
        received = bytearray()

        def until(marker):
            deadline = time.monotonic() + 5
            while marker not in received:
                remaining = deadline - time.monotonic()
                if remaining <= 0 or not select.select([master], [], [], remaining)[0]:
                    self.fail('Missing fixture response: ' + repr(marker))
                received.extend(os.read(master, 65536))

        try:
            until(b'BENCH_READY')
            for run in ('FIRST', 'SECOND'):
                os.write(master, f'keys {run}\n'.encode())
                until(f'KEYS_READY_{run}'.encode())
                for index in range(20):
                    key = bytes([97 + index])
                    os.write(master, key)
                    until(f'KEY_{run}_{index:02d}_{key.hex()}'.encode())
                until(f'KEYS_DONE_{run}'.encode())
                self.assertTrue(termios.tcgetattr(slave)[3] & termios.ICANON)
            os.write(master, b'echo normal-input\n')
            until(b'ACK_normal-input')
        finally:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
            os.close(master)
            os.close(slave)

    def test_inline_keys_use_fixed_width_carriage_returns_until_explicit_finish(self):
        master, slave = pty.openpty()
        process = subprocess.Popen(
            [sys.executable, str(Path(__file__).with_name('benchmark-fixture.py'))],
            stdin=slave,
            stdout=slave,
            stderr=slave,
        )
        received = bytearray()

        def until(marker):
            deadline = time.monotonic() + 5
            while marker not in received:
                remaining = deadline - time.monotonic()
                if remaining <= 0 or not select.select([master], [], [], remaining)[0]:
                    self.fail('Missing fixture response: ' + repr(marker))
                received.extend(os.read(master, 65536))

        try:
            until(b'BENCH_READY')
            for run in ('0001', '0002'):
                previous = termios.tcgetattr(slave)
                os.write(master, f'inlinekeys {run}\n'.encode())
                ready = f'INLINE_KEYS_READY_{run}\r\n'.encode()
                until(ready)
                start = received.index(ready) + len(ready)
                expected = bytearray()
                for index in range(20):
                    key = bytes([97 + index])
                    reply = f'INLINE_KEY_{run}_{index:02d}_{key.hex()}'.encode()
                    os.write(master, key)
                    until(reply)
                    expected.extend(b'\r' + reply)
                self.assertEqual(received[start:], expected)
                self.assertNotIn(b'\n', received[start:])
                self.assertEqual(len(set(map(len, expected.split(b'\r')[1:]))), 1)
                self.assertFalse(termios.tcgetattr(slave)[3] & termios.ICANON)
                os.write(master, b'\n')
                until(f'INLINE_KEYS_DONE_{run}'.encode())
                self.assertTrue(termios.tcgetattr(slave)[3] & termios.ICANON)
                self.assertEqual(termios.tcgetattr(slave), previous)
            os.write(master, b'echo normal-input\n')
            until(b'ACK_normal-input')
        finally:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
            os.close(master)
            os.close(slave)

    def test_inline_keys_restore_all_terminal_settings_on_read_and_finish_errors(self):
        spec = importlib.util.spec_from_file_location(
            'benchmark_fixture', Path(__file__).with_name('benchmark-fixture.py')
        )
        fixture = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(fixture)
        master, slave = pty.openpty()
        previous = termios.tcgetattr(slave)
        try:
            for values, error in (
                ([OSError('read failed')], OSError),
                ([b'x'] * 10 + [b''], EOFError),
                ([b'x'] * 20 + [OSError('finish failed')], OSError),
                ([b'x'] * 20 + [b''], ValueError),
                ([b'x'] * 20 + [b'x'], ValueError),
            ):
                with self.subTest(error=error, reads=len(values)):
                    with (
                        patch.object(fixture.sys, 'stdin', SimpleNamespace(fileno=lambda: slave)),
                        patch.object(fixture.sys, 'stdout', io.StringIO()),
                        patch.object(fixture.os, 'read', side_effect=values),
                        self.assertRaises(error),
                    ):
                        fixture.keystrokes('FAIL', inline=True)
                    self.assertEqual(termios.tcgetattr(slave), previous)
        finally:
            os.close(master)
            os.close(slave)


if __name__ == '__main__':
    unittest.main()
