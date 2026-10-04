"""Resolve mod metadata using the same fallback and tag rules as Rust builds."""

from pathlib import Path
import subprocess
import tomllib


ROOT = Path(__file__).resolve().parents[1]


def main() -> None:
    with (ROOT / "slopd/Cargo.toml").open("rb") as manifest:
        fallback = tomllib.load(manifest)["package"]["version"]
    subprocess.run([str(ROOT / "tools/version.sh"), fallback], check=True)


if __name__ == "__main__":
    main()
