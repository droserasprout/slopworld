"""Publish generated content without invalidating unchanged build inputs."""

import stat
import tempfile
from pathlib import Path


def write_if_changed(path: Path, content: bytes) -> None:
    try:
        if path.read_bytes() == content:
            return
        mode = stat.S_IMODE(path.stat().st_mode)
    except FileNotFoundError:
        mode = 0o644
    path.parent.mkdir(parents=True, exist_ok=True)
    # A failed generator must not leave a partially written compiler input.
    with tempfile.NamedTemporaryFile(dir=path.parent, delete=False) as output:
        temporary = Path(output.name)
        try:
            output.write(content)
            # Flush and close the staged bytes before publication.
            output.close()
            temporary.chmod(mode)
            temporary.replace(path)
        finally:
            temporary.unlink(missing_ok=True)
