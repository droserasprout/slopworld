"""Fetch, verify, and atomically install the latest RimWorld Harmony assembly."""

import argparse
import hashlib
import json
import os
import re
import shutil
import tempfile
import urllib.error
import urllib.request
import zipfile
from pathlib import Path

from tools import ROOT
from tools.utils import log

API_URL = 'https://api.github.com/repos/pardeike/HarmonyRimWorld/releases/latest'
ASSET_NAME = 'HarmonyMod.zip'
ARCHIVE_ENTRY = 'HarmonyMod/Current/Assemblies/0Harmony.dll'


def fetch_harmony(destination: Path) -> None:
    request = urllib.request.Request(
        API_URL,
        headers={
            'Accept': 'application/vnd.github+json',
            'X-GitHub-Api-Version': '2022-11-28',
            'User-Agent': 'SlopWorld',
        },
    )
    with urllib.request.urlopen(request, timeout=60) as response:
        release = json.load(response)
    asset = next((item for item in release.get('assets', []) if item.get('name') == ASSET_NAME), None)
    if asset is None:
        raise ValueError(f'latest release has no {ASSET_NAME} asset')
    digest = asset.get('digest')
    if not isinstance(digest, str) or not re.fullmatch(r'sha256:[0-9a-fA-F]{64}', digest):
        raise ValueError(f'{ASSET_NAME} has no valid SHA-256 digest')
    expected = digest[7:].lower()
    version = release['tag_name']
    log(f'Fetching Harmony {version}...')
    with tempfile.TemporaryFile() as archive:
        with urllib.request.urlopen(asset['browser_download_url'], timeout=60) as response:
            shutil.copyfileobj(response, archive)
        archive.seek(0)
        actual = hashlib.file_digest(archive, 'sha256').hexdigest()
        if actual != expected:
            raise ValueError(f'downloaded asset failed SHA-256 verification\nexpected: {expected}\nactual:   {actual}')
        archive.seek(0)
        with zipfile.ZipFile(archive) as package:
            try:
                assembly = package.read(ARCHIVE_ENTRY)
            except KeyError as error:
                raise ValueError(f'archive did not contain a non-empty {ARCHIVE_ENTRY}') from error
        if not assembly:
            raise ValueError(f'archive did not contain a non-empty {ARCHIVE_ENTRY}')

    destination.mkdir(parents=True, exist_ok=True)
    # Stage beside the destination to preserve an existing assembly on failure.
    output = tempfile.NamedTemporaryFile(dir=destination, prefix='.0Harmony.dll.', delete=False)
    staged = Path(output.name)
    target = destination / '0Harmony.dll'
    try:
        with output:
            output.write(assembly)
        staged.chmod(0o644)
        os.replace(staged, target)
    finally:
        staged.unlink(missing_ok=True)
    log(f'Installed Harmony {version} at {target}')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('destination', nargs='?', type=Path, default=ROOT / 'mod/Assemblies')
    args = parser.parse_args()
    try:
        fetch_harmony(args.destination)
    except (OSError, ValueError, KeyError, zipfile.BadZipFile, urllib.error.URLError) as error:
        parser.exit(1, f'error: {error}\n')


if __name__ == '__main__':
    main()
