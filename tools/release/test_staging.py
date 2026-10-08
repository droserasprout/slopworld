"""Keep distributed runtime assemblies aligned with the mod project."""

import xml.etree.ElementTree as ET

from tools import ROOT
from tools.release import staging


def test_runtime_allowlist_matches_mod_project_references() -> None:
    project = ET.parse(ROOT / 'mod/Source/SlopWorld/SlopWorld.csproj')
    runtime = {
        reference.attrib['Include']
        for reference in project.findall('.//Reference')
        if (reference.findtext('HintPath') or '').startswith('../../Assemblies/')
    }
    assert set(staging.MOD_ASSEMBLIES) == runtime | {'SlopWorld'}
