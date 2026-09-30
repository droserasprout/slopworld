"""Run tests with a writable tmux socket root, independent of the host's sockets."""

import os
import subprocess
import sys
import tempfile


def main():
    if len(sys.argv) < 2:
        raise SystemExit('usage: test_tmux.py COMMAND [ARG ...]')
    # Override even an inherited read-only root. Child tests and their tmux
    # clients share this directory for the entire run, including cleanup.
    with tempfile.TemporaryDirectory(prefix='slopworld-test-tmux-') as directory:
        result = subprocess.run(sys.argv[1:], env={**os.environ, 'TMUX_TMPDIR': directory})
    raise SystemExit(result.returncode)


if __name__ == '__main__':
    main()
