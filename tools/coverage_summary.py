#!/usr/bin/env python3
"""Print the headline rates from a Cobertura coverage report."""

import sys
import xml.etree.ElementTree as ET


def percentage(value: str) -> str:
    return f"{float(value) * 100:.1f}%"


def main() -> int:
    if len(sys.argv) not in (2, 3):
        print(f"usage: {sys.argv[0]} REPORT [LABEL]", file=sys.stderr)
        return 2

    report = ET.parse(sys.argv[1]).getroot()
    if report.find("./packages/package") is None:
        print(f"{sys.argv[1]} contains no measured packages", file=sys.stderr)
        return 1
    label = sys.argv[2] if len(sys.argv) == 3 else sys.argv[1]
    line_rate = percentage(report.attrib["line-rate"])
    summary = f"{label} coverage: {line_rate} lines"
    if int(report.attrib.get("branches-valid", "0")) > 0:
        summary += f", {percentage(report.attrib['branch-rate'])} branches"
    print(summary)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
