#!/usr/bin/env python3
"""Generate the repository's environment/API/CLI reference.

Usage: python3 tools/reference.py [output]

The scanner is deliberately small and source-oriented. It records names and source
locations, never environment values, and does not need a build or a running daemon.
The default output is ``reference.md`` at the repository root.
"""

from __future__ import annotations

import re
import subprocess
import sys
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
DEFAULT_OUTPUT = ROOT / "reference.md"
TEXT_SUFFIXES = {".cs", ".md", ".rs", ".service", ".sh", ".toml"}
SKIP_PARTS = {"target", "obj"}
ENV_NAME = r"[A-Z][A-Z0-9_]*"


@dataclass(frozen=True)
class Hit:
    path: str
    line: int
    text: str


@dataclass(frozen=True)
class Route:
    method: str
    path: str
    handler: str
    scope: str
    hit: Hit


def tracked_files() -> list[Path]:
    """Return source/config/document files, excluding generated build output."""

    try:
        names = subprocess.check_output(
            ["git", "ls-files", "--cached", "--others", "--exclude-standard"],
            cwd=ROOT,
            text=True,
        ).splitlines()
    except (OSError, subprocess.CalledProcessError):
        names = []
        for directory in (ROOT / "slopd", ROOT / "mod", ROOT / "notes", ROOT / "docs"):
            names.extend(str(p.relative_to(ROOT)) for p in directory.rglob("*"))
        names.extend(["Makefile", "README.md"])

    files = []
    for name in names:
        path = ROOT / name
        if not path.is_file() or name == "reference.md":
            continue
        generated_bin = "bin" in path.parts and "src" not in path.parts
        if any(part in SKIP_PARTS for part in path.parts) or generated_bin or "docs/book" in path.as_posix():
            continue
        if path.name != "Makefile" and path.suffix not in TEXT_SUFFIXES:
            continue
        files.append(path)
    return sorted(set(files))


def read_files() -> dict[Path, str]:
    files = {}
    for path in tracked_files():
        try:
            files[path] = path.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            continue
    return files


def hit(path: Path, text: str, offset: int, line_text: str | None = None) -> Hit:
    line = text.count("\n", 0, offset) + 1
    if line_text is None:
        line_text = text.splitlines()[line - 1].strip()
    return Hit(str(path.relative_to(ROOT)), line, line_text)


def add_hit(index: dict[str, list[Hit]], name: str, item: Hit) -> None:
    if item not in index[name]:
        index[name].append(item)


def cli_name(path: Path) -> str:
    """Return the public CLI name for a source file, including split modules."""

    try:
        bin_index = path.parts.index("bin")
    except ValueError:
        return path.stem
    if len(path.parts) > bin_index + 2:
        return path.parts[bin_index + 1]
    return path.stem


def env_inventory(files: dict[Path, str]) -> tuple[dict[str, list[Hit]], list[Hit]]:
    names: dict[str, list[Hit]] = defaultdict(list)
    dynamic: list[Hit] = []
    constants: dict[str, str] = {}

    for path, text in files.items():
        for match in re.finditer(
            rf"\bconst\s+([A-Z][A-Z0-9_]*)\s*:\s*&(?:'static\s+)?str\s*=\s*['\"]({ENV_NAME})['\"]",
            text,
        ):
            constants[match.group(1)] = match.group(2)

    direct_patterns = [
        rf"(?:std::)?env::var\(\s*['\"]({ENV_NAME})['\"]",
        rf"option_env!\(\s*['\"]({ENV_NAME})['\"]",
        rf"env!\(\s*['\"]({ENV_NAME})['\"]",
        rf"\.env\(\s*['\"]({ENV_NAME})['\"]",
        rf"Environment\s*=\s*({ENV_NAME})\s*=",
        rf"--setenv[=\"',\s]+['\"]({ENV_NAME})['\"]",
    ]
    for path, text in files.items():
        for pattern in direct_patterns:
            for match in re.finditer(pattern, text):
                add_hit(names, match.group(1), hit(path, text, match.start()))

        for match in re.finditer(r"(?:std::)?env::(?:var|var_os)\(\s*([A-Z][A-Z0-9_]*)\s*\)", text):
            if match.group(1) in constants:
                add_hit(names, constants[match.group(1)], hit(path, text, match.start()))

        # Covers selected environment names passed through a local variable, such as the
        # clipboard backend's `Needs::X11 => "DISPLAY"`.
        for match in re.finditer(rf"=>\s*['\"]({ENV_NAME})['\"]", text):
            add_hit(names, match.group(1), hit(path, text, match.start()))
        for block in re.finditer(r"\bBASE_ENV\b[^=]*=\s*&?\[([^]]*)\]", text, re.DOTALL):
            for match in re.finditer(rf"['\"]({ENV_NAME})['\"]", block.group(1)):
                add_hit(names, match.group(1), hit(path, text, block.start(1) + match.start()))

        for match in re.finditer(r"\bstd::env::vars\(\)|\bfor \([^\n]*\) in std::env::vars", text):
            dynamic.append(hit(path, text, match.start()))
        for match in re.finditer(r"--setenv[=\"',\s]+(?:[a-z][A-Za-z0-9_]*|k|key)", text):
            dynamic.append(hit(path, text, match.start()))

        # Preset paths and documentation are part of the environment interface even when
        # Rust delegates the lookup to a crate. Rust source is excluded here because `${NAME}`
        # is also Rust's format-string capture syntax, not necessarily an environment lookup.
        if path.suffix in {".md", ".toml"}:
            for match in re.finditer(rf"\$\{{({ENV_NAME})(?::[^}}]*)?\}}|\$({ENV_NAME})\b", text):
                add_hit(names, match.group(1) or match.group(2), hit(path, text, match.start()))

    for path, text in files.items():
        if path.name != "Makefile":
            continue
        for match in re.finditer(rf"^({ENV_NAME})\s*\?=", text, re.MULTILINE):
            add_hit(names, match.group(1), hit(path, text, match.start()))

    return dict(sorted(names.items())), sorted(set(dynamic), key=lambda x: (x.path, x.line))


