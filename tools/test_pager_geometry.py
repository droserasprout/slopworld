"""Exercise pager startup and wheel input in an isolated tmux server; no game needed."""

import pathlib
import subprocess
import tempfile
import time


def main():
    with tempfile.TemporaryDirectory(prefix="slopworld-pager-") as directory:
        root = pathlib.Path(directory)
        socket = str(root / "tmux")

        def tmux(*args):
            return subprocess.check_output(["tmux", "-S", socket, *args], text=True)

        def wait_for(name, expected):
            deadline = time.monotonic() + 5
            while time.monotonic() < deadline:
                lines = tmux("capture-pane", "-p", "-t", name + ":").splitlines()
                if lines and lines[0] == expected:
                    return lines
                time.sleep(0.05)
            raise AssertionError(f"{name}: expected {expected!r} at row 1, got {lines!r}")

        # LESSOPEN's startup delay exposes the resize race without requiring a highlighter.
        preprocessor = root / "preprocess"
        preprocessor.write_text('#!/bin/sh\nsleep 0.1\ncat -- "$1"\n')
        preprocessor.chmod(0o700)
        short = root / "short.txt"
        short.write_text("one\ntwo\nthree\n")
        long = root / "long.txt"
        long.write_text("".join(f"line {i}\n" for i in range(1, 101)))

        def start(name, path):
            # Match the mod's LessEnv and the daemon's supplied startup geometry.
            tmux("new-session", "-d", "-s", name, "-x", "147", "-y", "44",
                 "env", f"LESSOPEN=|{preprocessor} %s", "LESS=-Rc", "less", "--", str(path))
            tmux("set-option", "-w", "-t", name + ":", "window-size", "manual")

        try:
            start("short", short)
            wait_for("short", "one")
            tmux("send-keys", "-t", "short:", "Down", "Down", "Down")
            time.sleep(0.1)
            wait_for("short", "one")
            start("long", long)
            lines = wait_for("long", "line 1")
            assert lines[42] == "line 43", lines
            assert tmux("display-message", "-p", "-t", "long:", "#{alternate_on}").strip() == "1"
            tmux("send-keys", "-t", "long:", "Down", "Down", "Down")
            lines = wait_for("long", "line 4")
            assert lines[42] == "line 46", lines
            print("Pager geometry: short-file wheel, new long file, and full-height scrolling passed")
        finally:
            subprocess.run(["tmux", "-S", socket, "kill-server"], check=False)


if __name__ == "__main__":
    main()
