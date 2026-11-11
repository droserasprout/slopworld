"""Deterministic terminal traffic for invocation-owned temporary host sessions."""

import os
import sys
import termios
import time


def paint(suffix: str = '') -> None:
    print('\x1b[2J\x1b[H', end='')
    for row in range(200):
        print(f'{row:03d} | SlopWorld fixed terminal benchmark | 0123456789 abcdefghijklmnopqrstuvwxyz')
    print('BENCH_READY' + suffix, flush=True)


def keystrokes(marker: str, *, inline: bool = False) -> None:
    descriptor = sys.stdin.fileno()
    previous = termios.tcgetattr(descriptor)
    settings = termios.tcgetattr(descriptor)
    settings[3] &= ~(termios.ICANON | termios.ECHO)
    settings[6][termios.VMIN] = 1
    settings[6][termios.VTIME] = 0
    try:
        termios.tcsetattr(descriptor, termios.TCSANOW, settings)
        prefix = 'INLINE_' if inline else ''
        print(prefix + 'KEYS_READY_' + marker, flush=True)
        for index in range(20):
            value = os.read(descriptor, 1)
            if not value:
                raise EOFError('Keystroke fixture input closed')
            reply = f'{prefix}KEY_{marker}_{index:02d}_{value.hex()}'
            print('\r' + reply if inline else reply, end='' if inline else '\n', flush=True)
        if inline and os.read(descriptor, 1) != b'\n':
            raise ValueError('Inline keystroke fixture requires Enter to finish')
    finally:
        termios.tcsetattr(descriptor, termios.TCSANOW, previous)
    print(('\nINLINE_' if inline else '') + 'KEYS_DONE_' + marker, flush=True)


def main() -> None:
    settings = termios.tcgetattr(sys.stdin.fileno())
    settings[3] &= ~termios.ECHO
    termios.tcsetattr(sys.stdin.fileno(), termios.TCSANOW, settings)
    paint()
    for line in sys.stdin:
        command = line.strip()
        if command == 'paint' or command.startswith('paint '):
            paint('_' + command[6:] if command.startswith('paint ') else '')
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
        elif command.startswith('inlinekeys '):
            keystrokes(command[11:], inline=True)
        elif command.startswith('keys '):
            keystrokes(command[5:])
        elif command.startswith('echo '):
            print('ACK_' + command[5:], flush=True)
        elif command.startswith('echohex '):
            print('HEX_' + command[8:].encode('utf-8').hex() + '_END', flush=True)


if __name__ == '__main__':
    main()
