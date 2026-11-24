from tools import ROOT
from tools.assets.ost_manifest import outputs


def test_daemon_and_browser_catalogs_match_bundled_songdefs() -> None:
    for path, expected in outputs(ROOT).items():
        assert path.read_text(encoding='utf-8') == expected, f'Run just generate-ost to update {path.name}'
