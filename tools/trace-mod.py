"""Capture future PerfTrace records without changing the running game's state."""

import argparse
import datetime
import math
import os
import pathlib
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--log", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    parser.add_argument("--seconds", type=float, default=30)
    parser.add_argument("--label", default="current")
    args = parser.parse_args()
    if not math.isfinite(args.seconds) or args.seconds <= 0:
        parser.error("seconds must be finite and positive")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    count = 0
    # Exclusive output avoids silently replacing an earlier comparison window.
    with args.log.open() as source, args.output.open("x") as output:
        original = os.fstat(source.fileno())
        source.seek(0, 2)
        output.write(f"# {args.label} {datetime.datetime.now().astimezone().isoformat()}\n")
        output.write(f"# duration={args.seconds}s; first record may span pre-capture work\n")
        output.flush()
        deadline = time.monotonic() + args.seconds
        while time.monotonic() < deadline:
            line = source.readline()
            if not line:
                # Refuse a restart/truncation: mixing two processes is not a sample.
                current = args.log.stat()
                if (current.st_dev, current.st_ino) != (original.st_dev, original.st_ino) or current.st_size < source.tell():
                    raise RuntimeError("Game log replaced/truncated; repeat after startup")
                time.sleep(0.1)
                continue
            if "[SlopWorld] perf " in line:
                output.write(line)
                output.flush()
                count += 1
    print(f"Captured {count} perf records to {args.output}")
    if count == 0:
        print("No trace data: restart the game with SLOPWORLD_DEBUG=1 and repeat.")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
