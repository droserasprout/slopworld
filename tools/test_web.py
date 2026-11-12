"""Checkout cleanup removes owned output without following links into private caches."""

from pathlib import Path

from tools.web import clean


def test_cleanup_preserves_sources_caches_and_symlink_targets(tmp_path: Path) -> None:
    component = tmp_path / 'slopweb'
    helper = component / 'runtime/Helper'
    helper.mkdir(parents=True)
    (helper / 'Helper.csproj').write_text('<Project />')
    test = component / 'tests/runtime/Checks'
    test.mkdir(parents=True)
    (test / 'Checks.csproj').write_text('<Project />')
    removed = [component / 'out/bundle', helper / 'bin/helper', test / 'obj/restore']
    kept = [
        component / 'runtime/out/local-font-toolchain/compiler',
        component / 'runtime/out/Private.csproj',
        component / 'runtime/out/bin/private-output',
        tmp_path / 'private-cache/data',
    ]
    for path in removed + kept:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text('retained')
    (helper / 'obj').symlink_to(tmp_path / 'private-cache', target_is_directory=True)
    for _ in range(2):
        clean(tmp_path)
        assert all(not path.exists() for path in removed)
        assert not (helper / 'obj').is_symlink()
        assert all(path.read_text() == 'retained' for path in kept)
        assert (helper / 'Helper.csproj').is_file()
        assert (test / 'Checks.csproj').is_file()
