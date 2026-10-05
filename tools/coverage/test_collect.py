"""Coverage orchestration respects paths, tool overrides, and early failure."""

import subprocess
from unittest.mock import patch

import pytest

from tools.coverage import collect


def test_rust_reports_use_absolute_output_paths_and_tool_environment(tmp_path, monkeypatch):
    monkeypatch.setenv('CARGO', '"/tmp/cargo tool" --offline')
    monkeypatch.setenv('RUST_COVERAGE_EXCLUDE', '/tests/|/generated/')
    output = tmp_path / 'coverage with spaces'
    with (
        patch.object(collect.shutil, 'which', side_effect=lambda tool: '/bin/' + tool),
        patch.object(collect, 'run') as run,
        patch.object(collect, 'summarize') as summarize,
    ):
        collect.daemon(output)
    calls = [call.args[0] for call in run.call_args_list]
    assert calls[0] == ['/tmp/cargo tool', '--offline', 'llvm-cov', 'clean', '--profraw-only']
    assert calls[2][-1] == str(output / 'rust.cobertura.xml')
    assert calls[3][-1] == str(output / 'rust.filtered.cobertura.xml')
    assert '--ignore-filename-regex' in calls[4]
    assert run.call_args_list[0].kwargs['env']['LLVM_COV'] == '/bin/llvm-cov'
    assert summarize.call_count == 2


def test_missing_cargo_llvm_cov_fails_before_creating_output(tmp_path):
    with patch.object(collect.shutil, 'which', return_value=None), patch.object(collect, 'run') as run:
        with pytest.raises(ValueError, match='Missing cargo-llvm-cov'):
            collect.daemon(tmp_path / 'output')
        run.assert_not_called()
        assert not (tmp_path / 'output').exists()


def test_failed_collection_does_not_publish_summary(tmp_path, monkeypatch):
    monkeypatch.setenv('RUST_COVERAGE_EXCLUDE', '/tests/')
    with (
        patch.object(collect.shutil, 'which', return_value='/bin/tool'),
        patch.object(collect, 'run', side_effect=subprocess.CalledProcessError(23, ['cargo'])),
        patch.object(collect, 'summarize') as summarize,
    ):
        with pytest.raises(subprocess.CalledProcessError):
            collect.daemon(tmp_path)
        summarize.assert_not_called()


def test_csharp_report_excludes_tests_and_generated_serialization(tmp_path, monkeypatch):
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


def test_missing_exclusions_fail_before_creating_output(tmp_path, monkeypatch):
    monkeypatch.delenv('RUST_COVERAGE_EXCLUDE', raising=False)
    output = tmp_path / 'output'
    with patch.object(collect.shutil, 'which', return_value='/bin/tool'), patch.object(collect, 'run') as run:
        with pytest.raises(KeyError, match='RUST_COVERAGE_EXCLUDE'):
            collect.daemon(output)
        run.assert_not_called()
        assert not output.exists()
