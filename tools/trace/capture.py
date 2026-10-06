"""Capture future performance and latency records without changing the running game's state."""

import argparse
import datetime
import math
import os
import pathlib
import time
from typing import BinaryIO


def validate_source(path: pathlib.Path, source: BinaryIO, original: os.stat_result) -> None:
    """Compare pathname identity and byte size with the open capture stream."""
    current = path.stat()
    if (current.st_dev, current.st_ino) != (original.st_dev, original.st_ino) or current.st_size < source.tell():
        raise RuntimeError('The game log was replaced or truncated. Repeat the capture after startup.')


def capture(log: pathlib.Path, destination: pathlib.Path, seconds: float, label: str) -> int:
    count = 0
    created = False
    try:
        with log.open('rb') as source, destination.open('xb') as output:
            created = True
            original = os.fstat(source.fileno())
            source.seek(0, 2)
            output.write(f'# {label} {datetime.datetime.now().astimezone().isoformat()}\n'.encode())
            output.write(f'# duration={seconds}s. The first record may span pre-capture work.\n'.encode())
            deadline = time.monotonic() + seconds
            pending = b''
            while time.monotonic() < deadline:
                validate_source(log, source, original)
                line = source.readline()
                if not line:
                    time.sleep(0.1)
                    continue
                pending += line
                if not pending.endswith(b'\n'):
                    continue
                if b'[SlopWorld] perf ' in pending or b'[SlopWorld] latency ' in pending:
                    output.write(pending)
                    output.flush()
                    count += 1
                pending = b''
            validate_source(log, source, original)
        return count
    except BaseException:
        if created:
            destination.unlink(missing_ok=True)
        raise


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--log', type=pathlib.Path, required=True)
    parser.add_argument('--output', type=pathlib.Path, required=True)
    parser.add_argument('--seconds', type=float, default=30)
    parser.add_argument('--label', default='current')
    args = parser.parse_args()
    if not math.isfinite(args.seconds) or args.seconds <= 0:
        parser.error('seconds must be finite and positive')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    count = capture(args.log, args.output, args.seconds, args.label)
    print(f'Captured {count} trace records to {args.output}')
    if count == 0:
        print('No trace data: start with SLOPWORLD_DEBUG=1 and repeat.')
        return 1
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
