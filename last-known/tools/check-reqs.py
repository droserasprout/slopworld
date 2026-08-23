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
    path = shutil.which(command)
    return Result(path is not None, str(Path(path).absolute()) if path else command)


def check_any_command(spec):
    path = next((path for item in spec["commands"] if (path := shutil.which(item))), None)
    detail = str(Path(path).absolute()) if path else "one of: " + ", ".join(spec["commands"])
    return Result(path is not None, detail)


def check_library(spec):
    library = spec["value"]
    path = next(
        (Path(root) / library for root in ("/usr/lib", "/usr/lib64")
         if (Path(root) / library).exists()),
        None,
    )
    if path is None and shutil.which("ldconfig"):
        for line in run(["ldconfig", "-p"]).splitlines():
            if library in line and " => " in line:
                path = Path(line.rsplit(" => ", 1)[1])
                break
    return Result(path is not None, str(path) if path else library)


def check_rimworld_path(relative):
    game = os.environ.get("RIMWORLD", "")
    root = Path(game).resolve() if game else None
    found = bool(root and (root / relative).is_file())
    return Result(found, str(root / relative) if root else "RIMWORLD is unset")


def check_rimworld_assemblies(_spec):
    return check_rimworld_path("RimWorldLinux_Data/Managed/Assembly-CSharp.dll")


def check_rimworld_executable(_spec):
    return check_rimworld_path("RimWorldLinux")


def check_python_module(spec):
    module = importlib.util.find_spec(spec["module"])
    path = module.origin if module and module.origin else None
    return Result(module is not None, path or spec["module"])


def check_font_match(spec):
    name = spec["value"]
    output = run(["fc-match", "-f", "%{family}\t%{file}\n", name]) if shutil.which(
        "fc-match"
    ) else ""
    family, separator, path = output.strip().partition("\t")
    found = name.casefold() in family.casefold()
    return Result(found, path if found and separator else name)


def check_font_list(spec):
    name = spec["value"]
    output = run(["fc-list", "-f", "%{family}\t%{file}\n"]) if shutil.which(
        "fc-list"
    ) else ""
    match = next((line for line in output.splitlines() if name.casefold() in line.casefold()), "")
    _family, separator, path = match.partition("\t")
    return Result(bool(match), path if separator else name)


CHECKS = {
    "command": check_command,
    "any_command": check_any_command,
    "library": check_library,
    "rimworld_assemblies": check_rimworld_assemblies,
    "rimworld_executable": check_rimworld_executable,
    "python_module": check_python_module,
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
                    "✓",
                )
                message = paint("32" if section["required"] else "36", "detected")
            else:
                marker = paint(
                    "1;31" if section["required"] else "2",
                    "✗",
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
