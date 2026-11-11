"""Deterministic terminal traffic for invocation-owned temporary host sessions."""

import os
import sys
import termios
import time


def paint() -> None:
    print('\x1b[2J\x1b[H', end='')
    for row in range(200):
        print(f'{row:03d} | SlopWorld fixed terminal benchmark | 0123456789 abcdefghijklmnopqrstuvwxyz')
    print('BENCH_READY', flush=True)


def main() -> None:
    settings = termios.tcgetattr(sys.stdin.fileno())
    settings[3] &= ~termios.ECHO
    termios.tcsetattr(sys.stdin.fileno(), termios.TCSANOW, settings)
    paint()
    for line in sys.stdin:
        command = line.strip()
        if command == 'paint':
            paint()
        elif command == 'edges':
            rows = os.get_terminal_size().lines
            print('\x1b[2J\x1b[H', end='')
            for row in range(rows):
                label = 'BOTTOM_gyp_Mj' if row == rows - 1 else f'{row:03d} Agyp Mj'
                print(f'\x1b[{row + 1};1H{label}', end='')
            sys.stdout.flush()
        elif command == 'stream' or command.startswith('stream '):
            for row in range(100):
                print(f'{row:03d} | output workload | 0123456789 abcdefghijklmnopqrstuvwxyz', flush=True)
                time.sleep(0.05)
            suffix = '_' + command[7:] if command.startswith('stream ') else ''
            print('BENCH_STREAM_DONE' + suffix, flush=True)
        elif command.startswith('echo '):
            print('ACK_' + command[5:], flush=True)


if __name__ == '__main__':
    main()
