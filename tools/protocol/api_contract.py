"""Generate the wire contract, preserving timestamps of unchanged outputs."""

import subprocess
import sys
import tempfile
from pathlib import Path

from tools import ROOT
from tools.generated_files import write_if_changed


def main() -> None:
    destination = ROOT / 'mod/Source/SlopWorld/Client/Generated'
    with tempfile.TemporaryDirectory(prefix='slopworld-protocol-') as directory:
        subprocess.run(
            ['protoc', '-I', str(ROOT / 'shared'), f'--csharp_out={directory}', str(ROOT / 'shared/slopworld.proto')],
            check=True,
        )
        for path in sorted(Path(directory).glob('*.cs')):
            write_if_changed(destination / path.name, path.read_bytes())
    for module in ('tools.protocol.wire_contract', 'tools.protocol.protobuf_http'):
        subprocess.run([sys.executable, '-m', module], cwd=ROOT, check=True)


if __name__ == '__main__':
    main()
