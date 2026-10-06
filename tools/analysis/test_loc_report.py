"""Snapshots identify modified contents and cannot overwrite earlier reports."""

import datetime as dt
import subprocess
from pathlib import Path
from unittest.mock import patch

import pytest

from tools.analysis import loc_report


def test_snapshot_identifies_dirty_working_tree() -> None:
    with patch.object(subprocess, 'check_output', side_effect=['abc\n', ' M source.py\n']):
        assert loc_report.commit_hash() == 'abc (working tree modified)'


def test_exclusive_output_preserves_existing_report_and_symlinks(tmp_path: Path) -> None:
    output = tmp_path / 'report'
    output.write_text('previous')
    generated = dt.datetime(2026, 10, 4, tzinfo=dt.timezone.utc)
    with pytest.raises(FileExistsError):
        loc_report.write_note(output, 'abc', generated, {})
    assert output.read_text() == 'previous'
    output.unlink()
    target = tmp_path / 'missing'
    output.symlink_to(target)
    with pytest.raises(FileExistsError):
        loc_report.write_note(output, 'abc', generated, {})
    assert not target.exists()
