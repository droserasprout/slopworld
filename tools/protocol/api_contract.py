"""Generate or check the wire contract without publishing partial generation."""

import argparse
import subprocess
import tempfile
from pathlib import Path

from tools import ROOT
from tools.generated_files import write_if_changed
from tools.protocol import protobuf_http
from tools.protocol import wire_contract


def generate(destination: Path) -> None:
    csharp = destination / 'mod/Source/SlopWorld/Client/Generated'
    csharp.mkdir(parents=True)
    subprocess.run(
        ['protoc', '-I', str(ROOT / 'shared'), f'--csharp_out={csharp}', str(ROOT / 'shared/slopworld.proto')],
        check=True,
    )
    for path, content in wire_contract.outputs(wire_contract.load()).items():
        write_if_changed(destination / path.relative_to(ROOT), content.encode())
    protobuf_http.main(destination)


def publish(destination: Path, *, check: bool) -> list[Path]:
    differences = []
    for path in sorted(destination.rglob('*')):
        if not path.is_file():
            continue
        relative = path.relative_to(destination)
        target = ROOT / relative
        content = path.read_bytes()
        if check:
            if not target.is_file() or target.read_bytes() != content:
                differences.append(relative)
        else:
            write_if_changed(target, content)
    return differences


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true', help='compare temporary outputs without modifying the checkout')
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix='slopworld-protocol-') as directory:
        destination = Path(directory)
        generate(destination)
        differences = publish(destination, check=args.check)
    if differences:
        parser.exit(
            1, 'stale generated protocol files; run just refresh-protocol:\n' + '\n'.join(map(str, differences)) + '\n'
        )


if __name__ == '__main__':
    main()
