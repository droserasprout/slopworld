"""Check single-byte replies through a real PTY and return to canonical input."""

import os
import pty
import select
import subprocess
import sys
import termios
import time
import unittest
from pathlib import Path


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


if __name__ == '__main__':
    unittest.main()