def matching_close(text: str, open_at: int) -> int:
    """Find a call's closing parenthesis while ignoring strings and comments."""

    depth = 0
    quote: str | None = None
    escaped = False
    comment = False
    i = open_at
    while i < len(text):
        char = text[i]
        nxt = text[i + 1] if i + 1 < len(text) else ""
        if comment:
            if char == "\n":
                comment = False
        elif quote:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == quote:
                quote = None
        elif char == "/" and nxt == "/":
            comment = True
            i += 1
        elif char in "'\"":
            quote = char
        elif char == "(":
            depth += 1
        elif char == ")":
            depth -= 1
            if depth == 0:
                return i
        i += 1
    return len(text)


def api_routes(files: dict[Path, str]) -> list[Route]:
    routes: list[Route] = []
    wire_paths: dict[str, str] = {}
    route_paths: dict[str, str] = {}
    for path, text in files.items():
        if path != ROOT / "slopd/src/shared/protocol.rs":
            continue
        for match in re.finditer(
            r"\bconst\s+([A-Z][A-Z0-9_]*)\s*:\s*&str\s*=\s*['\"]([^'\"]+)['\"]",
            text,
        ):
            wire_paths[match.group(1)] = match.group(2)
        routes_block = re.search(
            r"pub\(crate\)\s+mod\s+routes\s*\{(.*?)\n\}", text, re.DOTALL
        )
        if routes_block:
            for match in re.finditer(
                r"\bconst\s+([A-Z][A-Z0-9_]*)\s*:\s*&str\s*=\s*['\"]([^'\"]+)['\"]",
                routes_block.group(1),
            ):
                route_paths[match.group(1)] = match.group(2)
    method_pattern = re.compile(r"\b(get|post|put|delete|patch|head|options|trace)\s*\(\s*([A-Za-z_][A-Za-z0-9_]*)")
    for path, text in files.items():
        for match in re.finditer(r"\.route\s*\(", text):
            end = matching_close(text, text.find("(", match.start()))
            body = text[match.end() : end]
            path_match = re.search(r"['\"]([^'\"]+)['\"]", body)
            path_value = path_match.group(1) if path_match else None
            if path_value is None:
                route_const = re.search(r"routes::([A-Z][A-Z0-9_]*)", body)
                if route_const:
                    path_value = route_paths.get(route_const.group(1))
                else:
                    const_match = re.search(r"shared::protocol::([A-Z][A-Z0-9_]*)", body)
                    path_value = wire_paths.get(const_match.group(1)) if const_match else None
            if path_value is None:
                continue
            before = text[: match.start()]
            scope = "root-only" if before.rfind("let root = Router::new()") > before.rfind("let scoped = Router::new()") else "scoped"
            route_hit = hit(path, text, match.start())
            for method_match in method_pattern.finditer(body):
                routes.append(
                    Route(
                        method=method_match.group(1).upper(),
                        path=path_value,
                        handler=method_match.group(2),
                        scope=scope,
                        hit=route_hit,
                    )
                )
    return sorted(routes, key=lambda r: (r.path, r.method, r.hit.path, r.hit.line))


