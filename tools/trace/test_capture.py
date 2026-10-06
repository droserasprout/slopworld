"""Capture publishes only complete windows from one log identity."""

import time
from pathlib import Path
from unittest.mock import patch

import pytest

from tools.trace import capture


def test_appended_utf8_records_use_byte_offsets(tmp_path: Path) -> None:
    log, output = tmp_path / 'log', tmp_path / 'capture'
    log.write_bytes(b'old records\n')

    def append(_seconds: float) -> None:
        with log.open('ab') as stream:
            stream.write('[SlopWorld] perf name=é\n'.encode())

    with (
        patch.object(time, 'monotonic', side_effect=[0, 0.1, 0.2, 2]),
        patch.object(time, 'sleep', side_effect=append),
    ):
        assert capture.capture(log, output, 1, 'test') == 1
    assert 'name=é' in output.read_text()
    assert 'old records' not in output.read_text()


@pytest.mark.parametrize('change', ['truncate', 'replace'])
def test_restart_at_end_of_window_removes_incomplete_output(tmp_path: Path, change: str) -> None:
    log, output = tmp_path / 'log', tmp_path / 'capture'
    log.write_bytes(b'old records\n')

    def restart(_seconds: float) -> None:
        if change == 'replace':
            log.rename(tmp_path / 'old')
        log.write_bytes(b'')

    with (
        patch.object(time, 'monotonic', side_effect=[0, 0.1, 2]),
        patch.object(time, 'sleep', side_effect=restart),
    ):
        with pytest.raises(RuntimeError, match='replaced or truncated'):
            capture.capture(log, output, 1, 'test')
    assert not output.exists()


def test_existing_capture_survives_rejected_overwrite(tmp_path: Path) -> None:
    log, output = tmp_path / 'log', tmp_path / 'capture'
    log.touch()
    output.write_bytes(b'previous capture')
    with pytest.raises(FileExistsError):
        capture.capture(log, output, 1, 'test')
    assert output.read_bytes() == b'previous capture'
