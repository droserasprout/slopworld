"""Publish generated content without invalidating unchanged build inputs."""

import os
from pathlib import Path
import stat
import tempfile


def write_if_changed(path: Path, content: bytes) -> None:
    try:
        if path.read_bytes() == content:
            return
        mode = stat.S_IMODE(path.stat().st_mode)
    except FileNotFoundError:
        mode = 0o644
    path.parent.mkdir(parents=True, exist_ok=True)
    # A failed generator must not leave a partially written compiler input.
    output = tempfile.NamedTemporaryFile(dir=path.parent, delete=False)
    temporary = Path(output.name)
    try:
        with output:
            output.write(content)
        temporary.chmod(mode)
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)
