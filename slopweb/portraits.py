"""Import explicit native portrait snapshots into private runtime assets only."""

import fcntl
import hashlib
import json
import math
import os
import re
import struct
import time
import uuid
from dataclasses import dataclass
from pathlib import Path


@dataclass
class PortraitSnapshot:
    manifest: bytes
    files: dict[str, bytes]

    @property
    def identity(self) -> str:
        return hashlib.sha256(self.manifest).hexdigest()

    def write(self, root: Path) -> None:
        destination = root / 'assets/portraits'
        destination.mkdir(parents=True, exist_ok=True)
        for name, pixels in self.files.items():
            (destination / name).write_bytes(pixels)
        pending = destination / 'manifest.json.partial'
        pending.write_bytes(self.manifest)
        pending.replace(destination / 'manifest.json')


def read_snapshot(source: Path) -> PortraitSnapshot:
    manifest = source / 'manifest.json'
    if not manifest.exists():
        return PortraitSnapshot(json.dumps({'version': 1, 'uiScale': 1, 'entries': []}, sort_keys=True).encode(), {})
    with manifest.open('rb') as stream:
        raw = stream.read(1024 * 1024 + 1)
    if len(raw) > 1024 * 1024:
        raise ValueError('Portrait manifest exceeds its size bound')
    value = json.loads(raw)
    if not isinstance(value, dict):
        raise TypeError('Portrait manifest must be an object')
    scale = value.get('uiScale')
    entries = value.get('entries')
    if (
        type(value.get('version')) is not int
        or value['version'] != 1
        or not isinstance(scale, (int, float))
        or isinstance(scale, bool)
        or not math.isfinite(scale)
        or not 0.5 <= scale <= 4
        or not isinstance(entries, list)
        or len(entries) > 1024
    ):
        raise ValueError('Invalid portrait manifest version, scale or inventory')
    names, files, accepted = set(), {}, []
    total = 0
    for entry in entries:
        if not isinstance(entry, dict):
            raise TypeError('Portrait entry must be an object')
        session, name = entry.get('session'), entry.get('file')
        width, height = entry.get('width'), entry.get('height')
        if (
            not isinstance(session, str)
            or not session
            or len(session) > 256
            or session in names
            or not isinstance(name, str)
            or re.fullmatch(r'[0-9a-f]{64}\.rgba', name) is None
            or type(width) is not int
            or type(height) is not int
            or not 0 < width <= 2048
            or not 0 < height <= 2048
        ):
            raise ValueError('Invalid portrait identity or geometry')
        names.add(session)
        size = 8 + width * height * 4
        if name not in files:
            total += size
            if total > 64 * 1024 * 1024:
                raise ValueError('Portrait pixels exceed the snapshot size bound')
            path = source / name
            if path.is_symlink():
                raise ValueError('Portrait pixels cannot be a symbolic link')
            with path.open('rb') as stream:
                pixels = stream.read(size + 1)
            if len(pixels) != size or hashlib.sha256(pixels).hexdigest() + '.rgba' != name:
                raise ValueError('Portrait pixels are truncated or changed')
            files[name] = pixels
        if struct.unpack('<ii', files[name][:8]) != (width, height):
            raise ValueError('Portrait geometry disagrees with its pixel header')
        accepted.append({'session': session, 'file': name, 'width': width, 'height': height})
    # Discard unrelated fields rather than publishing arbitrary host metadata.
    clean = json.dumps({'version': 1, 'uiScale': scale, 'entries': accepted}, sort_keys=True).encode()
    return PortraitSnapshot(clean, files)


def host_source() -> Path:
    data = Path(
        os.environ.get(
            'SLOPD_DATA', str(Path(os.environ.get('XDG_DATA_HOME', Path.home() / '.local/share')) / 'slopworld')
        )
    )
    cache = Path(
        os.environ.get('SLOPD_CACHE', str(Path(os.environ.get('XDG_CACHE_HOME', Path.home() / '.cache')) / 'slopworld'))
    )
    # Match native Path.GetFullPath; resolving symlinks would change the profile key.
    profile = os.path.abspath(data / 'profiles/local')  # noqa: PTH100
    key = hashlib.sha256(profile.encode()).hexdigest()
    return cache / 'workspace-portraits' / key


def request_snapshot(source: Path, timeout: float = 10) -> PortraitSnapshot:
    """Ask the native GUI thread for pixels, with bounded admission and acknowledgment."""
    if not math.isfinite(timeout) or not 0 < timeout <= 60:
        raise ValueError('Portrait export timeout must be between zero and 60 seconds')
    source.mkdir(parents=True, exist_ok=True, mode=0o700)
    deadline = time.monotonic() + timeout

    def wait() -> None:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise TimeoutError('Native portrait export did not acknowledge the request')
        time.sleep(min(0.1, remaining))

    with (source / '.request.lock').open('a') as lock:
        while True:
            try:
                fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
                break
            except BlockingIOError:
                wait()
        identity = uuid.uuid4().hex
        request = source / 'request'
        pending = source / ('request.' + identity)
        try:
            pending.write_text(identity)
            pending.replace(request)
            while True:
                try:
                    with (source / 'response').open('rb') as stream:
                        raw = stream.read(4097)
                    if len(raw) > 4096:
                        raise ValueError('Native portrait response exceeds its size bound')
                    response = json.loads(raw)
                    if isinstance(response, dict) and response.get('id') == identity:
                        if type(response.get('version')) is not int or response['version'] != 1:
                            raise ValueError('Unsupported native portrait response')
                        if response.get('ok') is not True:
                            raise RuntimeError(
                                'Native portrait export failed: ' + str(response.get('error', 'unknown error'))
                            )
                        if not (source / 'manifest.json').is_file():
                            raise RuntimeError('Native portrait export acknowledged without a snapshot')
                        return read_snapshot(source)
                except (FileNotFoundError, json.JSONDecodeError):
                    pass
                wait()
        finally:
            pending.unlink(missing_ok=True)
            # A timed-out request must not trigger a later unexpected readback.
            # Do not remove a request replaced by another owner.
            try:
                with request.open('rb') as stream:
                    owned = stream.read(33) == identity.encode()
                if owned:
                    request.unlink()
            except FileNotFoundError:
                pass


def exporter_available(source: Path) -> bool:
    """Only a live native game advertises support; old mod versions remain import-only."""
    try:
        with (source / 'available').open('rb') as stream:
            value = json.loads(stream.read(65))
        pid = value['pid']
        return (
            type(pid) is int
            and pid > 0
            and (Path('/proc') / str(pid) / 'exe').resolve().name in ('RimWorld', 'RimWorldLinux')
        )
    except (OSError, ValueError, KeyError, TypeError):
        return False


def host_snapshot() -> PortraitSnapshot:
    explicit = os.environ.get('SLOPWEB_PORTRAITS')
    if explicit is not None:
        if not explicit.strip():
            raise ValueError('Explicit portrait source cannot be empty')
        source = Path(explicit).expanduser()
        if not (source / 'manifest.json').is_file():
            raise ValueError('Explicit portrait source must contain a native snapshot manifest')
        return read_snapshot(source)
    source = host_source()
    return request_snapshot(source) if exporter_available(source) else read_snapshot(source)
