#!/usr/bin/env python3
"""Report the host requirements described in check-reqs.json."""

import argparse
import importlib.util
import json
import os
import shutil
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path


DATA = Path(__file__).with_suffix(".json")


@dataclass(frozen=True)
class Result:
    found: bool
    detail: str


class Paint:
    def __init__(self, enabled):
        self.enabled = enabled

    def __call__(self, code, text):
        return f"\033[{code}m{text}\033[0m" if self.enabled else text


def run(command):
    return subprocess.run(
        command,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL,
        check=False,
    ).stdout


def check_command(spec):
    command = spec["commands"][0]
    return Result(shutil.which(command) is not None, command)


def check_any_command(spec):
    found = next((item for item in spec["commands"] if shutil.which(item)), None)
    return Result(found is not None, found or "one of: " + ", ".join(spec["commands"]))


def check_all_commands(spec):
    missing = [item for item in spec["commands"] if not shutil.which(item)]
    return Result(not missing, ", ".join(spec["commands"] if not missing else missing))


def check_library(spec):
    library = spec["value"]
    found = any((Path(root) / library).exists() for root in ("/usr/lib", "/usr/lib64"))
    if not found and shutil.which("ldconfig"):
        found = library in run(["ldconfig", "-p"])
    return Result(found, library)


def check_rimworld_path(relative):
    game = os.environ.get("RIMWORLD", "")
    root = Path(game) if game else None
    found = bool(root and (root / relative).is_file())
    return Result(found, game or "RIMWORLD is unset")


def check_rimworld_assemblies(_spec):
    return check_rimworld_path("RimWorldLinux_Data/Managed/Assembly-CSharp.dll")


def check_rimworld_executable(_spec):
    return check_rimworld_path("RimWorldLinux")


def check_python_modules(spec):
    missing = [name for name in spec["modules"] if importlib.util.find_spec(name) is None]
    detail = spec["display"] if not missing else ", ".join(missing)
    return Result(not missing, detail)


def check_font_match(spec):
    name = spec["value"]
    found = shutil.which("fc-match") is not None and name.casefold() in run(
        ["fc-match", "-f", "%{family}\n", name]).casefold()
    return Result(found, name)


def check_font_list(spec):
    name = spec["value"]
    found = (
        shutil.which("fc-list") is not None
        and name.casefold() in run(["fc-list"]).casefold()
    )
    return Result(found, name)


CHECKS = {
    "command": check_command,
    "any_command": check_any_command,
    "all_commands": check_all_commands,
    "library": check_library,
    "rimworld_assemblies": check_rimworld_assemblies,
    "rimworld_executable": check_rimworld_executable,
    "python_modules": check_python_modules,
    "font_match": check_font_match,
    "font_list": check_font_list,
}


def arguments():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--color",
        choices=("auto", "always", "never"),
        default="auto",
        help="colorize output (default: auto)",
    )
    return parser.parse_args()


def main():
    args = arguments()
    color = args.color == "always" or (
        args.color == "auto"
        and sys.stdout.isatty()
        and "NO_COLOR" not in os.environ
    )
    paint = Paint(color)
    inventory = json.loads(DATA.read_text())
    width = max(len(check["label"]) for section in inventory["sections"]
                for check in section["checks"])
    required_missing = 0

    print(paint("1;36", "SlopWorld requirements"))
    for index, section in enumerate(inventory["sections"]):
        if index:
            print()
        print(paint("1", section["title"]))
        for spec in section["checks"]:
            result = CHECKS[spec["kind"]](spec)
            if result.found:
                marker = paint(
                    "1;32" if section["required"] else "1;36",
                    "✓" if section["required"] else "◆",
                )
                message = paint("32" if section["required"] else "36", "detected")
            else:
                marker = paint(
                    "1;31" if section["required"] else "2",
                    "✗" if section["required"] else "○",
                )
                message = paint(
                    "31" if section["required"] else "2",
                    "missing" if section["required"] else "not detected",
                )
                required_missing += int(section["required"])
            print(f"  {marker} {spec['label']:<{width}}  {message}: {result.detail}")

    print()
    if required_missing:
        print(paint("1;31", f"✗ {required_missing} required check(s) failed."))
        return 1
    print(paint("1;32", "✓ All required checks passed."))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