def cli_inventory(files: dict[Path, str]) -> tuple[list[tuple[str, str, Hit]], list[tuple[str, str, Hit]]]:
    commands: list[tuple[str, str, Hit]] = []
    options: list[tuple[str, str, Hit]] = []

    for path, text in files.items():
        if path.name == "Makefile":
            for match in re.finditer(r"^([A-Za-z0-9_.-]+):[^\n]*##\s*(.+)$", text, re.MULTILINE):
                commands.append(("make", f"make {match.group(1)}", hit(path, text, match.start(), match.group(0).strip())))

        if path.suffix != ".rs" or "/bin/" not in str(path):
            continue
        tool = cli_name(path)
        # Usage strings are the canonical public CLI surface. Looking only inside the
        # string avoids mistaking HTTP headers and status values for subcommands.
        in_usage = False
        offset = 0
        for line in text.splitlines(keepends=True):
            if "const USAGE:" in line:
                in_usage = True
            elif in_usage and line.strip() == '";':
                in_usage = False
            elif in_usage:
                stripped = line.strip()
                if stripped.startswith("usage: "):
                    invocation = stripped.removeprefix("usage: ").strip()
                    commands.append((tool, invocation, hit(path, text, offset, stripped)))
                elif stripped.startswith(("slopctl ", "slopworld ")):
                    # The launcher banner is prose (`slopworld - launch ...`), not a command.
                    if not re.match(r"(?:slopctl|slopworld)\s+-\s", stripped):
                        commands.append((tool, stripped, hit(path, text, offset, stripped)))
                option = re.match(
                    r"\s+((?:--[A-Za-z][A-Za-z0-9-]*(?:\s+[A-Z][A-Z0-9_<>.-]+)?|-h,\s+--help))\s{2,}(.+)$",
                    line.rstrip("\n"),
                )
                if option:
                    options.append((tool, f"{option.group(1)} — {option.group(2).strip()}", hit(path, text, offset, stripped)))
            offset += len(line)

    # The subcommands are also encoded in match arms; include one only if the usage text
    # forgot to mention it, keeping the generated list useful during an incomplete edit.
    slopctl = next((p for p in files if p.name == "slopctl.rs"), None)
    if slopctl is not None:
        text = files[slopctl]
        known = {row[1].split()[1] for row in commands if row[0] == "slopctl" and len(row[1].split()) > 1}
        for match in re.finditer(r'^\s*"([a-z][a-z0-9_-]*)"\s+if|command @ \(([^)]*)\)', text, re.MULTILINE):
            values = [match.group(1)] if match.group(1) else re.findall(r'"([a-z][a-z0-9_-]*)"', match.group(2))
            for value in values:
                if value in known:
                    continue
                commands.append(("slopctl", f"slopctl {value}", hit(slopctl, text, match.start())))
                known.add(value)

    return unique_rows(commands), unique_rows(options)


def unique_rows(rows: list[tuple[str, str, Hit]]) -> list[tuple[str, str, Hit]]:
    seen: set[tuple[str, str]] = set()
    output = []
    for row in sorted(rows, key=lambda r: (r[0], r[1], r[2].path, r[2].line)):
        key = (row[0], row[1])
        if key not in seen:
            seen.add(key)
            output.append(row)
    return output


def md_cell(value: str) -> str:
    return value.replace("|", "\\|").replace("\n", " ")


def source_link(item: Hit) -> str:
    return f"[`{item.path}:{item.line}`](./{item.path}#L{item.line})"


def render(files: dict[Path, str]) -> str:
    envs, dynamic = env_inventory(files)
    routes = api_routes(files)
    commands, options = cli_inventory(files)
    lines = [
        "# SlopWorld reference",
        "",
        "Generated by [`tools/reference.py`](./tools/reference.py). This is a static inventory of names and source locations; environment values are never read or written.",
        "",
        "## Environment variables",
        "",
        "| Name | Source references |",
        "| --- | --- |",
    ]
    for name, hits in envs.items():
        refs = ", ".join(source_link(item) for item in hits[:8])
        if len(hits) > 8:
            refs += f" (+{len(hits) - 8} more)"
        lines.append(f"| `{name}` | {refs} |")
    if dynamic:
        lines.extend(["", "### Dynamic environment handling", "", "These are patterns rather than single variable names:", ""])
        for item in dynamic:
            lines.append(f"- inherited or computed environment name at {source_link(item)}: `{md_cell(item.text)}`")

    lines.extend(["", "## API routes", "", "Routes declared by the daemon's Axum router. `scoped` routes are mounted in the grant-visible router; handler guards can impose a stricter access requirement.", "", "| Method | Path | Handler | Router scope | Source |", "| --- | --- | --- | --- | --- |"])
    for route in routes:
        lines.append(f"| `{route.method}` | `{route.path}` | `{route.handler}` | `{route.scope}` | {source_link(route.hit)} |")

    lines.extend(["", "## CLI commands", "", "### Commands", "", "| Tool | Invocation | Source |", "| --- | --- | --- |"])
    for tool, command, item in commands:
        lines.append(f"| `{tool}` | `{md_cell(command)}` | {source_link(item)} |")
    if options:
        lines.extend(["", "### Options", "", "| Tool | Option | Source |", "| --- | --- | --- |"])
        for tool, option, item in options:
            lines.append(f"| `{tool}` | `{md_cell(option)}` | {source_link(item)} |")

    lines.extend(["", "## Scanner scope", "", "The scanner reads tracked and untracked, non-ignored text files under the project, excluding generated build output and this generated file. It recognizes explicit Rust/service/Make environment access, `$VAR` expansion, Axum `.route(...)` declarations, Rust CLI usage text and Make targets.", ""])
    return "\n".join(lines)


def main() -> int:
    output = Path(sys.argv[1]).expanduser() if len(sys.argv) > 1 else DEFAULT_OUTPUT
    if not output.is_absolute():
        output = ROOT / output
    output.write_text(render(read_files()), encoding="utf-8")
    print(output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
