"""Generate the wire contract, preserving timestamps of unchanged outputs."""

from pathlib import Path
import subprocess
import sys
import tempfile

from generated_files import write_if_changed


ROOT = Path(__file__).resolve().parents[1]


def main() -> None:
    destination = ROOT / "mod/Source/SlopWorld/Client/Generated"
    with tempfile.TemporaryDirectory(prefix="slopworld-protocol-") as directory:
        subprocess.run(
            ["protoc", "-I", str(ROOT / "shared"),
             f"--csharp_out={directory}", str(ROOT / "shared/slopworld.proto")],
            check=True,
        )
        for path in sorted(Path(directory).glob("*.cs")):
            write_if_changed(destination / path.name, path.read_bytes())
    for script in ("wire_contract.py", "protobuf_http.py"):
        subprocess.run([sys.executable, str(ROOT / "tools" / script)], check=True)


if __name__ == "__main__":
    main()
