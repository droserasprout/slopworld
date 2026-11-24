"""Build ordered daemon and browser OST catalogs from the native SongDefs."""

import re
from pathlib import Path
from xml.etree import ElementTree

from tools import ROOT
from tools.generated_files import write_if_changed


def track_ids(mod: Path) -> list[str]:
    tracks = []
    for song in ElementTree.parse(mod / 'Defs/Songs.xml').findall('SongDef'):
        match = re.fullmatch(r'SlopWorld/OST/([a-zA-Z0-9_-]+)', song.findtext('clipPath', ''))
        if not match:
            raise ValueError('Expected a bundled OST clipPath in Songs.xml')
        track = match[1]
        if track in tracks or not (mod / 'Sounds/SlopWorld/OST' / f'{track}.ogg').is_file():
            raise ValueError(f'Duplicate or missing OST track {track}')
        tracks.append(track)
    if not tracks:
        raise ValueError('Songs.xml has no OST tracks')
    return tracks


def outputs(root: Path) -> dict[Path, str]:
    tracks = track_ids(root / 'mod')
    header = '// Generated from mod/Defs/Songs.xml by just generate-ost.\n'
    return {
        root / 'slopd/src/ost/tracks.rs': header
        + 'pub(super) const TRACKS: &[(&str, &[u8])] = &[\n'
        + ''.join(f'    track!("{track}"),\n' for track in tracks)
        + '];\n',
        root / 'slopweb/runtime/Adapters/BrowserOstManifest.cs': header
        + 'namespace BrowserAdapters;\n\ninternal static class BrowserOstManifest\n{\n'
        + '    internal static readonly string[] Tracks =\n    [\n'
        + ''.join(f'        "{track}",\n' for track in tracks)
        + '    ];\n}\n',
    }


if __name__ == '__main__':
    for path, text in outputs(ROOT).items():
        write_if_changed(path, text.encode())
