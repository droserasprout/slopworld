"""Just-only workload provisioning; the installed launcher remains read-only to SDKs."""

import argparse
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

import aot


def ensure_workload(arguments: list[str]) -> None:
    parser = argparse.ArgumentParser(add_help=False, allow_abbrev=False)
    parser.add_argument('--interpreted', action='store_true')
    parser.add_argument('--aot-sdk')
    options, _ = parser.parse_known_args(arguments)
    if options.interpreted or any(
        value in arguments for value in ('--help', '-h', '--version', '-V', 'paths', 'completions')
    ):
        return
    selected = options.aot_sdk or os.environ.get('SLOPWEB_AOT_SDK') or shutil.which('dotnet')
    if not selected:
        raise ValueError('Mixed AOT requires an installed .NET 10 SDK, or use --interpreted')
    try:
        aot.toolchain(Path(selected))
    except aot.MissingWorkload as missing:
        sdk = missing.sdk
        command = [str(sdk), 'workload', 'install', 'wasm-tools', '--version', missing.version]
        if os.geteuid() != 0 and not os.access(sdk.parent, os.W_OK):
            command.insert(0, 'sudo')
        print(f'slopweb: installing matching wasm-tools workload set {missing.version}', flush=True)
        # Pin SDK selection outside the checkout, even when sudo changes the environment.
        with tempfile.TemporaryDirectory(prefix='slopweb-workload-') as temporary:
            (Path(temporary) / 'global.json').write_text(aot.sdk_selection(missing.version))
            subprocess.run(command, cwd=temporary, env=aot.sdk_environment(sdk), check=True)
        # Never start preparation after a failed or incomplete installation.
        aot.toolchain(sdk)


def main() -> None:
    try:
        ensure_workload(sys.argv[1:])
        launcher = Path(__file__).resolve().parent / 'out/launch.py'
        os.execv(sys.executable, [sys.executable, str(launcher), *sys.argv[1:]])
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        sys.exit(f'slopweb: {error}')


if __name__ == '__main__':
    main()
