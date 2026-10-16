"""Coverage orchestration respects paths, tool overrides, and early failure."""

import shutil
import subprocess
from pathlib import Path
from unittest.mock import patch

import pytest

from tools.coverage import collect


def test_rust_reports_use_absolute_output_paths_and_tool_environment(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setenv('CARGO', '"/tmp/cargo tool" --offline')
    monkeypatch.setenv('RUST_COVERAGE_EXCLUDE', '/tests/|/generated/')
    monkeypatch.delenv('LLVM_COV', raising=False)
    monkeypatch.delenv('LLVM_PROFDATA', raising=False)
    output = tmp_path / 'coverage with spaces'
    with (
        patch.object(shutil, 'which', side_effect=lambda tool: '/bin/' + tool),
        patch.object(collect, 'run') as run,
        patch.object(collect, 'summarize') as summarize,
    ):
        collect.daemon(output)
    calls = [call.args[0] for call in run.call_args_list]
    assert calls[0] == ['/tmp/cargo tool', '--offline', 'llvm-cov', 'clean', '--workspace']
    assert calls[1] == ['/tmp/cargo tool', '--offline', 'llvm-cov', '--locked', '--no-report']
    assert calls[2][-1] == str(output / 'rust.cobertura.xml')
    assert calls[3][-1] == str(output / 'rust.filtered.cobertura.xml')
    assert '--ignore-filename-regex' in calls[4]
    for call in run.call_args_list:
        assert 'LLVM_COV' not in call.kwargs['env']
        assert 'LLVM_PROFDATA' not in call.kwargs['env']
    assert summarize.call_count == 2


def test_rust_coverage_preserves_explicit_llvm_tool_overrides(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv('LLVM_COV', '/opt/llvm/bin/llvm-cov')
    monkeypatch.setenv('LLVM_PROFDATA', '/opt/llvm/bin/llvm-profdata')
    monkeypatch.setenv('RUST_COVERAGE_EXCLUDE', '/tests/')
    with (
        patch.object(shutil, 'which', side_effect=lambda tool: tool),
        patch.object(collect, 'run') as run,
        patch.object(collect, 'summarize'),
    ):
        collect.daemon(tmp_path)
    for call in run.call_args_list:
        assert call.kwargs['env']['LLVM_COV'] == '/opt/llvm/bin/llvm-cov'
        assert call.kwargs['env']['LLVM_PROFDATA'] == '/opt/llvm/bin/llvm-profdata'


def test_missing_cargo_llvm_cov_fails_before_creating_output(tmp_path: Path) -> None:
    with patch.object(shutil, 'which', return_value=None), patch.object(collect, 'run') as run:
        with pytest.raises(ValueError, match='Missing cargo-llvm-cov'):
            collect.daemon(tmp_path / 'output')
        run.assert_not_called()
        assert not (tmp_path / 'output').exists()


def test_failed_cleanup_stops_collection(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv('RUST_COVERAGE_EXCLUDE', '/tests/')
    with (
        patch.object(shutil, 'which', return_value='/bin/tool'),
        patch.object(collect, 'run', side_effect=subprocess.CalledProcessError(23, ['cargo'])) as run,
        patch.object(collect, 'summarize') as summarize,
    ):
        with pytest.raises(subprocess.CalledProcessError):
            collect.daemon(tmp_path)
        run.assert_called_once()
        summarize.assert_not_called()


def test_csharp_report_excludes_tests_and_generated_serialization(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setenv('DOTNET', '"/tmp/dotnet tool"')
    monkeypatch.setenv('TEST_PROJECT', 'tests/project with spaces.csproj')
    monkeypatch.setenv('TEST_DLL', 'tests/library with spaces.dll')
    with patch.object(collect, 'run') as run, patch.object(collect, 'summarize') as summarize:
        collect.mod(tmp_path)
    coverlet = run.call_args_list[2].args[0]
    assert coverlet[coverlet.index('--target') + 1] == '/tmp/dotnet tool'
    assert coverlet[coverlet.index('--targetargs') + 1] == '"tests/library with spaces.dll" --quiet'
    assert '**/mod/Tests/**/*.cs' in coverlet
    assert '**/Client/Generated/Slopworld.cs' in coverlet
    assert coverlet[-1] == str(tmp_path / 'csharp.cobertura.xml')
    summarize.assert_called_once_with(tmp_path / 'csharp.cobertura.xml', 'C#')


def test_missing_exclusions_fail_before_creating_output(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv('RUST_COVERAGE_EXCLUDE', raising=False)
    output = tmp_path / 'output'
    with patch.object(shutil, 'which', return_value='/bin/tool'), patch.object(collect, 'run') as run:
        with pytest.raises(KeyError, match='RUST_COVERAGE_EXCLUDE'):
            collect.daemon(output)
        run.assert_not_called()
        assert not output.exists()
